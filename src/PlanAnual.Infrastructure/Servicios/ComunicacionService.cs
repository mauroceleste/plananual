using System.Net;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Mail;

namespace PlanAnual.Infrastructure.Servicios;

/// <summary>Estado general del plan de una empresa, según lo que pasó entre ella y la Subsecretaría.</summary>
public enum EstadoEmpresa { SinPresentar, EnCarga, Presentado, InformacionSolicitada, Observado, Rechazado, Aprobado }

/// <param name="Origen">«Sistema» si el movimiento sale de lo que el sistema ya sabe (envíos, rectificaciones); «Registro» si lo cargó una persona.</param>
/// <param name="Estado">Para notas con plazo: «Pendiente», «Vencida» o «Respondida». Null en el resto.</param>
public sealed record MovimientoVm(
    long? Id, DateTime Fecha, string Tipo, string Titulo, string? Detalle, string Usuario, string Origen, string? Estado, DateOnly? PlazoHasta,
    string? NumeroGde, string? NombreArea, bool Visible, string? AdjuntoNombre, long? AdjuntoId, bool MailEnviado, string? MailDestinatarios, string? MailError);

public sealed record LineaDeTiempo(
    string Cuit, string RazonSocial, EstadoEmpresa Estado, DateOnly LimiteGeneral, DateOnly LimiteDeLaEmpresa, DateOnly? ProximoVencimiento,
    int NotasPendientes, IReadOnlyList<MovimientoVm> Movimientos);

/// <summary>Lo que se carga al registrar un movimiento (lo arma el formulario de Administración).</summary>
public sealed record RegistroComunicacion(
    TipoComunicacion Tipo, string Asunto, string? Detalle, string? NumeroGde, DateOnly? PlazoHasta, long? RespondeA, int? IdBloque, string? NombreArea,
    bool VisibleParaEmpresa, bool EnviarMail, string? DestinatariosExtra,
    string? AdjuntoNombre = null, string? AdjuntoRuta = null, long? AdjuntoTamano = null, string? AdjuntoSha256 = null);

/// <summary>
/// El ida y vuelta con las empresas: notas y mails, respuestas, prórrogas y decisiones sobre el plan. Siempre se registra; el mail es opcional.
/// Registrar y decidir es de Administración; la Subsecretaría consulta; la empresa ve solo lo suyo y lo que se marcó visible.
/// </summary>
public sealed class ComunicacionService(PlanAnualDbContext db, IMailSender mail, IConfigurationLike cfg)
{
    static void SoloAdmin(UsuarioSesion u) { if (u.Rol != Rol.Administrador) throw new UnauthorizedAccessException(); }

    public static string Etiqueta(TipoComunicacion t) => t switch
    {
        TipoComunicacion.NotaEnviada => "Nota enviada a la empresa",
        TipoComunicacion.RespuestaEmpresa => "Respuesta de la empresa",
        TipoComunicacion.ProrrogaSolicitada => "Prórroga solicitada",
        TipoComunicacion.ProrrogaOtorgada => "Prórroga otorgada",
        TipoComunicacion.Observado => "Plan observado",
        TipoComunicacion.Rechazado => "Plan rechazado",
        TipoComunicacion.Aprobado => "Plan aprobado",
        _ => t.ToString(),
    };

    /// <summary>Límite de presentación de una empresa: el general de la convocatoria o la prórroga más lejana que se le otorgó.</summary>
    public async Task<DateOnly> LimiteDeLaEmpresaAsync(Ciclo ciclo, string cuit)
    {
        var otorgadas = await db.Comunicaciones.AsNoTracking()
            .Where(c => c.CicloId == ciclo.Id && c.Cuit == cuit && c.Tipo == TipoComunicacion.ProrrogaOtorgada && c.PlazoHasta != null)
            .Select(c => c.PlazoHasta!.Value).ToListAsync();
        return otorgadas.Count == 0 ? ciclo.LimiteVigente : otorgadas.Append(ciclo.LimiteVigente).Max();
    }

