using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Servicios;
using PlanAnual.Web.Infraestructura;
using PlanAnual.Web.Models;

namespace PlanAnual.Web.Controllers;

[Authorize]
public class PlanController(PresentacionService svc, ILegajosClient legajos, PlanAnualDbContext db, ITipoActividadProvider tiposProvider, IConfiguration config,
    IWebHostEnvironment env) : Controller
{
    static readonly string[] ExtensionesPermitidas = [".pdf", ".xlsx", ".xls", ".xlsb", ".docx", ".doc", ".pptx", ".csv", ".png", ".jpg", ".jpeg", ".zip", ".kml", ".kmz"];

    // ---------- Mis áreas ----------
    public async Task<IActionResult> Index(string? cuit)
    {
        var u = User.ToSesion();
        IReadOnlyList<string> cuits = u.Cuits;
        if (u.Rol == Rol.Administrador)
        {
            // Administración entra a las áreas de una empresa para asistirla (se elige desde Presentaciones).
            if (string.IsNullOrEmpty(cuit) || !cuit.All(char.IsDigit)) return RedirectToAction("Index", "Seguimiento");
            cuits = [cuit];
        }
        else if (u.VeTodo) return RedirectToAction("Index", "Seguimiento");

        var vm = new MisAreasVm { Ciclo = await svc.CicloAbiertoAsync(), CuitAsistido = u.Rol == Rol.Administrador ? cuit : null };
        try
        {
            var bloques = (await Task.WhenAll(cuits.Select(c => legajos.BloquesPorOperadorAsync(c)))).SelectMany(x => x).OrderBy(b => b.Nombre).ToList();
            var tipos = await tiposProvider.TodosAsync();
            var ultimas = vm.Ciclo is null ? [] : await svc.UltimasPorBloqueAsync(vm.Ciclo.Id, cuits);
            vm.Filas = bloques.Select(b => new FilaArea(b, tipos.TryGetValue(b.IdBloque, out var t) ? t : (TipoPlan?)null, ultimas.GetValueOrDefault(b.IdBloque))).ToList();
            vm.RazonAsistida = bloques.FirstOrDefault()?.RazonSocial;
        }
        catch (HttpRequestException)
        {
            vm.ErrorServicio = "No se pudo consultar Legajos de Áreas en este momento. Probá de nuevo en unos minutos.";
        }
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Iniciar(int idBloque, string? cuit)
    {
        var u = User.ToSesion();
        IReadOnlyList<string> cuits = u.Rol == Rol.Administrador ? (string.IsNullOrEmpty(cuit) || !cuit.All(char.IsDigit) ? [] : [cuit]) : u.Cuits;
        var bloque = (await Task.WhenAll(cuits.Select(c => legajos.BloquesPorOperadorAsync(c)))).SelectMany(x => x).FirstOrDefault(b => b.IdBloque == idBloque);
        if (bloque is null) return Forbid();
        var tipo = await tiposProvider.DeAsync(idBloque);
        if (tipo is null) { TempData["Error"] = "La Subsecretaría todavía no definió el tipo de actividad de esta área."; return RedirectToAction(nameof(Index), new { cuit }); }
        try { var p = await svc.IniciarAsync(u, bloque, tipo.Value); return RedirectToAction(nameof(Editar), new { id = p.Id }); }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; return RedirectToAction(nameof(Index), new { cuit }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost]
    public async Task<IActionResult> Rectificar(long id, string? cuit)
    {
        try { var p = await svc.RectificarAsync(id, User.ToSesion()); return RedirectToAction(nameof(Editar), new { id = p.Id }); }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; return RedirectToAction(nameof(Index), new { cuit }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    // ---------- Formulario por pasos ----------
    [HttpGet("Plan/Editar/{id:long}")]
    public async Task<IActionResult> Editar(long id, int paso = 0)
    {
        var u = User.ToSesion();
        Presentacion p;
        try { p = await svc.ObtenerAsync(id, u); } catch (OperacionInvalida) { return NotFound(); } catch (UnauthorizedAccessException) { return Forbid(); }

        var vm = new EditarVm
        {
            P = p, Pasos = PasosDe(p.TipoPlan), SoloLectura = p.Estado != EstadoPresentacion.Borrador || !u.PuedeEditar(p.Cuit),
            Calc = Calculadora.CalcularPlan(p.TipoPlan, p.Valores),
            Ingresados = p.Valores.Where(v => v.Valor.HasValue).ToDictionary(v => (v.Anio, v.Campo), v => v.Valor!.Value),
        };
        vm.PasoActual = Math.Clamp(paso, 0, vm.Pasos.Count - 1);
        if (vm.Actual.Tipo == TipoPaso.Revision) vm.Observaciones = await svc.RevisarAsync(id, u);
        vm.Anios = Enumerable.Range(p.AnioDesde, p.AnioHasta - p.AnioDesde + 1).ToList();
        return View(vm);
    }

    public record GuardarReq(List<CeldaDto> Celdas);

    [HttpPost("Plan/Guardar/{id:long}")]
    public async Task<IActionResult> Guardar(long id, [FromBody] GuardarReq req)
    {
        try
        {
            var filas = await svc.GuardarCeldasAsync(id, User.ToSesion(), req.Celdas);
            return Json(new { ok = true, filas, hora = DateTime.Now.ToString("HH:mm:ss") });
        }
        catch (OperacionInvalida ex) { return BadRequest(new { ok = false, error = ex.Message }); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
    }

    [HttpPost("Plan/Rango/{id:long}")]
    public async Task<IActionResult> Rango(long id, int desde, int hasta, int paso)
    {
        try { await svc.CambiarRangoAsync(id, User.ToSesion(), desde, hasta); }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Editar), new { id, paso });
    }

    // ---------- Anexos ----------
    [HttpPost("Plan/AnexoSubir/{id:long}")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> AnexoSubir(long id, string tipo, IFormFile archivo, int paso)
    {
        var u = User.ToSesion();
        var maxMb = config.GetValue("Anexos:MaximoMB", 25);
        try
        {
            if (archivo is null || archivo.Length == 0) throw new OperacionInvalida("Elegí un archivo.");
            if (archivo.Length > maxMb * 1024L * 1024L) throw new OperacionInvalida($"El archivo supera el máximo de {maxMb} MB.");
            var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
            if (!ExtensionesPermitidas.Contains(ext)) throw new OperacionInvalida($"Formato no permitido ({ext}). Se aceptan: {string.Join(", ", ExtensionesPermitidas)}.");
            await svc.ObtenerAsync(id, u, conDetalle: false);   // controla acceso antes de escribir en disco

            var relativa = Path.Combine(id.ToString(), $"{Guid.NewGuid():N}{ext}");
            var completa = Path.GetFullPath(Path.Combine(CarpetaAnexos, relativa));
            if (!completa.StartsWith(Path.GetFullPath(CarpetaAnexos))) throw new OperacionInvalida("Ruta inválida.");
            Directory.CreateDirectory(Path.GetDirectoryName(completa)!);
            string sha;
            await using (var fs = System.IO.File.Create(completa)) { await archivo.CopyToAsync(fs); }
            await using (var fs = System.IO.File.OpenRead(completa)) sha = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();

            var nombre = Path.GetFileName(archivo.FileName);
            try { await svc.AgregarAnexoAsync(id, u, tipo, nombre, relativa, archivo.Length, sha); }
            catch { System.IO.File.Delete(completa); throw; }
            TempData["Mensaje"] = $"Se adjuntó {nombre}.";
        }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return RedirectToAction(nameof(Editar), new { id, paso });
    }

    [HttpPost("Plan/AnexoQuitar/{id:long}")]
    public async Task<IActionResult> AnexoQuitar(long id, long anexoId, int paso)
    {
        try { await svc.QuitarAnexoAsync(id, anexoId, User.ToSesion()); }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return RedirectToAction(nameof(Editar), new { id, paso });
    }

    [HttpGet("Plan/Anexo/{id:long}/{anexoId:long}")]
    public async Task<IActionResult> AnexoDescargar(long id, long anexoId)
    {
        Presentacion p;
        try { p = await svc.ObtenerAsync(id, User.ToSesion()); } catch (OperacionInvalida) { return NotFound(); } catch (UnauthorizedAccessException) { return Forbid(); }
        var a = p.Anexos.FirstOrDefault(x => x.Id == anexoId);
        if (a is null) return NotFound();
        var completa = Path.GetFullPath(Path.Combine(CarpetaAnexos, a.RutaAlmacenada));
        if (!completa.StartsWith(Path.GetFullPath(CarpetaAnexos)) || !System.IO.File.Exists(completa)) return NotFound();
        return PhysicalFile(completa, "application/octet-stream", a.NombreArchivo);
    }

    // ---------- Excel ----------
    [HttpGet("Plan/Exportar/{id:long}")]
    public async Task<IActionResult> Exportar(long id)
    {
        try
        {
            var p = await svc.ObtenerAsync(id, User.ToSesion());
            var nombre = string.Concat($"Plan_{p.Ciclo!.Anio}_{p.NombreArea}_{p.NumeroComprobante ?? "borrador"}".Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
            return File(ExportadorExcel.Planilla(p), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombre + ".xlsx");
        }
        catch (OperacionInvalida) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    // ---------- Envío ----------
    [HttpPost]
    public async Task<IActionResult> Enviar(long id)
    {
        try
        {
            // Detrás del proxy el host y el esquema internos no sirven para el mail: se usa el origen público configurado.
            var origen = config["PublicOrigin"];
            var url = string.IsNullOrWhiteSpace(origen)
                ? Url.Action(nameof(Comprobante), "Plan", new { id }, Request.Scheme)!
                : origen.TrimEnd('/') + Url.Action(nameof(Comprobante), "Plan", new { id });
            await svc.EnviarAsync(id, User.ToSesion(), url);
            TempData["Mensaje"] = "La presentación fue enviada. Te mandamos el comprobante por mail.";
            return RedirectToAction(nameof(Comprobante), new { id });
        }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; return RedirectToAction(nameof(Editar), new { id, paso = int.MaxValue }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpGet("Plan/Comprobante/{id:long}")]
    public async Task<IActionResult> Comprobante(long id)
    {
        try
        {
            var p = await svc.ObtenerAsync(id, User.ToSesion());
            if (p.Estado == EstadoPresentacion.Borrador) return RedirectToAction(nameof(Editar), new { id });
            var mails = await db.EnviosMail.AsNoTracking().Where(m => m.PresentacionId == id).OrderByDescending(m => m.Fecha).FirstOrDefaultAsync();
            return View(new ComprobanteVm { P = p, Calc = Calculadora.CalcularPlan(p.TipoPlan, p.Valores), Mail = mails });
        }
        catch (OperacionInvalida) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    string CarpetaAnexos
    {
        get
        {
            var c = config["Anexos:Carpeta"] ?? "App_Data/anexos";
            return Path.IsPathRooted(c) ? c : Path.Combine(env.ContentRootPath, c);
        }
    }

    // ---------- Definición de los pasos ----------
    static readonly (string Titulo, string[] Grupos)[] Grillas =
    [
        ("Pozos", [CatalogoCampos.GPozos]),
        ("Capacidad", [CatalogoCampos.GCapacidad]),
        ("Inversión", [CatalogoCampos.GInversion, CatalogoCampos.GAbandono]),
        ("Gastos operativos", [CatalogoCampos.GGastos]),
        ("Producción y precios", [CatalogoCampos.GProduccion, CatalogoCampos.GPrecios]),
        ("Ingresos y flujo", [CatalogoCampos.GFlujo]),
    ];

    public static List<PasoVm> PasosDe(TipoPlan plan)
    {
        var pasos = Grillas.Select((g, i) => new PasoVm(i, g.Titulo, TipoPaso.Grilla,
            CatalogoCampos.Para(plan).Where(c => g.Grupos.Contains(c.Grupo)).ToList())).ToList();
        pasos.Add(new PasoVm(pasos.Count, "Anexos", TipoPaso.Anexos, []));
        pasos.Add(new PasoVm(pasos.Count, "Revisión y envío", TipoPaso.Revision, []));
        return pasos;
    }
}
