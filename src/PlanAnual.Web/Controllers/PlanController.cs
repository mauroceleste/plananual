using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Gde;
using PlanAnual.Infrastructure.Servicios;
using PlanAnual.Web.Infraestructura;
using PlanAnual.Web.Models;

namespace PlanAnual.Web.Controllers;

[Authorize]
public class PlanController(PresentacionService svc, ILegajosClient legajos, PlanAnualDbContext db, ITipoActividadProvider tiposProvider, IConfiguration config,
    IWebHostEnvironment env, IGdeClient gde, ComunicacionService comunicaciones) : Controller
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
            var ultimas = vm.Ciclo is null ? [] : await svc.UltimasPorBloqueAsync(vm.Ciclo.Id, cuits, conAnexos: u.Rol == Rol.Administrador);
            var solicitudes = vm.Ciclo is null ? [] : await svc.UltimasSolicitudesAsync(vm.Ciclo.Id, cuits);
            vm.Filas = bloques.Select(b => new FilaArea(b, null, ultimas.GetValueOrDefault(b.IdBloque), solicitudes.GetValueOrDefault(b.IdBloque))).ToList();
            if (vm.Ciclo is not null && cuits.Count > 0)
            {
                // Plazo propio de la empresa (prórrogas) y lo último que le comunicó la Subsecretaría.
                var novedades = new List<MovimientoVm>(); var limites = new List<DateOnly>();
                foreach (var cu in cuits)
                {
                    var linea = await comunicaciones.LineaDeTiempoAsync(u, vm.Ciclo, cu);
                    limites.Add(linea.LimiteDeLaEmpresa); novedades.AddRange(linea.Movimientos.Where(m => m.Origen == "Registro")); vm.NotasPendientes += linea.NotasPendientes;
                }
                vm.LimiteEmpresa = limites.Max();
                vm.Novedades = novedades.OrderByDescending(m => m.Fecha).Take(3).ToList();
            }
            vm.Actividades = await svc.ActividadesAsync(vm.Filas.Where(f => f.Ultima is not null).Select(f => f.Ultima!.Id));
            vm.Pendientes = await svc.PendientesAsync(vm.Filas.Where(f => f.Ultima?.Estado == EstadoPresentacion.Borrador).Select(f => f.Ultima!.Id));
            vm.RazonAsistida = bloques.FirstOrDefault()?.RazonSocial;
            if (vm.Ciclo is not null && u.Rol == Rol.Administrador && cuit is not null) vm.Observaciones = await svc.ConteoObservacionesAsync(u, vm.Ciclo.Id, cuit);
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
        try { var p = await svc.IniciarAsync(u, bloque, TipoPlan.Convencional); return RedirectToAction(nameof(Editar), new { id = p.Id }); }
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

    // ---------- Modales de Administración: adjuntos y observaciones internas de un área ----------

    /// <summary>Adjuntos de un área, para el modal del listado (solo Administración).</summary>
    [HttpGet("Plan/AdjuntosArea/{id:long}")]
    public async Task<IActionResult> AdjuntosArea(long id)
    {
        var u = User.ToSesion();
        if (u.Rol != Rol.Administrador) return Forbid();
        try { return PartialView("_AdjuntosArea", await svc.ObtenerAsync(id, u)); }
        catch (OperacionInvalida) { return NotFound(); }
    }

    /// <summary>Historial del expediente de GDE (pases y documentos), para el modal del listado. Solo Administración.</summary>
    [HttpGet("Plan/HistorialExpediente")]
    public async Task<IActionResult> HistorialExpediente(string? numero, CancellationToken ct)
    {
        if (User.ToSesion().Rol != Rol.Administrador) return Forbid();
        return PartialView("_HistorialExpediente", await gde.HistorialAsync(numero ?? "", ct));
    }

    /// <summary>
    /// Baja el PDF de una nota o documento de GDE y lo abre en el navegador. Lo ve Administración y la Subsecretaría, no las empresas: la API de GDE
    /// no distingue usuarios, así que quién puede bajar se decide acá.
    /// </summary>
    [HttpGet("Plan/DocumentoGde")]
    public async Task<IActionResult> DocumentoGde(string? numero, CancellationToken ct)
    {
        if (User.ToSesion().Rol == Rol.Empresa) return Forbid();
        var r = await gde.DocumentoPdfAsync(numero ?? "", ct);
        if (r.Ok) return File(r.Bytes!, "application/pdf");
        var msg = System.Net.WebUtility.HtmlEncode(r.Mensaje);
        return Content($"<!doctype html><meta charset=\"utf-8\"><title>GDE</title><body style=\"font-family:system-ui;margin:2rem\"><h3>No se pudo bajar el documento</h3><p>{msg}</p><p><a href=\"javascript:window.close()\">Cerrar</a></p>", "text/html; charset=utf-8", System.Text.Encoding.UTF8);
    }

    /// <summary>Historial de modificaciones de un área (quién cambió qué y cuándo), para el modal del listado. Solo Administración.</summary>
    [HttpGet("Plan/HistorialArea")]
    public async Task<IActionResult> HistorialArea(string cuit, int idBloque)
    {
        var u = User.ToSesion();
        if (u.Rol != Rol.Administrador) return Forbid();
        var ciclo = await svc.CicloAbiertoAsync();
        if (ciclo is null || string.IsNullOrEmpty(cuit) || !cuit.All(char.IsDigit)) return BadRequest(new { error = "Datos no válidos." });
        return PartialView("_HistorialArea", await svc.HistorialAsync(u, ciclo.Id, cuit, idBloque));
    }

    [HttpGet("Plan/ObservacionesArea")]
    public async Task<IActionResult> ObservacionesArea(string cuit, int idBloque) => await ListaObservaciones(cuit, idBloque);

    [HttpPost("Plan/AgregarObservacion")]
    public async Task<IActionResult> AgregarObservacion(string cuit, int idBloque, string? texto)
    {
        var u = User.ToSesion();
        if (u.Rol != Rol.Administrador) return Forbid();
        var ciclo = await svc.CicloAbiertoAsync();
        if (ciclo is null || string.IsNullOrEmpty(cuit) || !cuit.All(char.IsDigit)) return BadRequest(new { error = "Datos no válidos." });
        try { await svc.AgregarObservacionAsync(u, ciclo.Id, cuit, idBloque, texto); }
        catch (OperacionInvalida ex) { return BadRequest(new { error = ex.Message }); }
        return await ListaObservaciones(cuit, idBloque);
    }

    [HttpPost("Plan/EliminarObservacion")]
    public async Task<IActionResult> EliminarObservacion(long id)
    {
        var u = User.ToSesion();
        if (u.Rol != Rol.Administrador) return Forbid();
        try { var o = await svc.EliminarObservacionAsync(u, id); return await ListaObservaciones(o.Cuit, o.IdBloque); }
        catch (OperacionInvalida ex) { return BadRequest(new { error = ex.Message }); }
    }

    async Task<IActionResult> ListaObservaciones(string cuit, int idBloque)
    {
        var u = User.ToSesion();
        if (u.Rol != Rol.Administrador) return Forbid();
        var ciclo = await svc.CicloAbiertoAsync();
        if (ciclo is null || string.IsNullOrEmpty(cuit) || !cuit.All(char.IsDigit)) return BadRequest(new { error = "Datos no válidos." });
        ViewData["Cuit"] = cuit; ViewData["IdBloque"] = idBloque;
        return PartialView("_ObservacionesArea", await svc.ObservacionesAsync(u, ciclo.Id, cuit, idBloque));
    }

    /// <summary>Número de expediente de GDE de un área (solo Administración).</summary>
    [HttpPost]
    public async Task<IActionResult> GuardarExpediente(long id, string? expediente, string? cuit)
    {
        try { await svc.GuardarExpedienteAsync(id, User.ToSesion(), expediente); TempData["Mensaje"] = "Expediente guardado."; }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return RedirectToAction(nameof(Index), new { cuit });
    }

    [HttpPost]
    public async Task<IActionResult> SolicitarRectificacion(long id, string? motivo, string? cuit)
    {
        try
        {
            await svc.SolicitarRectificacionAsync(id, User.ToSesion(), motivo, this.Absoluta(config, nameof(AdminController.Rectificaciones), "Admin"));
            TempData["Mensaje"] = "Enviamos tu pedido a la Subsecretaría. Cuando lo autorice te avisamos por mail y vas a tener 7 días para presentar la rectificatoria.";
        }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return RedirectToAction(nameof(Index), new { cuit });
    }

    /// <summary>Envía de una vez todas las áreas en borrador de la empresa (todo o nada).</summary>
    [HttpPost]
    public async Task<IActionResult> EnviarFinal(string? cuit)
    {
        var u = User.ToSesion();
        IReadOnlyList<string> cuits = u.Rol == Rol.Administrador ? (string.IsNullOrEmpty(cuit) || !cuit.All(char.IsDigit) ? [] : [cuit]) : u.Cuits;
        try
        {
            var bloques = (await Task.WhenAll(cuits.Select(c => legajos.BloquesPorOperadorAsync(c)))).SelectMany(x => x).ToList();
            var r = await svc.EnviarFinalAsync(u, bloques, id => this.Absoluta(config, nameof(Comprobante), "Plan", new { id }));
            if (r.Problemas.Count > 0) TempData["Problemas"] = string.Join("\n", r.Problemas);
            else TempData["Mensaje"] = $"La presentación final fue enviada: {r.Enviadas} área(s). Te mandamos un comprobante por cada una.";
        }
        catch (HttpRequestException) { TempData["Error"] = "No se pudo consultar Legajos de Áreas en este momento. Probá de nuevo en unos minutos."; }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return RedirectToAction(nameof(Index), new { cuit });
    }

    // ---------- Formulario por pasos ----------
    [HttpGet("Plan/Editar/{id:long}")]
    public async Task<IActionResult> Editar(long id, int paso = 0, int? plan = null)
    {
        var u = User.ToSesion();
        Presentacion p;
        try { p = await svc.ObtenerAsync(id, u); } catch (OperacionInvalida) { return NotFound(); } catch (UnauthorizedAccessException) { return Forbid(); }

        // Un área puede tener plan Convencional y No Convencional: se trabaja de a uno (?plan=1 o 2); por defecto, el principal.
        var tipoActual = plan is { } pl && Enum.IsDefined(typeof(TipoPlan), pl) ? (TipoPlan)pl : p.TipoPlan;
        var vm = new EditarVm
        {
            P = p, TipoActual = tipoActual, Cargados = p.TiposCargados(), Resumen = ResumenDePlanes(p), Pasos = PasosDe(tipoActual), SoloLectura = p.Estado != EstadoPresentacion.Borrador || !u.PuedeEditar(p.Cuit),
            Calc = Calculadora.CalcularPeriodos(tipoActual, p.Valores.Where(v => p.ActividadDe(v) == tipoActual)),
            Ingresados = p.Valores.Where(v => v.Valor.HasValue && p.ActividadDe(v) == tipoActual).ToDictionary(v => (v.Anio, v.Mes, v.Campo), v => v.Valor!.Value),
        };
        vm.PasoActual = Math.Clamp(paso, 0, vm.Pasos.Count - 1);
        if (vm.Actual.Tipo == TipoPaso.Revision) vm.Observaciones = await svc.RevisarAsync(id, u);
        // El primer año (el de la convocatoria) va mes por mes con su subtotal anual; los siguientes, un renglón por año.
        for (var anio = p.AnioDesde; anio <= p.AnioHasta; anio++)
        {
            if (anio == p.Ciclo!.Anio)
            {
                for (var m = 1; m <= 12; m++) vm.Filas.Add(new FilaPeriodo(anio, m));
                vm.Filas.Add(new FilaPeriodo(anio, 0, EsSubtotal: true));
            }
            else vm.Filas.Add(new FilaPeriodo(anio, 0));
        }
        return View(vm);
    }

    /// <summary>Estado de cada plan de actividad para las tarjetas del formulario.</summary>
    static Dictionary<TipoPlan, ResumenPlan> ResumenDePlanes(Presentacion p) =>
        Presentacion.Actividades.ToDictionary(t => t, t =>
        {
            var valores = p.Valores.Where(v => v.Valor.HasValue && p.ActividadDe(v) == t).ToList();
            if (valores.Count == 0) return new ResumenPlan("Sin datos", null);
            var inversion = Calculadora.CalcularPlan(t, valores).GetValueOrDefault(p.Ciclo!.Anio)?.GetValueOrDefault("inv_total");
            return new ResumenPlan(valores.All(v => v.Valor == 0) ? "Sin actividad" : "Cargado", inversion);
        });

    public record GuardarReq(List<CeldaDto> Celdas);

    [HttpPost("Plan/Guardar/{id:long}")]
    public async Task<IActionResult> Guardar(long id, [FromBody] GuardarReq req, int? plan = null)
    {
        try
        {
            var filas = await svc.GuardarCeldasAsync(id, User.ToSesion(), plan is { } pl ? req.Celdas.Select(c => c with { Tipo = (TipoPlan)pl }).ToList() : req.Celdas);
            return Json(new { ok = true, filas, hora = DateTime.Now.ToString("HH:mm:ss") });
        }
        catch (OperacionInvalida ex) { return BadRequest(new { ok = false, error = ex.Message }); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
    }

    /// <summary>Guarda los cuatro textos del bloque 4. Se llama solo (al salir de cada cuadro) o con el botón Guardar.</summary>
    [HttpPost("Plan/GuardarTexto/{id:long}")]
    public async Task<IActionResult> GuardarTexto(long id, string? conflictos, string? contratosFirmes, string? plazos, string? costos, int paso, bool ajax = false)
    {
        try
        {
            await svc.GuardarRestriccionesAsync(id, User.ToSesion(), conflictos, contratosFirmes, plazos, costos);
            if (ajax) return Json(new { ok = true, hora = DateTime.Now.ToString("HH:mm:ss") });
            TempData["Mensaje"] = "Guardamos las restricciones a la actividad.";
        }
        catch (OperacionInvalida ex)
        {
            if (ajax) return BadRequest(new { ok = false, error = ex.Message });
            TempData["Error"] = ex.Message;
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return RedirectToAction(nameof(Editar), new { id, paso });
    }

    /// <summary>Declara que el área no tiene actividad de ese tipo: carga 0 en todo ese plan.</summary>
    [HttpPost("Plan/SinActividad/{id:long}")]
    public async Task<IActionResult> SinActividad(long id, int plan, int paso)
    {
        try
        {
            if (!Enum.IsDefined(typeof(TipoPlan), plan)) throw new OperacionInvalida("Tipo de actividad desconocido.");
            await svc.MarcarSinActividadAsync(id, User.ToSesion(), (TipoPlan)plan);
            TempData["Mensaje"] = $"Marcamos «{Validador.NombreActividad((TipoPlan)plan)}» como sin actividad: se cargó 0 en todo ese plan.";
        }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return RedirectToAction(nameof(Editar), new { id, paso, plan });
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

    // ---------- Planilla Excel de carga (todas las áreas) ----------
    const string TipoXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Las áreas de la empresa (o de la asistida, si entra Administración); null si Administración no indicó empresa.</summary>
    async Task<List<BloqueInfo>?> AreasDeLaEmpresaAsync(string? cuit)
    {
        var u = User.ToSesion();
        IReadOnlyList<string> cuits = u.Rol == Rol.Administrador ? (string.IsNullOrEmpty(cuit) || !cuit.All(char.IsDigit) ? [] : [cuit]) : u.Cuits;
        if (cuits.Count == 0) return null;
        return (await Task.WhenAll(cuits.Select(c => legajos.BloquesPorOperadorAsync(c)))).SelectMany(x => x).OrderBy(b => b.Nombre).ToList();
    }

    /// <summary>Planilla modelo de los bloques 2 a 4 (instalaciones, paradas de planta, conflictos y equipos), con los datos del área.</summary>
    [HttpGet("Plan/PlanillaModelo/{id:long}")]
    public async Task<IActionResult> PlanillaModelo(long id, string tipo)
    {
        if (!PlanillasModelo.Existe(tipo)) return NotFound();
        Presentacion p;
        try { p = await svc.ObtenerAsync(id, User.ToSesion(), conDetalle: false); } catch (OperacionInvalida) { return NotFound(); } catch (UnauthorizedAccessException) { return Forbid(); }
        var anio = p.Ciclo?.Anio ?? DateTime.Today.Year;
        var bytes = PlanillasModelo.Generar(tipo, anio, p.RazonSocial, p.NombreArea);
        var nombre = string.Concat($"Plan_{anio}_{p.NombreArea}_{PlanillasModelo.Nombre(tipo)}".Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return File(bytes, TipoXlsx, nombre + ".xlsx");
    }

    [HttpGet("Plan/PlanillaExcel")]
    public async Task<IActionResult> PlanillaExcel(string? cuit, bool cargado = false)
    {
        try
        {
            var bloques = await AreasDeLaEmpresaAsync(cuit);
            if (bloques is null) return RedirectToAction(nameof(Index), new { cuit });
            var bytes = await svc.PlantillaAsync(User.ToSesion(), bloques, cargado);
            var ciclo = await svc.CicloAbiertoAsync();
            return File(bytes, TipoXlsx, $"Plan_{ciclo?.Anio}_{(cargado ? "planilla_con_lo_cargado" : "planilla_de_carga")}.xlsx");
        }
        catch (HttpRequestException) { TempData["Error"] = "No se pudo consultar Legajos de Áreas en este momento. Probá de nuevo en unos minutos."; }
        catch (OperacionInvalida ex) { TempData["Error"] = ex.Message; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return RedirectToAction(nameof(Index), new { cuit });
    }

    [HttpPost("Plan/Importar")]
    [RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> Importar(IFormFile? archivo, string? cuit)
    {
        var vm = new ImportacionVm { CuitAsistido = cuit, NombreArchivo = archivo?.FileName };
        try
        {
            if (archivo is null || archivo.Length == 0) throw new OperacionInvalida("Elegí el archivo Excel que completaste.");
            if (archivo.Length > 10 * 1024 * 1024) throw new OperacionInvalida("El archivo supera los 10 MB.");
            if (!string.Equals(Path.GetExtension(archivo.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new OperacionInvalida("El archivo tiene que ser un Excel .xlsx (el que se descarga desde el sistema).");
            var bloques = await AreasDeLaEmpresaAsync(cuit) ?? throw new OperacionInvalida("No hay áreas para importar.");
            await using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms); ms.Position = 0;
            var r = await svc.ImportarAsync(User.ToSesion(), bloques, ms);
            vm.Errores = r.Errores.ToList(); vm.Truncado = r.Truncado; vm.Areas = r.Areas.ToList();
        }
        catch (HttpRequestException) { vm.Errores.Add(new ErrorImportacion("(sistema)", null, null, "No se pudo consultar Legajos de Áreas en este momento. Probá de nuevo en unos minutos.")); }
        catch (OperacionInvalida ex) { vm.Errores.Add(new ErrorImportacion("(archivo)", null, null, ex.Message)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return View("Importar", vm);
    }

    // El envío se hace solo con la presentación final (EnviarFinal): no hay envío por área suelta.

    [HttpGet("Plan/Comprobante/{id:long}")]
    public async Task<IActionResult> Comprobante(long id)
    {
        try
        {
            var p = await svc.ObtenerAsync(id, User.ToSesion());
            if (p.Estado == EstadoPresentacion.Borrador) return RedirectToAction(nameof(Editar), new { id });
            var mails = await db.EnviosMail.AsNoTracking().Where(m => m.PresentacionId == id).OrderByDescending(m => m.Fecha).FirstOrDefaultAsync();
            return View(new ComprobanteVm { P = p, Planes = p.TiposCargados().DefaultIfEmpty(p.TipoPlan).Select(t => (t, Calculadora.CalcularPlan(t, p.Valores.Where(v => p.ActividadDe(v) == t)))).ToList(), Mail = mails });
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
        pasos.Add(new PasoVm(pasos.Count, "Restricciones", TipoPaso.Texto, []));
        pasos.Add(new PasoVm(pasos.Count, "Revisión y envío", TipoPaso.Revision, []));
        return pasos;
    }
}
