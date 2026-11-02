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

public sealed record CeldaDto(int Anio, string Campo, decimal? Valor);

public sealed class OperacionInvalida(string mensaje) : Exception(mensaje);

public sealed class PresentacionService(PlanAnualDbContext db, IMailSender mail, IConfigurationLike cfg)
{
    public Task<Ciclo?> CicloAbiertoAsync() =>
        db.Ciclos.Where(c => c.Abierto).OrderByDescending(c => c.Anio).FirstOrDefaultAsync();

    /// <summary>Última versión de cada bloque (la que se muestra en "Mis áreas").</summary>
    public async Task<Dictionary<int, Presentacion>> UltimasPorBloqueAsync(int cicloId, IEnumerable<string> cuits)
    {
        var lista = cuits.ToList();
        var todas = await db.Presentaciones.AsNoTracking()
            .Where(p => p.CicloId == cicloId && lista.Contains(p.Cuit)).ToListAsync();
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

    public async Task<Presentacion> RectificarAsync(long id, UsuarioSesion u)
    {
        var origen = await ObtenerAsync(id, u);
        if (!u.PuedeEditar(origen.Cuit)) throw new UnauthorizedAccessException();
        if (origen.Estado != EstadoPresentacion.Enviada) throw new OperacionInvalida("Solo se puede rectificar la última versión enviada.");
        if (await db.Presentaciones.AnyAsync(p => p.CicloId == origen.CicloId && p.IdBloque == origen.IdBloque && p.Cuit == origen.Cuit && p.Estado == EstadoPresentacion.Borrador))
            throw new OperacionInvalida("Ya hay una rectificatoria en borrador.");

        var ahora = DateTime.Now;
        var nueva = new Presentacion
        {
            CicloId = origen.CicloId, Cuit = origen.Cuit, RazonSocial = origen.RazonSocial, IdBloque = origen.IdBloque,
            NombreArea = origen.NombreArea, NombreBloque = origen.NombreBloque, TipoPlan = origen.TipoPlan,
            AnioDesde = origen.AnioDesde, AnioHasta = origen.AnioHasta, Version = origen.Version + 1,
            Estado = EstadoPresentacion.Borrador, UsuarioId = u.Id, UsuarioNombre = u.Nombre, UsuarioEmail = u.Email,
            CreadaEn = ahora, ModificadaEn = ahora,
            Valores = origen.Valores.Select(v => new PlanValor { Anio = v.Anio, Campo = v.Campo, Valor = v.Valor }).ToList(),
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

    /// <summary>Guarda celdas sueltas y devuelve las filas recalculadas de los años tocados.</summary>
    public async Task<Dictionary<int, Dictionary<string, decimal>>> GuardarCeldasAsync(long id, UsuarioSesion u, IReadOnlyCollection<CeldaDto> celdas)
    {
        var p = await ObtenerAsync(id, u);
        ExigirBorrador(p, u);
        var campos = CatalogoCampos.Para(p.TipoPlan).ToDictionary(c => c.Codigo);

        foreach (var c in celdas)
        {
            if (!campos.TryGetValue(c.Campo, out var def) || def.EsCalculado) throw new OperacionInvalida($"Campo no editable: {c.Campo}.");
            if (c.Anio < p.AnioDesde || c.Anio > p.AnioHasta) throw new OperacionInvalida($"El año {c.Anio} está fuera del rango de la planilla.");
            if (c.Valor is { } v && Math.Abs(v) > 1_000_000_000m) throw new OperacionInvalida("El valor es demasiado grande.");
            if (c.Valor is { } val && ReglasDeDatos.Motivo(def, val) is { } motivo) throw new OperacionInvalida(motivo);

            var actual = p.Valores.FirstOrDefault(x => x.Anio == c.Anio && x.Campo == c.Campo);
            if (actual is null && c.Valor is null) continue;
            if (actual is null) p.Valores.Add(new PlanValor { PresentacionId = p.Id, Anio = c.Anio, Campo = c.Campo, Valor = c.Valor });
            else if (c.Valor is null) { db.PlanValores.Remove(actual); p.Valores.Remove(actual); }
            else actual.Valor = c.Valor;
        }
        p.ModificadaEn = DateTime.Now;
        RegistrarAsistencia(p, u);
        await db.SaveChangesAsync();

        var tocados = celdas.Select(c => c.Anio).ToHashSet();
        var calc = Calculadora.CalcularPlan(p.TipoPlan, p.Valores);
        return calc.Where(kv => tocados.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public async Task CambiarRangoAsync(long id, UsuarioSesion u, int desde, int hasta)
    {
        var p = await ObtenerAsync(id, u);
        ExigirBorrador(p, u);
        if (desde > hasta || hasta - desde > 60) throw new OperacionInvalida("Rango de años inválido.");
        if (desde > p.Ciclo!.Anio) throw new OperacionInvalida($"La planilla debe comenzar como máximo en {p.Ciclo.Anio}.");
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
        ExigirBorrador(p, u);
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
        ExigirBorrador(p, u);
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
        ExigirBorrador(p, u);
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
        await db.SaveChangesAsync();

        await EnviarMailAsync(p, u, urlComprobante);
        return p;
    }

    public static string Huella(Presentacion p)
    {
        var sb = new StringBuilder();
        foreach (var v in p.Valores.Where(v => v.Valor.HasValue).OrderBy(v => v.Anio).ThenBy(v => v.Campo, StringComparer.Ordinal))
            sb.Append(v.Anio).Append('|').Append(v.Campo).Append('|').Append(v.Valor!.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        foreach (var a in p.Anexos.OrderBy(a => a.Sha256, StringComparer.Ordinal)) sb.Append("A|").Append(a.Tipo).Append('|').Append(a.Sha256).Append('\n');
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

    static void ExigirBorrador(Presentacion p, UsuarioSesion u)
    {
        if (!u.PuedeEditar(p.Cuit)) throw new UnauthorizedAccessException();
        if (p.Estado != EstadoPresentacion.Borrador) throw new OperacionInvalida("La presentación ya fue enviada y no se puede modificar.");
    }

    /// <summary>Si Administración toca la planilla de una empresa, queda constancia con su nombre.</summary>
    static void RegistrarAsistencia(Presentacion p, UsuarioSesion u)
    {
        if (u.EsAsistencia(p.Cuit)) p.AsistidaPor = u.Nombre;
    }
}

/// <summary>Lectura mínima de configuración para no atar este servicio a IConfiguration.</summary>
public interface IConfigurationLike { IEnumerable<string> Lista(string clave); }