    // ============================ Lectura: la línea de tiempo ============================

    public async Task<LineaDeTiempo> LineaDeTiempoAsync(UsuarioSesion u, Ciclo ciclo, string cuit)
    {
        if (!u.PuedeVer(cuit)) throw new UnauthorizedAccessException();
        var esEmpresa = u.Rol == Rol.Empresa;
        var hoy = DateOnly.FromDateTime(DateTime.Today);

        var registradas = await db.Comunicaciones.AsNoTracking().Where(c => c.CicloId == ciclo.Id && c.Cuit == cuit).ToListAsync();
        var presentaciones = await db.Presentaciones.AsNoTracking().Where(p => p.CicloId == ciclo.Id && p.Cuit == cuit).ToListAsync();
        var solicitudes = await db.SolicitudesRectificacion.AsNoTracking().Where(s => s.CicloId == ciclo.Id && s.Cuit == cuit).ToListAsync();
        var razon = registradas.Select(c => c.RazonSocial).Concat(presentaciones.Select(p => p.RazonSocial)).FirstOrDefault(r => !string.IsNullOrEmpty(r)) ?? cuit;

        // Notas con plazo: pendientes hasta que alguien las contesta.
        var contestadas = registradas.Where(c => c.RespondeA != null).Select(c => c.RespondeA!.Value).ToHashSet();
        string? EstadoDe(Comunicacion c) =>
            c.Tipo is TipoComunicacion.NotaEnviada or TipoComunicacion.ProrrogaSolicitada && (c.PlazoHasta != null || c.Tipo == TipoComunicacion.ProrrogaSolicitada)
                ? contestadas.Contains(c.Id) ? "Respondida" : c.PlazoHasta is { } p && p < hoy ? "Vencida" : "Pendiente"
                : null;

        var mov = new List<MovimientoVm>();
        foreach (var c in registradas.Where(c => !esEmpresa || c.VisibleParaEmpresa))
            mov.Add(new(c.Id, c.Fecha, c.Tipo.ToString(), c.Asunto, c.Detalle, c.Usuario, "Registro", EstadoDe(c), c.PlazoHasta, c.NumeroGde, c.NombreArea, c.VisibleParaEmpresa,
                c.AdjuntoNombre, c.AdjuntoNombre is null ? null : c.Id, c.MailEnviado, c.MailDestinatarios, c.MailError));

        // Lo que el sistema ya sabe: los envíos del plan (todas las áreas enviadas juntas son un solo movimiento) y los pedidos de rectificación.
        foreach (var g in presentaciones.Where(p => p.EnviadaEn != null).GroupBy(p => (Minuto: new DateTime(p.EnviadaEn!.Value.Year, p.EnviadaEn.Value.Month, p.EnviadaEn.Value.Day, p.EnviadaEn.Value.Hour, p.EnviadaEn.Value.Minute, 0), p.Version)))
        {
            var quien = g.First().EnviadaPor ?? g.First().UsuarioNombre;
            var tipo = g.Key.Version > 1 ? "Rectificatoria" : "Presentacion";
            var titulo = g.Key.Version > 1 ? $"Rectificatoria presentada (v{g.Key.Version})" : "Plan presentado";
            var detalle = g.Count() == 1 ? $"{g.First().NombreArea} · comprobante {g.First().NumeroComprobante}" : $"{g.Count()} áreas · {g.Select(p => p.NombreArea).OrderBy(n => n).Take(3).Aggregate((a, b) => a + ", " + b)}…";
            mov.Add(new(null, g.Key.Minuto, tipo, titulo, detalle, quien, "Sistema", null, null, null, null, true, null, null, false, null, null));
        }
        foreach (var s in solicitudes)
        {
            mov.Add(new(null, s.SolicitadaEn, "RectificacionSolicitada", $"Pedido de rectificación · {s.NombreArea}", s.Motivo, s.SolicitadaPor, "Sistema", null, null, null, s.NombreArea, true, null, null, false, null, null));
            if (s.ResueltaEn is { } res)
                mov.Add(new(null, res, s.Estado == EstadoSolicitud.Rechazada ? "RectificacionRechazada" : "RectificacionHabilitada",
                    s.Estado == EstadoSolicitud.Rechazada ? $"Rectificación no autorizada · {s.NombreArea}" : $"Rectificación autorizada · {s.NombreArea}",
                    s.Estado == EstadoSolicitud.Rechazada ? null : $"Hasta el {s.HabilitadaHasta:dd/MM/yyyy HH:mm}", s.ResueltaPor ?? "Subsecretaría", "Sistema", null, null, null, s.NombreArea, true, null, null, false, null, null));
        }

        var ordenados = mov.OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id ?? 0).ToList();
        var pendientes = registradas.Where(c => EstadoDe(c) is "Pendiente" or "Vencida").ToList();
        var proximo = pendientes.Where(c => c.PlazoHasta != null).Select(c => c.PlazoHasta!.Value).OrderBy(d => d).Cast<DateOnly?>().FirstOrDefault();

