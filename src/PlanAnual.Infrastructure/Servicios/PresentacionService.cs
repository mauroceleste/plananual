using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Mail;

namespace PlanAnual.Infrastructure.Servicios;

public enum Rol { Empresa = 1, Subsecretaria = 2, Administrador = 3 }

/// <summary>Quién está operando. Sale de la sesión de UsersAccess (o del acceso de desarrollo).</summary>
public sealed record UsuarioSesion(int Id, string Nombre, string Email, IReadOnlyList<string> Cuits, Rol Rol)
{
    public bool VeTodo => Rol is Rol.Subsecretaria or Rol.Administrador;
    public bool PuedeVer(string cuit) => VeTodo || Cuits.Contains(cuit);
    /// <summary>La empresa edita lo suyo; Administración puede asistir a cualquier empresa (queda registrado a su nombre).</summary>
    public bool PuedeEditar(string cuit) => Rol == Rol.Administrador || (Rol == Rol.Empresa && Cuits.Contains(cuit));
    public bool EsAsistencia(string cuit) => Rol == Rol.Administrador && !Cuits.Contains(cuit);
}

/// <param name="Mes">0 para los años anuales; 1 a 12 en el primer año de la planilla (el de la convocatoria).</param>
public sealed record CeldaDto(int Anio, string Campo, decimal? Valor, int Mes = 0);

public sealed class OperacionInvalida(string mensaje) : Exception(mensaje);

public sealed class PresentacionService(PlanAnualDbContext db, IMailSender mail, IConfigurationLike cfg)
{
    public Task<Ciclo?> CicloAbiertoAsync() =>
        db.Ciclos.Where(c => c.Abierto).OrderByDescending(c => c.Anio).FirstOrDefaultAsync();

    /// <summary>Todas las convocatorias, la más nueva primero (para el selector de la Subsecretaría).</summary>
    public Task<List<Ciclo>> CiclosAsync() =>
        db.Ciclos.AsNoTracking().OrderByDescending(c => c.Anio).ToListAsync();

    /// <summary>La convocatoria del año pedido; si no existe o no se pide ninguna, la abierta.</summary>
    public async Task<Ciclo?> CicloParaConsultaAsync(int? anio)
    {
        if (anio is not null && await db.Ciclos.FirstOrDefaultAsync(c => c.Anio == anio) is { } c) return c;
        return await CicloAbiertoAsync();
    }

