using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Servicios;
using PlanAnual.Web.Infraestructura;
using PlanAnual.Web.Models;

namespace PlanAnual.Web.Controllers;

/// <summary>
/// El ida y vuelta con una empresa: línea de tiempo de notas, respuestas, prórrogas y decisiones. Administración registra y decide, la Subsecretaría consulta
/// y la empresa ve solo lo suyo (lo que se marcó visible). El registro se hace siempre; el mail es opcional.
/// </summary>
[Authorize]
public class ComunicacionesController(ComunicacionService svc, PresentacionService presentaciones, PlanAnualDbContext db, IConfiguration config, IWebHostEnvironment env) : Controller
{
    static readonly string[] ExtensionesPermitidas = [".pdf", ".xlsx", ".xls", ".docx", ".doc", ".png", ".jpg", ".jpeg", ".zip"];

    string CarpetaComunicaciones
    {
        get
        {
            var c = config["Anexos:Carpeta"] ?? "App_Data/anexos";
            return Path.Combine(Path.IsPathRooted(c) ? c : Path.Combine(env.ContentRootPath, c), "comunicaciones");
        }
    }

    /// <summary>Empresa que se está mirando: la de la persona (si es de una empresa) o la que elige Administración desde Presentaciones.</summary>
    string? CuitDe(string? cuit, UsuarioSesion u)
    {
        if (u.Rol == Rol.Empresa) return !string.IsNullOrEmpty(cuit) && u.Cuits.Contains(cuit) ? cuit : u.Cuits.FirstOrDefault();
        return !string.IsNullOrEmpty(cuit) && cuit.All(char.IsDigit) ? cuit : null;
    }