        // Estado: lo último que se decidió, salvo que la empresa haya vuelto a presentar después.
        var ultimaPresentacion = presentaciones.Where(p => p.EnviadaEn != null).Select(p => p.EnviadaEn!.Value).DefaultIfEmpty().Max();
        var decision = registradas.Where(c => c.Tipo is TipoComunicacion.Aprobado or TipoComunicacion.Rechazado or TipoComunicacion.Observado).OrderByDescending(c => c.Fecha).FirstOrDefault();
        var estado =
            decision is not null && decision.Fecha >= ultimaPresentacion ? decision.Tipo switch { TipoComunicacion.Aprobado => EstadoEmpresa.Aprobado, TipoComunicacion.Rechazado => EstadoEmpresa.Rechazado, _ => EstadoEmpresa.Observado }
            : pendientes.Any(c => c.Tipo == TipoComunicacion.NotaEnviada) ? EstadoEmpresa.InformacionSolicitada
            : presentaciones.Any(p => p.EnviadaEn != null) ? EstadoEmpresa.Presentado
            : presentaciones.Count > 0 ? EstadoEmpresa.EnCarga : EstadoEmpresa.SinPresentar;

        return new LineaDeTiempo(cuit, razon, estado, ciclo.LimiteVigente, await LimiteDeLaEmpresaAsync(ciclo, cuit), proximo,
            pendientes.Count(c => c.Tipo == TipoComunicacion.NotaEnviada), ordenados);
    }

    /// <summary>Notas con plazo que la empresa todavía no contestó (para elegir a cuál responde un movimiento nuevo).</summary>
    public async Task<List<Comunicacion>> PendientesDeRespuestaAsync(UsuarioSesion u, int cicloId, string cuit)
    {
        if (!u.PuedeVer(cuit)) throw new UnauthorizedAccessException();
        var todas = await db.Comunicaciones.AsNoTracking().Where(c => c.CicloId == cicloId && c.Cuit == cuit).ToListAsync();
        var contestadas = todas.Where(c => c.RespondeA != null).Select(c => c.RespondeA!.Value).ToHashSet();
        return todas.Where(c => c.Tipo is TipoComunicacion.NotaEnviada or TipoComunicacion.ProrrogaSolicitada && !contestadas.Contains(c.Id)).OrderByDescending(c => c.Fecha).ToList();
    }

    /// <summary>Cantidad de notas pendientes de respuesta por empresa (para el listado de la Subsecretaría).</summary>
    public async Task<Dictionary<string, int>> PendientesPorEmpresaAsync(int cicloId)
    {
        var todas = await db.Comunicaciones.AsNoTracking().Where(c => c.CicloId == cicloId).Select(c => new { c.Id, c.Cuit, c.Tipo, c.RespondeA }).ToListAsync();
        var contestadas = todas.Where(c => c.RespondeA != null).Select(c => c.RespondeA!.Value).ToHashSet();
        return todas.Where(c => c.Tipo == TipoComunicacion.NotaEnviada && !contestadas.Contains(c.Id)).GroupBy(c => c.Cuit).ToDictionary(g => g.Key, g => g.Count());
    }

    public async Task<Comunicacion?> ObtenerAsync(UsuarioSesion u, long id)
    {
        var c = await db.Comunicaciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return null;
        if (!u.PuedeVer(c.Cuit)) throw new UnauthorizedAccessException();
        if (u.Rol == Rol.Empresa && !c.VisibleParaEmpresa) throw new UnauthorizedAccessException();
        return c;
    }

    // ============================ Escritura (solo Administración) ============================

    /// <summary>Registra un movimiento y, si se pidió, manda el mail. El registro se hace siempre; un mail que falla no lo impide.</summary>
    public async Task<Comunicacion> RegistrarAsync(UsuarioSesion u, Ciclo ciclo, string cuit, string razonSocial, RegistroComunicacion r, string urlEmpresa)
    {
        SoloAdmin(u);
        var asunto = r.Asunto?.Trim() ?? "";
        if (asunto.Length < 3) throw new OperacionInvalida("Escribí el asunto (al menos 3 caracteres).");
        if (asunto.Length > 200) throw new OperacionInvalida("El asunto es demasiado largo (máximo 200 caracteres).");
        var detalle = string.IsNullOrWhiteSpace(r.Detalle) ? null : r.Detalle.Trim();
        if (detalle is { Length: > 4000 }) throw new OperacionInvalida("El detalle es demasiado largo (máximo 4000 caracteres).");
        var gde = string.IsNullOrWhiteSpace(r.NumeroGde) ? null : r.NumeroGde.Trim();
        if (gde is { Length: > 80 }) throw new OperacionInvalida("El número de GDE es demasiado largo (máximo 80 caracteres).");
        if (!Enum.IsDefined(r.Tipo)) throw new OperacionInvalida("Tipo de movimiento desconocido.");
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        if (r.Tipo == TipoComunicacion.ProrrogaOtorgada && r.PlazoHasta is null) throw new OperacionInvalida("Indicá el nuevo plazo de la prórroga.");
        if (r.Tipo == TipoComunicacion.ProrrogaOtorgada && r.PlazoHasta <= ciclo.LimiteVigente && r.PlazoHasta <= await LimiteDeLaEmpresaAsync(ciclo, cuit))
            throw new OperacionInvalida("El nuevo plazo tiene que ser posterior al límite vigente de la empresa.");
        if (r.PlazoHasta is { } p && p < hoy && r.Tipo is TipoComunicacion.NotaEnviada or TipoComunicacion.Rechazado) throw new OperacionInvalida("El plazo no puede ser una fecha pasada.");

        if (r.RespondeA is { } resp)
        {
            var origen = await db.Comunicaciones.AsNoTracking().FirstOrDefaultAsync(c => c.Id == resp && c.Cuit == cuit && c.CicloId == ciclo.Id);
            if (origen is null || origen.Tipo is not (TipoComunicacion.NotaEnviada or TipoComunicacion.ProrrogaSolicitada)) throw new OperacionInvalida("La nota a la que responde no existe.");
        }

        var c = new Comunicacion
        {
            CicloId = ciclo.Id, Cuit = cuit, RazonSocial = razonSocial, IdBloque = r.IdBloque, NombreArea = r.IdBloque is null ? null : r.NombreArea,
            Fecha = DateTime.Now, Tipo = r.Tipo, Asunto = asunto, Detalle = detalle, NumeroGde = gde, PlazoHasta = r.PlazoHasta, RespondeA = r.RespondeA,
            VisibleParaEmpresa = r.VisibleParaEmpresa, UsuarioId = u.Id, Usuario = u.Nombre,
            AdjuntoNombre = r.AdjuntoNombre, AdjuntoRuta = r.AdjuntoRuta, AdjuntoTamano = r.AdjuntoTamano, AdjuntoSha256 = r.AdjuntoSha256,
        };
        // Una prórroga otorgada contesta el pedido pendiente más reciente si no se indicó otro.
        if (r.Tipo == TipoComunicacion.ProrrogaOtorgada && c.RespondeA is null)
            c.RespondeA = (await db.Comunicaciones.AsNoTracking().Where(x => x.CicloId == ciclo.Id && x.Cuit == cuit && x.Tipo == TipoComunicacion.ProrrogaSolicitada)
                .OrderByDescending(x => x.Fecha).Select(x => (long?)x.Id).FirstOrDefaultAsync());
        db.Comunicaciones.Add(c);
        await db.SaveChangesAsync();

        if (r.EnviarMail) await EnviarMailAsync(c, ciclo, r.DestinatariosExtra, urlEmpresa);
        return c;
    }

    /// <summary>
    /// Rechaza el plan de la empresa: queda registrado con el motivo y, si se pide, se la habilita a rectificar todas sus áreas enviadas hasta el plazo indicado.
    /// </summary>
    public async Task<Comunicacion> RechazarPlanAsync(UsuarioSesion u, Ciclo ciclo, string cuit, string razonSocial, string motivo, DateOnly plazoParaRepresentar, bool habilitarRectificacion,
        string? numeroGde, bool enviarMail, string? destinatariosExtra, string urlEmpresa, string? adjuntoNombre = null, string? adjuntoRuta = null, long? adjuntoTamano = null, string? adjuntoSha256 = null)
    {
        SoloAdmin(u);
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10) throw new OperacionInvalida("Contá el motivo del rechazo (al menos 10 caracteres).");
        var c = await RegistrarAsync(u, ciclo, cuit, razonSocial, new RegistroComunicacion(TipoComunicacion.Rechazado, "Plan de Inversiones " + ciclo.Anio + " rechazado", motivo.Trim(), numeroGde,
            plazoParaRepresentar, null, null, null, true, false, null, adjuntoNombre, adjuntoRuta, adjuntoTamano, adjuntoSha256), urlEmpresa);

        if (habilitarRectificacion)
        {
            var hasta = plazoParaRepresentar.ToDateTime(new TimeOnly(23, 59));
            var enviadas = (await db.Presentaciones.AsNoTracking().Where(p => p.CicloId == ciclo.Id && p.Cuit == cuit).ToListAsync())
                .GroupBy(p => p.IdBloque).Select(g => g.OrderByDescending(p => p.Version).First()).Where(p => p.Estado == EstadoPresentacion.Enviada).ToList();
            foreach (var p in enviadas)
            {
                var yaHabilitada = await db.SolicitudesRectificacion.AnyAsync(s => s.CicloId == ciclo.Id && s.Cuit == cuit && s.IdBloque == p.IdBloque && s.Estado == EstadoSolicitud.Habilitada && s.HabilitadaHasta > DateTime.Now);
                if (yaHabilitada) continue;
                db.SolicitudesRectificacion.Add(new SolicitudRectificacion
                {
                    CicloId = ciclo.Id, Cuit = cuit, RazonSocial = p.RazonSocial, IdBloque = p.IdBloque, NombreArea = p.NombreArea, PresentacionId = p.Id,
                    Motivo = "Plan rechazado: " + motivo.Trim(), SolicitadaPor = u.Nombre, SolicitadaEmail = u.Email, SolicitadaEn = DateTime.Now,
                    Estado = EstadoSolicitud.Habilitada, ResueltaPor = u.Nombre, ResueltaEn = DateTime.Now, HabilitadaHasta = hasta,
                });
            }
            await db.SaveChangesAsync();
        }
        if (enviarMail) await EnviarMailAsync(c, ciclo, destinatariosExtra, urlEmpresa);
        return c;
    }

    // ============================ Mail ============================

    /// <summary>Casillas de la empresa: quienes iniciaron o enviaron sus planillas, más las que se agreguen a mano.</summary>
    public async Task<List<string>> DestinatariosDeLaEmpresaAsync(int cicloId, string cuit)
    {
        var emails = await db.Presentaciones.AsNoTracking().Where(p => p.CicloId == cicloId && p.Cuit == cuit).Select(p => p.UsuarioEmail).Distinct().ToListAsync();
        return emails.Where(e => !string.IsNullOrWhiteSpace(e) && e.Contains('@')).Select(e => e.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(e => e).ToList();
    }

    static IEnumerable<string> Separar(string? lista) =>
        (lista ?? "").Split([';', ',', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(e => e.Contains('@') && e.Length <= 150);

    async Task EnviarMailAsync(Comunicacion c, Ciclo ciclo, string? extra, string urlEmpresa)
    {
        var para = (await DestinatariosDeLaEmpresaAsync(c.CicloId, c.Cuit)).Concat(Separar(extra)).Concat(cfg.Lista("Mail:Subsecretaria")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (para.Count == 0) { c.MailError = "No hay una casilla de la empresa para avisarle: agregala a mano."; await db.SaveChangesAsync(); return; }
        Func<string?, string> h = WebUtility.HtmlEncode!;
        var plazo = c.PlazoHasta is { } p ? $"<tr><td style=\"padding:4px 12px 4px 0;color:#5F7185\">{(c.Tipo == TipoComunicacion.ProrrogaOtorgada ? "Nuevo plazo" : c.Tipo == TipoComunicacion.Rechazado ? "Podés volver a presentar hasta" : "Plazo de respuesta")}</td><td><strong>{p:dd/MM/yyyy}</strong></td></tr>" : "";
        var gde = string.IsNullOrEmpty(c.NumeroGde) ? "" : $"<tr><td style=\"padding:4px 12px 4px 0;color:#5F7185\">Nota / expediente GDE</td><td><strong>{h(c.NumeroGde)}</strong></td></tr>";
        var detalle = string.IsNullOrEmpty(c.Detalle) ? "" : $"<p style=\"white-space:pre-wrap\">{h(c.Detalle)}</p>";
        var adjunto = c.AdjuntoNombre is null ? "" : $"<p>Documento adjunto en el sistema: <strong>{h(c.AdjuntoNombre)}</strong>.</p>";
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;color:#111C26;max-width:560px">
            <h2 style="color:#00374C;margin:0 0 8px">{h(Etiqueta(c.Tipo))}</h2>
            <p>Plan de Inversiones {ciclo.Anio} · <strong>{h(c.RazonSocial)}</strong>{(c.NombreArea is null ? "" : " · " + h(c.NombreArea))}</p>
            <p style="font-size:16px"><strong>{h(c.Asunto)}</strong></p>
            {detalle}
            <table>{gde}{plazo}</table>
            {adjunto}
            <p><a href="{h(urlEmpresa)}">Ver el detalle en el sistema</a></p>
            <p style="color:#5F7185;font-size:12px">Subsecretaría de Hidrocarburos · Provincia del Neuquén. Este mensaje es automático.</p></div>
            """;
        var r = await mail.EnviarAsync(para, $"[Plan de Inversiones {ciclo.Anio}] {c.Asunto}", html);
        c.MailEnviado = r.Error is null;
        c.MailError = r.Error is null ? null : (r.Error.Length > 480 ? r.Error[..480] : r.Error);
        c.MailDestinatarios = string.Join("; ", r.Destinatarios) + (r.Desviado && r.Destinatarios.Count > 0 ? " (modo de prueba: no llegó a la empresa)" : "");
        if (c.MailDestinatarios.Length > 990) c.MailDestinatarios = c.MailDestinatarios[..990] + "…";
        await db.SaveChangesAsync();
    }
}