    /// <summary>Última versión de cada bloque (la que se muestra en "Mis áreas").</summary>
    public async Task<Dictionary<int, Presentacion>> UltimasPorBloqueAsync(int cicloId, IEnumerable<string> cuits, bool conAnexos = false)
    {
        var lista = cuits.ToList();
        IQueryable<Presentacion> q = db.Presentaciones.AsNoTracking();
        if (conAnexos) q = q.Include(p => p.Anexos);
        var todas = await q.Where(p => p.CicloId == cicloId && lista.Contains(p.Cuit)).ToListAsync();
        return todas.GroupBy(p => p.IdBloque).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Version).First());
    }

    public async Task<Presentacion> ObtenerAsync(long id, UsuarioSesion u, bool conDetalle = true)
    {
        IQueryable<Presentacion> q = db.Presentaciones.Include(p => p.Ciclo);
        if (conDetalle) q = q.Include(p => p.Valores).Include(p => p.Anexos);
        var p = await q.FirstOrDefaultAsync(x => x.Id == id) ?? throw new OperacionInvalida("La presentación no existe.");
        if (!u.PuedeVer(p.Cuit)) throw new UnauthorizedAccessException();
        return p;
    }

    public async Task<Presentacion> IniciarAsync(UsuarioSesion u, BloqueInfo bloque, TipoPlan tipo)
    {
        if (!u.PuedeEditar(bloque.Cuit)) throw new UnauthorizedAccessException();
        var ciclo = await CicloAbiertoAsync() ?? throw new OperacionInvalida("No hay una convocatoria abierta.");

        var existente = await db.Presentaciones.Where(p => p.CicloId == ciclo.Id && p.IdBloque == bloque.IdBloque && p.Cuit == bloque.Cuit)
            .OrderByDescending(p => p.Version).FirstOrDefaultAsync();
        if (existente is { Estado: EstadoPresentacion.Borrador }) return existente;
        if (existente is not null) throw new OperacionInvalida("Esta área ya fue enviada. Usá \"Rectificar\" para presentar una nueva versión.");

        var ahora = DateTime.Now;
        var p = new Presentacion
        {
            CicloId = ciclo.Id, Cuit = bloque.Cuit, RazonSocial = bloque.RazonSocial, IdBloque = bloque.IdBloque,
            NombreArea = bloque.NombreArea, NombreBloque = bloque.NombreBloque, TipoPlan = tipo,
            AnioDesde = ciclo.Anio, AnioHasta = ciclo.Anio + 4, Version = 1, Estado = EstadoPresentacion.Borrador,
            UsuarioId = u.Id, UsuarioNombre = u.Nombre, UsuarioEmail = u.Email, CreadaEn = ahora, ModificadaEn = ahora
        };
        RegistrarAsistencia(p, u);
        db.Presentaciones.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    /// <summary>Número de expediente de GDE de un área. Solo Administración; vale en cualquier estado (no es parte del plan) y vacío lo borra.</summary>
    public async Task GuardarExpedienteAsync(long id, UsuarioSesion u, string? expediente)
    {
        if (u.Rol != Rol.Administrador) throw new UnauthorizedAccessException();
        var p = await db.Presentaciones.FirstOrDefaultAsync(x => x.Id == id) ?? throw new OperacionInvalida("La presentación no existe.");
        var limpio = string.IsNullOrWhiteSpace(expediente) ? null : expediente.Trim();
        if (limpio is { Length: > 80 }) throw new OperacionInvalida("El número de expediente es demasiado largo (máximo 80 caracteres).");
        if (limpio is not null && limpio.Any(char.IsControl)) throw new OperacionInvalida("El número de expediente tiene caracteres no válidos.");
        p.ExpedienteGde = limpio;
        await db.SaveChangesAsync();
    }

    public async Task<Presentacion> RectificarAsync(long id, UsuarioSesion u)
    {
        var origen = await ObtenerAsync(id, u);
        if (!u.PuedeEditar(origen.Cuit)) throw new UnauthorizedAccessException();
        if (origen.Estado != EstadoPresentacion.Enviada) throw new OperacionInvalida("Solo se puede rectificar la última versión enviada.");
        if (await db.Presentaciones.AnyAsync(p => p.CicloId == origen.CicloId && p.IdBloque == origen.IdBloque && p.Cuit == origen.Cuit && p.Estado == EstadoPresentacion.Borrador))
            throw new OperacionInvalida("Ya hay una rectificatoria en borrador.");
        if (u.Rol != Rol.Administrador && await HabilitacionVigenteAsync(origen.CicloId, origen.Cuit, origen.IdBloque) is null)
            throw new OperacionInvalida("Para rectificar necesitás la autorización de la Subsecretaría. Pedila indicando el motivo.");

        var ahora = DateTime.Now;
        var nueva = new Presentacion
        {
            CicloId = origen.CicloId, Cuit = origen.Cuit, RazonSocial = origen.RazonSocial, IdBloque = origen.IdBloque,
            NombreArea = origen.NombreArea, NombreBloque = origen.NombreBloque, TipoPlan = origen.TipoPlan,
            AnioDesde = origen.AnioDesde, AnioHasta = origen.AnioHasta, Version = origen.Version + 1,
            Estado = EstadoPresentacion.Borrador, UsuarioId = u.Id, UsuarioNombre = u.Nombre, UsuarioEmail = u.Email,
            CreadaEn = ahora, ModificadaEn = ahora,
            ConflictosSuperficiarios = origen.ConflictosSuperficiarios, EquiposContratosFirmes = origen.EquiposContratosFirmes,
            EquiposPlazos = origen.EquiposPlazos, EquiposCostos = origen.EquiposCostos, ExpedienteGde = origen.ExpedienteGde,
            Valores = origen.Valores.Select(v => new PlanValor { Anio = v.Anio, Mes = v.Mes, Campo = v.Campo, Valor = v.Valor }).ToList(),
            Anexos = origen.Anexos.Select(a => new Anexo
            {
                Tipo = a.Tipo, NombreArchivo = a.NombreArchivo, RutaAlmacenada = a.RutaAlmacenada,
                Tamano = a.Tamano, Sha256 = a.Sha256, SubidoEn = a.SubidoEn
            }).ToList()
        };
        RegistrarAsistencia(nueva, u);
        db.Presentaciones.Add(nueva);
        await db.SaveChangesAsync();
        return nueva;
    }

    /// <summary>
    /// Guarda celdas sueltas y devuelve las filas recalculadas que se tocaron, por clave de fila (año*100+mes). Si se tocó un mes,
    /// también vuelve el subtotal anual de ese año (clave año*100).
    /// </summary>
    public async Task<Dictionary<int, Dictionary<string, decimal>>> GuardarCeldasAsync(long id, UsuarioSesion u, IReadOnlyCollection<CeldaDto> celdas)
    {
        var p = await ObtenerAsync(id, u);
        await ExigirEditableAsync(p, u);
        var campos = CatalogoCampos.Para(p.TipoPlan).ToDictionary(c => c.Codigo);

        foreach (var c in celdas)
        {
            if (!campos.TryGetValue(c.Campo, out var def) || def.EsCalculado) throw new OperacionInvalida($"Campo no editable: {c.Campo}.");
            if (c.Anio < p.AnioDesde || c.Anio > p.AnioHasta) throw new OperacionInvalida($"El año {c.Anio} está fuera del rango de la planilla.");
            if (c.Anio == p.Ciclo!.Anio ? c.Mes is < 1 or > 12 : c.Mes != 0)
                throw new OperacionInvalida(c.Anio == p.Ciclo.Anio ? $"El año {c.Anio} se carga mes por mes." : $"A partir de {p.Ciclo.Anio + 1} los valores son anuales.");
            if (c.Valor is { } v && Math.Abs(v) > 1_000_000_000m) throw new OperacionInvalida("El valor es demasiado grande.");
            if (c.Valor is { } val && ReglasDeDatos.Motivo(def, val) is { } motivo) throw new OperacionInvalida(motivo);

            var actual = p.Valores.FirstOrDefault(x => x.Anio == c.Anio && x.Mes == c.Mes && x.Campo == c.Campo);
            if (actual is null && c.Valor is null) continue;
            if (actual is null) p.Valores.Add(new PlanValor { PresentacionId = p.Id, Anio = c.Anio, Mes = c.Mes, Campo = c.Campo, Valor = c.Valor });
            else if (c.Valor is null) { db.PlanValores.Remove(actual); p.Valores.Remove(actual); }
            else actual.Valor = c.Valor;
        }
        p.ModificadaEn = DateTime.Now;
        RegistrarAsistencia(p, u);
        await db.SaveChangesAsync();

        var tocadas = celdas.Select(c => Calculadora.Clave(c.Anio, c.Mes)).Concat(celdas.Where(c => c.Mes > 0).Select(c => Calculadora.Clave(c.Anio, 0))).ToHashSet();
        var calc = Calculadora.CalcularPeriodos(p.TipoPlan, p.Valores);
        return tocadas.ToDictionary(k => k, k => calc.GetValueOrDefault(k) ?? []);
    }

    /// <summary>Guarda los cuatro textos del bloque 4 (restricciones a la actividad).</summary>
    public async Task GuardarRestriccionesAsync(long id, UsuarioSesion u, string? conflictos, string? contratosFirmes, string? plazos, string? costos)
    {
        var p = await ObtenerAsync(id, u, conDetalle: false);
        await ExigirEditableAsync(p, u);
        static string? Limpio(string? t, string cual)
        {
            t = t?.Trim();
            if (string.IsNullOrEmpty(t)) return null;
            if (t.Length > 4000) throw new OperacionInvalida($"«{cual}» supera los 4000 caracteres.");
            return t;
        }
        p.ConflictosSuperficiarios = Limpio(conflictos, Validador.TextosBloque4[0].Titulo);
        p.EquiposContratosFirmes = Limpio(contratosFirmes, Validador.TextosBloque4[1].Titulo);
        p.EquiposPlazos = Limpio(plazos, Validador.TextosBloque4[2].Titulo);
        p.EquiposCostos = Limpio(costos, Validador.TextosBloque4[3].Titulo);
        p.ModificadaEn = DateTime.Now;
        RegistrarAsistencia(p, u);
        await db.SaveChangesAsync();
    }

    public async Task CambiarRangoAsync(long id, UsuarioSesion u, int desde, int hasta)
    {
        var p = await ObtenerAsync(id, u);
        await ExigirEditableAsync(p, u);
        if (desde > hasta || hasta - desde > 60) throw new OperacionInvalida("Rango de años inválido.");
        if (desde != p.Ciclo!.Anio) throw new OperacionInvalida($"La planilla comienza en {p.Ciclo.Anio}, el año de la convocatoria (se carga mes por mes).");
        p.AnioDesde = desde; p.AnioHasta = hasta;
        var fuera = p.Valores.Where(v => v.Anio < desde || v.Anio > hasta).ToList();
        db.PlanValores.RemoveRange(fuera);
        p.ModificadaEn = DateTime.Now;
        RegistrarAsistencia(p, u);
        await db.SaveChangesAsync();
    }

    public async Task<Anexo> AgregarAnexoAsync(long id, UsuarioSesion u, string tipo, string nombre, string ruta, long tamano, string sha256)
    {
        var p = await ObtenerAsync(id, u, conDetalle: false);
        await ExigirEditableAsync(p, u);
        if (AnexosRequeridos.Buscar(tipo) is null) throw new OperacionInvalida("Tipo de anexo desconocido.");
        RegistrarAsistencia(p, u);
        var a = new Anexo { PresentacionId = id, Tipo = tipo, NombreArchivo = nombre, RutaAlmacenada = ruta, Tamano = tamano, Sha256 = sha256, SubidoEn = DateTime.Now };
        db.Anexos.Add(a);
        await db.SaveChangesAsync();
        return a;
    }

    public async Task QuitarAnexoAsync(long id, long anexoId, UsuarioSesion u)
    {
        var p = await ObtenerAsync(id, u, conDetalle: false);
        await ExigirEditableAsync(p, u);
        var a = await db.Anexos.FirstOrDefaultAsync(x => x.Id == anexoId && x.PresentacionId == id);
        if (a is null) return;
        RegistrarAsistencia(p, u);
        db.Anexos.Remove(a);
        await db.SaveChangesAsync();
    }

    public async Task<List<Observacion>> RevisarAsync(long id, UsuarioSesion u)
    {
        var p = await ObtenerAsync(id, u);
        return Validador.Validar(p, p.Anexos.Select(a => new AnexoTipoPresentado(a.Tipo)).ToList());
    }

    public async Task<Presentacion> EnviarAsync(long id, UsuarioSesion u, string urlComprobante)
    {
        var p = await ObtenerAsync(id, u);
        await ExigirEditableAsync(p, u);
        var obs = Validador.Validar(p, p.Anexos.Select(a => new AnexoTipoPresentado(a.Tipo)).ToList());
        if (obs.Any(o => o.EsError)) throw new OperacionInvalida("Hay errores que corregir antes de enviar.");
        if (p.Ciclo is { } c && DateOnly.FromDateTime(DateTime.Today) > c.LimiteVigente)
            p.Observaciones = $"Presentada fuera de plazo (vencía el {c.LimiteVigente:dd/MM/yyyy}).";

        var anteriores = await db.Presentaciones.Where(x => x.CicloId == p.CicloId && x.IdBloque == p.IdBloque && x.Cuit == p.Cuit && x.Estado == EstadoPresentacion.Enviada).ToListAsync();
        foreach (var a in anteriores) a.Estado = EstadoPresentacion.Reemplazada;

        p.Estado = EstadoPresentacion.Enviada;
        p.EnviadaPor = u.Nombre;
        RegistrarAsistencia(p, u);
        p.EnviadaEn = DateTime.Now;
        p.ModificadaEn = p.EnviadaEn.Value;
        p.NumeroComprobante = $"PAI-{p.Ciclo!.Anio}-{p.Id:000000}-v{p.Version}";
        p.HuellaEnvio = Huella(p);

        // La rectificatoria consume la habilitación: para corregir de nuevo hay que volver a pedir autorización.
        if (p.Version > 1)
            foreach (var s in await db.SolicitudesRectificacion.Where(x => x.CicloId == p.CicloId && x.Cuit == p.Cuit && x.IdBloque == p.IdBloque && x.Estado == EstadoSolicitud.Habilitada).ToListAsync())
                s.Estado = EstadoSolicitud.Usada;
        await db.SaveChangesAsync();

        await EnviarMailAsync(p, u, urlComprobante);
        return p;
    }

    public sealed record ResultadoEnvioFinal(int Enviadas, IReadOnlyList<string> Problemas);

    /// <summary>
    /// Envía de una vez todas las áreas en borrador de la empresa. Es todo o nada: si falta iniciar un área o alguna
    /// tiene errores, no se envía ninguna y se devuelve la lista de problemas.
    /// </summary>
    public async Task<ResultadoEnvioFinal> EnviarFinalAsync(UsuarioSesion u, IReadOnlyList<BloqueInfo> bloques, Func<long, string> urlComprobante)
    {
        if (bloques.Count == 0) return new(0, ["No hay áreas para enviar."]);
        if (bloques.Any(b => !u.PuedeEditar(b.Cuit))) throw new UnauthorizedAccessException();
        var ciclo = await CicloAbiertoAsync() ?? throw new OperacionInvalida("No hay una convocatoria abierta.");

        var cuits = bloques.Select(b => b.Cuit).Distinct().ToList();
        var ultimas = (await db.Presentaciones.AsNoTracking().Where(p => p.CicloId == ciclo.Id && cuits.Contains(p.Cuit)).ToListAsync())
            .GroupBy(p => (p.Cuit, p.IdBloque)).Select(g => g.OrderByDescending(p => p.Version).First()).ToList();

        // Presentación final = todos los requisitos de la nota cumplidos en todas las áreas. Si falta algo, no sale ninguna.
        var pendientes = await PendientesAsync(ultimas.Where(p => p.Estado == EstadoPresentacion.Borrador).Select(p => p.Id));
        var problemas = new List<string>();
        var aEnviar = new List<long>();
        foreach (var b in bloques.OrderBy(b => b.Nombre))
        {
            var p = ultimas.FirstOrDefault(x => x.Cuit == b.Cuit && x.IdBloque == b.IdBloque);
            if (p is null) { problemas.Add($"{b.Nombre}: todavía no la iniciaste."); continue; }
            if (p.Estado != EstadoPresentacion.Borrador) continue;   // ya enviada
            if (pendientes[p.Id].Count > 0) { problemas.Add($"{b.Nombre}: {string.Join(" · ", pendientes[p.Id])}"); continue; }
            if (p.Version > 1 && u.Rol != Rol.Administrador && await HabilitacionVigenteAsync(p.CicloId, p.Cuit, p.IdBloque) is null)
            { problemas.Add($"{b.Nombre}: la autorización para rectificar venció. Pedila de nuevo."); continue; }
            aEnviar.Add(p.Id);
        }
        if (problemas.Count > 0) return new(0, problemas);
        if (aEnviar.Count == 0) return new(0, ["No hay áreas en borrador para enviar: todas ya fueron enviadas."]);

        foreach (var id in aEnviar) await EnviarAsync(id, u, urlComprobante(id));
        return new(aEnviar.Count, []);
    }

    // ---------- Planilla Excel de carga (todas las áreas) e importación ----------

    public sealed record ResultadoImportacion(IReadOnlyList<ErrorImportacion> Errores, bool Truncado, IReadOnlyList<string> Areas);

    /// <summary>Excel vacío con todas las áreas de la empresa (las que ya tienen tipo de actividad definido).</summary>
    public async Task<byte[]> PlantillaAsync(UsuarioSesion u, IReadOnlyList<BloqueInfo> bloques, IReadOnlyDictionary<int, TipoPlan> tipos)
    {
        if (bloques.Count == 0) throw new OperacionInvalida("No hay áreas para armar la planilla.");
        if (bloques.Any(b => !u.PuedeEditar(b.Cuit))) throw new UnauthorizedAccessException();
        var ciclo = await CicloAbiertoAsync() ?? throw new OperacionInvalida("No hay una convocatoria abierta.");
        var areas = bloques.Where(b => tipos.ContainsKey(b.IdBloque)).Select(b => new AreaPlantilla(b.IdBloque, b.Nombre, tipos[b.IdBloque])).ToList();
        if (areas.Count == 0) throw new OperacionInvalida("La Subsecretaría todavía no definió el tipo de actividad de tus áreas: no se puede armar la planilla.");
        return PlanillaExcel.Generar(ciclo, bloques[0].RazonSocial, areas, bloques.Where(b => !tipos.ContainsKey(b.IdBloque)).Select(b => b.Nombre).OrderBy(n => n).ToList());
    }

    /// <summary>
    /// Lee el Excel y, si no tiene ningún error, carga los valores como borrador de cada área incluida (reemplazando lo que hubiera).
    /// Es todo o nada: con un solo error no se toca nada y se devuelve la lista completa.
    /// </summary>
    public async Task<ResultadoImportacion> ImportarAsync(UsuarioSesion u, IReadOnlyList<BloqueInfo> bloques, IReadOnlyDictionary<int, TipoPlan> tipos, Stream archivo)
    {
        if (bloques.Any(b => !u.PuedeEditar(b.Cuit))) throw new UnauthorizedAccessException();
        var ciclo = await CicloAbiertoAsync() ?? throw new OperacionInvalida("No hay una convocatoria abierta.");
        var permitidas = bloques.Where(b => tipos.ContainsKey(b.IdBloque)).ToDictionary(b => b.IdBloque, b => new AreaPlantilla(b.IdBloque, b.Nombre, tipos[b.IdBloque]));

        var lectura = PlanillaExcel.Leer(archivo, permitidas, ciclo.Anio);
        var errores = lectura.Errores.ToList();
        if (errores.Count > 0) return new ResultadoImportacion(errores, lectura.Truncado, []);

        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var area in lectura.Areas)
        {
            var b = bloques.First(x => x.IdBloque == area.IdBloque);
            var p = await db.Presentaciones.Include(x => x.Ciclo).Include(x => x.Valores)
                .Where(x => x.CicloId == ciclo.Id && x.Cuit == b.Cuit && x.IdBloque == b.IdBloque).OrderByDescending(x => x.Version).FirstOrDefaultAsync();
            if (p is { Estado: not EstadoPresentacion.Borrador })
            { errores.Add(new("(archivo)", null, null, $"{area.Nombre}: ya fue enviada. Para modificarla pedí una rectificación y, cuando te la habiliten, importá de nuevo.")); continue; }
            if (p is null) { await IniciarAsync(u, b, area.Tipo); p = await db.Presentaciones.Include(x => x.Ciclo).Include(x => x.Valores).FirstAsync(x => x.CicloId == ciclo.Id && x.Cuit == b.Cuit && x.IdBloque == b.IdBloque); }
            else
            {
                try { await ExigirEditableAsync(p, u); }
                catch (OperacionInvalida ex) { errores.Add(new("(archivo)", null, null, $"{area.Nombre}: {ex.Message}")); continue; }
            }

            // Se actualiza en el lugar (no se borra y se vuelve a crear la misma clave).
            var nuevos = area.Valores.ToDictionary(v => (v.Anio, v.Mes, v.Campo), v => v.Valor);
            foreach (var viejo in p.Valores.ToList())
            {
                if (nuevos.Remove((viejo.Anio, viejo.Mes, viejo.Campo), out var valor)) viejo.Valor = valor;
                else { db.PlanValores.Remove(viejo); p.Valores.Remove(viejo); }
            }
            foreach (var ((anio, mes, campo), valor) in nuevos) p.Valores.Add(new PlanValor { PresentacionId = p.Id, Anio = anio, Mes = mes, Campo = campo, Valor = valor });
            p.TipoPlan = area.Tipo;
            p.AnioDesde = ciclo.Anio; p.AnioHasta = Math.Max(ciclo.Anio, area.AnioHasta);
            p.ModificadaEn = DateTime.Now;
            RegistrarAsistencia(p, u);
        }
        if (errores.Count > 0) return new ResultadoImportacion(errores, false, []);   // al salir sin confirmar, la transacción se deshace
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new ResultadoImportacion([], false, lectura.Areas.Select(a => a.Nombre).OrderBy(n => n).ToList());
    }

    /// <summary>Qué le falta a cada borrador para poder entrar en la presentación final: errores y requisitos de la nota.</summary>
    public async Task<Dictionary<long, List<string>>> PendientesAsync(IEnumerable<long> presentacionIds)
    {
        var ids = presentacionIds.ToList();
        var ps = await db.Presentaciones.AsNoTracking().AsSplitQuery()
            .Include(p => p.Ciclo).Include(p => p.Valores).Include(p => p.Anexos).Where(p => ids.Contains(p.Id)).ToListAsync();
        return ps.ToDictionary(p => p.Id, p => Validador.Validar(p, p.Anexos.Select(a => new AnexoTipoPresentado(a.Tipo)).ToList())
            .Where(o => o.Bloquea).Select(o => (o.Anio is not null ? o.Anio + ": " : "") + o.Mensaje).ToList());
    }

    // ---------- Rectificación con autorización ----------

    /// <summary>Habilitación vigente (otorgada y sin vencer) para rectificar esa área, o null.</summary>
    public Task<SolicitudRectificacion?> HabilitacionVigenteAsync(int cicloId, string cuit, int idBloque)
    {
        var ahora = DateTime.Now;
        return db.SolicitudesRectificacion.AsNoTracking()
            .Where(s => s.CicloId == cicloId && s.Cuit == cuit && s.IdBloque == idBloque && s.Estado == EstadoSolicitud.Habilitada && s.HabilitadaHasta > ahora)
            .OrderByDescending(s => s.HabilitadaHasta).FirstOrDefaultAsync();
    }

    /// <summary>La solicitud más reciente de cada área (para mostrar el estado en "Mis áreas").</summary>
    public async Task<Dictionary<int, SolicitudRectificacion>> UltimasSolicitudesAsync(int cicloId, IEnumerable<string> cuits)
    {
        var lista = cuits.ToList();
        var todas = await db.SolicitudesRectificacion.AsNoTracking().Where(s => s.CicloId == cicloId && lista.Contains(s.Cuit)).ToListAsync();
        return todas.GroupBy(s => s.IdBloque).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.SolicitadaEn).First());
    }

    public async Task<SolicitudRectificacion> SolicitarRectificacionAsync(long presentacionId, UsuarioSesion u, string? motivo, string urlAdministracion)
    {
        var origen = await ObtenerAsync(presentacionId, u, conDetalle: false);
        if (!u.PuedeEditar(origen.Cuit)) throw new UnauthorizedAccessException();
        if (origen.Estado != EstadoPresentacion.Enviada) throw new OperacionInvalida("Solo se puede pedir rectificar la última versión enviada.");
        motivo = motivo?.Trim() ?? "";
        if (motivo.Length < 10) throw new OperacionInvalida("Contanos el motivo de la modificación (al menos 10 caracteres).");
        if (motivo.Length > 1000) throw new OperacionInvalida("El motivo es demasiado largo (máximo 1000 caracteres).");

        var abierta = await db.SolicitudesRectificacion.AnyAsync(s => s.CicloId == origen.CicloId && s.Cuit == origen.Cuit && s.IdBloque == origen.IdBloque
            && (s.Estado == EstadoSolicitud.Pendiente || (s.Estado == EstadoSolicitud.Habilitada && s.HabilitadaHasta > DateTime.Now)));
        if (abierta) throw new OperacionInvalida("Ya hay un pedido de rectificación pendiente o habilitado para esta área.");

        var s = new SolicitudRectificacion
        {
            CicloId = origen.CicloId, Cuit = origen.Cuit, RazonSocial = origen.RazonSocial, IdBloque = origen.IdBloque, NombreArea = origen.NombreArea,
            PresentacionId = origen.Id, Motivo = motivo, SolicitadaPor = u.Nombre, SolicitadaEmail = u.Email, SolicitadaEn = DateTime.Now
        };
        db.SolicitudesRectificacion.Add(s);
        await db.SaveChangesAsync();

        Func<string?, string> h = WebUtility.HtmlEncode!;
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;color:#111C26;max-width:560px">
            <h2 style="color:#00374C;margin:0 0 8px">Pedido de rectificación</h2>
            <p><strong>{h(origen.RazonSocial)}</strong> (CUIT {h(origen.Cuit)}) pide modificar la presentación de <strong>{h(origen.NombreArea)}</strong> (versión {origen.Version} enviada).</p>
            <p style="margin:12px 0 4px;color:#5F7185">Motivo:</p>
            <blockquote style="margin:0;padding:8px 12px;border-left:3px solid #00374C;background:#F3F5F9">{h(motivo).Replace("\n", "<br>")}</blockquote>
            <p>Solicitado por {h(u.Nombre)} ({h(u.Email)}) el {s.SolicitadaEn:dd/MM/yyyy HH:mm}.</p>
            <p>Si la autorizás, la empresa puede rectificar durante {SolicitudRectificacion.DiasDeHabilitacion} días. <a href="{h(urlAdministracion)}">Revisar y habilitar</a></p>
            <p style="color:#5F7185;font-size:12px">Subsecretaría de Hidrocarburos · Provincia del Neuquén. Este mensaje es automático.</p></div>
            """;
        var asunto = $"Pedido de rectificación · {origen.RazonSocial} · {origen.NombreArea}";
        await EnviarYRegistrarAsync(origen.Id, cfg.Lista("Mail:Subsecretaria"), asunto, html);
        return s;
    }

    public async Task<List<SolicitudRectificacion>> ListarSolicitudesAsync(int tomar = 100) =>
        await db.SolicitudesRectificacion.AsNoTracking()
            .OrderBy(s => s.Estado == EstadoSolicitud.Pendiente ? 0 : 1).ThenByDescending(s => s.SolicitadaEn).Take(tomar).ToListAsync();

    public async Task<SolicitudRectificacion> HabilitarAsync(long solicitudId, UsuarioSesion u, string urlMisAreas)
    {
        if (u.Rol != Rol.Administrador) throw new UnauthorizedAccessException();
        var s = await db.SolicitudesRectificacion.FirstOrDefaultAsync(x => x.Id == solicitudId) ?? throw new OperacionInvalida("La solicitud no existe.");
        if (s.Estado != EstadoSolicitud.Pendiente) throw new OperacionInvalida("Esta solicitud ya fue resuelta.");
        s.Estado = EstadoSolicitud.Habilitada;
        s.ResueltaPor = u.Nombre; s.ResueltaEn = DateTime.Now;
        s.HabilitadaHasta = s.ResueltaEn.Value.AddDays(SolicitudRectificacion.DiasDeHabilitacion);
        await db.SaveChangesAsync();

        Func<string?, string> h = WebUtility.HtmlEncode!;
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;color:#111C26;max-width:560px">
            <h2 style="color:#00374C;margin:0 0 8px">Rectificación autorizada</h2>
            <p>La Subsecretaría autorizó la rectificación de <strong>{h(s.NombreArea)}</strong>. Podés presentar la nueva versión hasta el <strong>{s.HabilitadaHasta:dd/MM/yyyy HH:mm}</strong>; pasado ese momento hay que pedir una nueva autorización.</p>
            <p><a href="{h(urlMisAreas)}">Ir a Mis áreas</a></p>
            <p style="color:#5F7185;font-size:12px">Subsecretaría de Hidrocarburos · Provincia del Neuquén. Este mensaje es automático.</p></div>
            """;
        await EnviarYRegistrarAsync(s.PresentacionId, [s.SolicitadaEmail], $"Rectificación autorizada · {s.NombreArea}", html);
        return s;
    }

    public async Task<SolicitudRectificacion> RechazarAsync(long solicitudId, UsuarioSesion u)
    {
        if (u.Rol != Rol.Administrador) throw new UnauthorizedAccessException();
        var s = await db.SolicitudesRectificacion.FirstOrDefaultAsync(x => x.Id == solicitudId) ?? throw new OperacionInvalida("La solicitud no existe.");
        if (s.Estado != EstadoSolicitud.Pendiente) throw new OperacionInvalida("Esta solicitud ya fue resuelta.");
        s.Estado = EstadoSolicitud.Rechazada;
        s.ResueltaPor = u.Nombre; s.ResueltaEn = DateTime.Now;
        await db.SaveChangesAsync();

        Func<string?, string> h = WebUtility.HtmlEncode!;
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;color:#111C26;max-width:560px">
            <h2 style="color:#00374C;margin:0 0 8px">Rectificación no autorizada</h2>
            <p>La Subsecretaría no autorizó por ahora la rectificación de <strong>{h(s.NombreArea)}</strong>. Si necesitás más información, comunicate con la Subsecretaría de Hidrocarburos.</p>
            <p style="color:#5F7185;font-size:12px">Subsecretaría de Hidrocarburos · Provincia del Neuquén. Este mensaje es automático.</p></div>
            """;
        await EnviarYRegistrarAsync(s.PresentacionId, [s.SolicitadaEmail], $"Rectificación no autorizada · {s.NombreArea}", html);
        return s;
    }

    async Task EnviarYRegistrarAsync(long presentacionId, IEnumerable<string> para, string asunto, string html)
    {
        var r = await mail.EnviarAsync(para, asunto, html);
        var destinos = string.Join("; ", r.Destinatarios) + (r.Desviado && r.Destinatarios.Count > 0 ? " (modo de prueba: no llegó al destinatario real)" : "");
        db.EnviosMail.Add(new EnvioMail { PresentacionId = presentacionId, Destinatarios = destinos, Asunto = asunto, Enviado = r.Ok, Error = r.Error, Fecha = DateTime.Now });
        await db.SaveChangesAsync();
    }

    public static string Huella(Presentacion p)
    {
        var sb = new StringBuilder();
        foreach (var v in p.Valores.Where(v => v.Valor.HasValue).OrderBy(v => v.Anio).ThenBy(v => v.Mes).ThenBy(v => v.Campo, StringComparer.Ordinal))
            sb.Append(v.Anio).Append(v.Mes > 0 ? "-" + v.Mes : "").Append('|').Append(v.Campo).Append('|').Append(v.Valor!.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        foreach (var a in p.Anexos.OrderBy(a => a.Sha256, StringComparer.Ordinal)) sb.Append("A|").Append(a.Tipo).Append('|').Append(a.Sha256).Append('\n');
        foreach (var (titulo, valor) in Validador.TextosBloque4)
            if (!string.IsNullOrWhiteSpace(valor(p))) sb.Append("T|").Append(titulo).Append('|').Append(valor(p)!.Trim()).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    async Task EnviarMailAsync(Presentacion p, UsuarioSesion u, string url)
    {
        var calc = Calculadora.CalcularPlan(p.TipoPlan, p.Valores);
        string Fila(string et, string v) => $"<tr><td style=\"padding:4px 12px 4px 0;color:#5F7185\">{et}</td><td><strong>{WebUtility.HtmlEncode(v)}</strong></td></tr>";
        var anioFirme = p.Ciclo!.Anio;
        var inv = calc.GetValueOrDefault(anioFirme)?.GetValueOrDefault("inv_total") ?? 0;
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;color:#111C26;max-width:560px">
            <h2 style="color:#00374C;margin:0 0 8px">Plan de Inversiones {anioFirme}: presentación recibida</h2>
            <p>Se registró la presentación de <strong>{WebUtility.HtmlEncode(p.RazonSocial)}</strong> para <strong>{WebUtility.HtmlEncode(p.NombreArea)}{(p.NombreArea == p.NombreBloque ? "" : " - " + WebUtility.HtmlEncode(p.NombreBloque))}</strong>.</p>
            <table>{Fila("Comprobante", p.NumeroComprobante!)}{Fila("Tipo de plan", p.TipoPlan == TipoPlan.Cench ? "CENCH (no convencional)" : "Convencional")}
            {Fila("Versión", p.Version.ToString())}{Fila("Enviada", p.EnviadaEn!.Value.ToString("dd/MM/yyyy HH:mm"))}{Fila("Enviada por", p.EnviadaPor ?? p.UsuarioNombre)}
            {Fila($"Inversión {anioFirme}", inv.ToString("N2", new System.Globalization.CultureInfo("es-AR")) + " MMUSD")}{Fila("Años cubiertos", $"{p.AnioDesde} a {p.AnioHasta}")}
            {Fila("Anexos adjuntos", p.Anexos.Count.ToString())}{Fila("Huella (SHA-256)", p.HuellaEnvio![..16] + "…")}</table>
            {(p.AsistidaPor is null ? "" : $"<p>Cargada con asistencia de Administración ({WebUtility.HtmlEncode(p.AsistidaPor)}), en nombre de la empresa.</p>")}
            {(p.Observaciones is null ? "" : $"<p style=\"color:#B3382C\">{WebUtility.HtmlEncode(p.Observaciones)}</p>")}
            <p><a href="{WebUtility.HtmlEncode(url)}">Ver el comprobante</a></p>
            <p style="color:#5F7185;font-size:12px">Subsecretaría de Hidrocarburos · Provincia del Neuquén. Este mensaje es automático.</p></div>
            """;

        var para = new List<string> { u.Email, p.UsuarioEmail };   // quien envía y quien inició la planilla
        para.AddRange(cfg.Lista("Mail:Subsecretaria"));
        var asunto = $"Plan de Inversiones {anioFirme} · {p.NombreArea} · {p.NumeroComprobante}";
        var r = await mail.EnviarAsync(para, asunto, html);

        // Se registra a quién fue DE VERDAD (en modo de prueba el envío se desvía a la lista de prueba).
        var destinos = string.Join("; ", r.Destinatarios) + (r.Desviado && r.Destinatarios.Count > 0 ? " (modo de prueba: no llegó a la empresa)" : "");
        db.EnviosMail.Add(new EnvioMail { PresentacionId = p.Id, Destinatarios = destinos, Asunto = asunto, Enviado = r.Ok, Error = r.Error, Fecha = DateTime.Now });
        await db.SaveChangesAsync();
    }

    /// <summary>Se edita solo un borrador; si es una rectificatoria, la empresa además necesita la habilitación vigente.</summary>
    async Task ExigirEditableAsync(Presentacion p, UsuarioSesion u)
    {
        if (!u.PuedeEditar(p.Cuit)) throw new UnauthorizedAccessException();
        if (p.Estado != EstadoPresentacion.Borrador) throw new OperacionInvalida("La presentación ya fue enviada y no se puede modificar.");
        if (p.Version > 1 && u.Rol != Rol.Administrador && await HabilitacionVigenteAsync(p.CicloId, p.Cuit, p.IdBloque) is null)
            throw new OperacionInvalida("La autorización para rectificar venció. Pedila de nuevo a la Subsecretaría.");
    }

    /// <summary>Si Administración toca la planilla de una empresa, queda constancia con su nombre.</summary>
    static void RegistrarAsistencia(Presentacion p, UsuarioSesion u)
    {
        if (u.EsAsistencia(p.Cuit)) p.AsistidaPor = u.Nombre;
    }
}

/// <summary>Lectura mínima de configuración para no atar este servicio a IConfiguration.</summary>
public interface IConfigurationLike { IEnumerable<string> Lista(string clave); }