    [HttpGet("Comunicaciones")]
    public async Task<IActionResult> Index(string? cuit, int? anio)
    {
        var u = User.ToSesion();
        var c = CuitDe(cuit, u);
        if (c is null) return RedirectToAction("Index", u.VeTodo ? "Seguimiento" : "Plan");
        var ciclo = await presentaciones.CicloParaConsultaAsync(anio);
        if (ciclo is null) return View(new ComunicacionesVm { Cuit = c });
        try
        {
            var vm = new ComunicacionesVm
            {
                Cuit = c, Ciclo = ciclo, Convocatorias = await presentaciones.CiclosAsync(), Linea = await svc.LineaDeTiempoAsync(u, ciclo, c),
                EsAdministrador = u.Rol == Rol.Administrador, EsEmpresa = u.Rol == Rol.Empresa,
            };
            if (vm.EsAdministrador)
            {
                vm.Pendientes = await svc.PendientesDeRespuestaAsync(u, ciclo.Id, c);
                vm.Destinatarios = await svc.DestinatariosDeLaEmpresaAsync(ciclo.Id, c);
                vm.Areas = (await db.Presentaciones.AsNoTracking().Where(p => p.CicloId == ciclo.Id && p.Cuit == c).Select(p => new { p.IdBloque, p.NombreArea }).ToListAsync())
                    .GroupBy(p => p.IdBloque).Select(g => (g.Key, g.First().NombreArea)).OrderBy(a => a.NombreArea).ToList();
            }
            return View(vm);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("Comunicaciones/Registrar")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> Registrar(string cuit, int? anio, TipoComunicacion tipo, string? asunto, string? detalle, string? numeroGde, DateOnly? plazoHasta, long? respondeA,
        int? idBloque, bool visible = true, bool enviarMail = false, string? destinatarios = null, IFormFile? archivo = null)
    {
        var u = User.ToSesion();
        if (u.Rol != Rol.Administrador) return Forbid();
        var c = CuitDe(cuit, u);
        try
        {
            if (c is null) throw new OperacionInvalida("Elegí una empresa.");
            var ciclo = await presentaciones.CicloParaConsultaAsync(anio) ?? throw new OperacionInvalida("No hay una convocatoria.");
            var (razon, area) = await DatosDeLaEmpresaAsync(ciclo.Id, c, idBloque);
            var adj = await GuardarAdjuntoAsync(archivo, ciclo.Id);
            try
            {
                var reg = new RegistroComunicacion(tipo, asunto ?? "", detalle, numeroGde, plazoHasta, respondeA, idBloque, area, visible, enviarMail, destinatarios, adj?.Nombre, adj?.Ruta, adj?.Tamano, adj?.Sha);
                var creada = await svc.RegistrarAsync(u, ciclo, c, razon, reg, this.Absoluta(config, nameof(Index), "Comunicaciones", new { cuit = c, anio = ciclo.Anio }));
                TempData["Mensaje"] = Mensaje(creada, enviarMail);
            }
            catch { BorrarAdjunto(adj?.Ruta); throw; }
        }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { cuit = c ?? cuit, anio });
    }

    [HttpPost("Comunicaciones/Rechazar")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> Rechazar(string cuit, int? anio, string? motivo, DateOnly? plazoHasta, bool habilitar = true, string? numeroGde = null, bool enviarMail = false, string? destinatarios = null, IFormFile? archivo = null)
    {
        var u = User.ToSesion();
        if (u.Rol != Rol.Administrador) return Forbid();
        var c = CuitDe(cuit, u);
        try
        {
            if (c is null) throw new OperacionInvalida("Elegí una empresa.");
            if (plazoHasta is null) throw new OperacionInvalida("Indicá hasta cuándo puede volver a presentar.");
            var ciclo = await presentaciones.CicloParaConsultaAsync(anio) ?? throw new OperacionInvalida("No hay una convocatoria.");
            var (razon, _) = await DatosDeLaEmpresaAsync(ciclo.Id, c, null);
            var adj = await GuardarAdjuntoAsync(archivo, ciclo.Id);
            try
            {
                var creada = await svc.RechazarPlanAsync(u, ciclo, c, razon, motivo ?? "", plazoHasta.Value, habilitar, numeroGde, enviarMail, destinatarios,
                    this.Absoluta(config, nameof(Index), "Comunicaciones", new { cuit = c, anio = ciclo.Anio }), adj?.Nombre, adj?.Ruta, adj?.Tamano, adj?.Sha);
                TempData["Mensaje"] = "El plan quedó rechazado" + (habilitar ? $" y la empresa puede rectificar hasta el {plazoHasta:dd/MM/yyyy}" : "") + (enviarMail ? (creada.MailEnviado ? ". Le mandamos el mail." : ". No se pudo mandar el mail (el rechazo quedó registrado igual).") : ".");
            }
            catch { BorrarAdjunto(adj?.Ruta); throw; }
        }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { cuit = c ?? cuit, anio });
    }

    [HttpGet("Comunicaciones/Adjunto/{id:long}")]
    public async Task<IActionResult> Adjunto(long id)
    {
        Comunicacion? c;
        try { c = await svc.ObtenerAsync(User.ToSesion(), id); } catch (UnauthorizedAccessException) { return Forbid(); }
        if (c?.AdjuntoRuta is null) return NotFound();
        var completa = Path.GetFullPath(Path.Combine(CarpetaComunicaciones, c.AdjuntoRuta));
        if (!completa.StartsWith(Path.GetFullPath(CarpetaComunicaciones)) || !System.IO.File.Exists(completa)) return NotFound();
        return PhysicalFile(completa, "application/octet-stream", c.AdjuntoNombre);
    }

    // ---------- apoyo ----------

    static string Mensaje(Comunicacion c, bool pidioMail) =>
        $"Se registró: {ComunicacionService.Etiqueta(c.Tipo).ToLowerInvariant()}." + (pidioMail ? (c.MailEnviado ? " Le mandamos el mail a la empresa." : $" No se pudo mandar el mail ({c.MailError}); el movimiento quedó registrado igual.") : "");

    async Task<(string Razon, string? Area)> DatosDeLaEmpresaAsync(int cicloId, string cuit, int? idBloque)
    {
        var ps = await db.Presentaciones.AsNoTracking().Where(p => p.CicloId == cicloId && p.Cuit == cuit).Select(p => new { p.RazonSocial, p.IdBloque, p.NombreArea }).ToListAsync();
        var razon = ps.Select(p => p.RazonSocial).FirstOrDefault() ?? (await db.Comunicaciones.AsNoTracking().Where(x => x.Cuit == cuit).Select(x => x.RazonSocial).FirstOrDefaultAsync()) ?? cuit;
        return (razon, idBloque is null ? null : ps.FirstOrDefault(p => p.IdBloque == idBloque)?.NombreArea);
    }

    sealed record Adj(string Nombre, string Ruta, long Tamano, string Sha);

    async Task<Adj?> GuardarAdjuntoAsync(IFormFile? archivo, int cicloId)
    {
        if (archivo is null || archivo.Length == 0) return null;
        var maxMb = config.GetValue("Anexos:MaximoMB", 25);
        if (archivo.Length > maxMb * 1024L * 1024L) throw new OperacionInvalida($"El archivo supera el máximo de {maxMb} MB.");
        var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        if (!ExtensionesPermitidas.Contains(ext)) throw new OperacionInvalida($"Formato no permitido ({ext}). Se aceptan: {string.Join(", ", ExtensionesPermitidas)}.");
        var relativa = Path.Combine(cicloId.ToString(), $"{Guid.NewGuid():N}{ext}");
        var completa = Path.GetFullPath(Path.Combine(CarpetaComunicaciones, relativa));
        if (!completa.StartsWith(Path.GetFullPath(CarpetaComunicaciones))) throw new OperacionInvalida("Ruta inválida.");
        Directory.CreateDirectory(Path.GetDirectoryName(completa)!);
        await using (var fs = System.IO.File.Create(completa)) await archivo.CopyToAsync(fs);
        string sha; await using (var fs = System.IO.File.OpenRead(completa)) sha = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
        return new Adj(Path.GetFileName(archivo.FileName), relativa, archivo.Length, sha);
    }

    void BorrarAdjunto(string? relativa)
    {
        if (relativa is null) return;
        var completa = Path.GetFullPath(Path.Combine(CarpetaComunicaciones, relativa));
        if (completa.StartsWith(Path.GetFullPath(CarpetaComunicaciones)) && System.IO.File.Exists(completa)) System.IO.File.Delete(completa);
    }
}
