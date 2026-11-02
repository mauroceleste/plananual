using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Mail;
using PlanAnual.Infrastructure.Servicios;
using PlanAnual.Web.Infraestructura;
using PlanAnual.Web.Models;

namespace PlanAnual.Web.Controllers;

[Authorize(Policy = "Administrador")]
public class AdminController(PlanAnualDbContext db, ILegajosClient legajos, IConfiguration config, IMailSender mail) : Controller
{
    // ---------- Tipo de actividad por bloque (provisorio hasta que lo exponga Legajos de Áreas) ----------
    [HttpGet("Admin/Bloques")]
    public async Task<IActionResult> Bloques()
    {
        var tipos = await db.BloquesTipoActividad.AsNoTracking().ToDictionaryAsync(x => x.IdBloque, x => x.TipoPlan);
        try
        {
            var filas = (await legajos.TodosLosBloquesAsync()).OrderBy(b => b.RazonSocial).ThenBy(b => b.Nombre)
                .Select(b => new FilaTipo(b, tipos.TryGetValue(b.IdBloque, out var t) ? t : (TipoPlan?)null, TipoActividadTexto.Interpretar(b.TipoActividad))).ToList();
            return View(filas);
        }
        catch (HttpRequestException) { TempData["Error"] = "No se pudo consultar Legajos de Áreas."; return View(new List<FilaTipo>()); }
    }

    [HttpPost("Admin/Bloques")]
    public async Task<IActionResult> GuardarTipo(int idBloque, TipoPlan? tipo)
    {
        var b = (await legajos.TodosLosBloquesAsync()).FirstOrDefault(x => x.IdBloque == idBloque);
        if (b is null) return NotFound();
        var fila = await db.BloquesTipoActividad.FindAsync(idBloque);
        if (tipo is null) { if (fila is not null) db.BloquesTipoActividad.Remove(fila); }
        else if (fila is null) db.BloquesTipoActividad.Add(new BloqueTipoActividad { IdBloque = idBloque, NombreArea = b.NombreArea, NombreBloque = b.NombreBloque, TipoPlan = tipo.Value });
        else fila.TipoPlan = tipo.Value;
        await db.SaveChangesAsync();
        TempData["Mensaje"] = $"Se actualizó el tipo de actividad de {b.Nombre}.";
        return RedirectToAction(nameof(Bloques));
    }

    // ---------- Convocatoria ----------
    [HttpGet("Admin/Ciclo")]
    public async Task<IActionResult> Ciclo() => View(await db.Ciclos.OrderByDescending(c => c.Anio).FirstOrDefaultAsync() ?? new Ciclo { Anio = DateTime.Today.Year, Nombre = $"Plan de Inversiones {DateTime.Today.Year}", FechaLimite = DateOnly.FromDateTime(DateTime.Today.AddDays(30)) });

    [HttpPost("Admin/Ciclo")]
    public async Task<IActionResult> Ciclo(int anio, string nombre, DateOnly fechaLimite, DateOnly? fechaProrroga, bool abierto, string? notaGde)
    {
        var c = await db.Ciclos.FirstOrDefaultAsync(x => x.Anio == anio);
        if (c is null) { c = new Ciclo { Anio = anio }; db.Ciclos.Add(c); }
        c.Nombre = nombre.Trim(); c.FechaLimite = fechaLimite; c.FechaProrroga = fechaProrroga; c.Abierto = abierto; c.NotaGde = notaGde?.Trim();
        await db.SaveChangesAsync();
        TempData["Mensaje"] = "Convocatoria guardada.";
        return RedirectToAction(nameof(Ciclo));
    }

    // ---------- Correo: ver cómo está configurado y probarlo ----------
    [HttpGet("Admin/Correo")]
    public IActionResult Correo()
    {
        var m = config.GetSection("Mail");
        return View(new CorreoVm
        {
            Habilitado = m.GetValue<bool>("Habilitado"), ModoPrueba = m.GetValue<bool>("ModoPrueba"),
            Host = m["Host"], Puerto = m.GetValue("Puerto", 25), Ssl = m.GetValue<bool>("Ssl"), Desde = m["Desde"],
            TieneUsuario = !string.IsNullOrEmpty(m["Usuario"]), TieneClave = !string.IsNullOrEmpty(m["Clave"]),
            Subsecretaria = (m["Subsecretaria"] ?? "").Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            ListaPrueba = (m["ListaPrueba"] ?? "").Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            PublicOrigin = config["PublicOrigin"], MiMail = User.ToSesion().Email,
        });
    }

    [HttpPost("Admin/Correo")]
    public async Task<IActionResult> ProbarCorreo(string? para)
    {
        para = string.IsNullOrWhiteSpace(para) ? User.ToSesion().Email : para.Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(para, out _)) { TempData["Error"] = "La casilla no es válida."; return RedirectToAction(nameof(Correo)); }
        var r = await mail.EnviarAsync([para], "Prueba del sistema Plan Anual de Inversiones",
            $"<p>Este es un mensaje de prueba enviado el {DateTime.Now:dd/MM/yyyy HH:mm} por {System.Net.WebUtility.HtmlEncode(User.ToSesion().Nombre)}.</p><p>Si lo recibiste, el envío de avisos funciona.</p>");
        if (r.Ok) TempData["Mensaje"] = r.Desviado ? $"Se envió la prueba, pero el modo de prueba la desvió a: {string.Join(", ", r.Destinatarios)} (no a la casilla indicada)." : $"Se envió la prueba a {string.Join(", ", r.Destinatarios)}.";
        else TempData["Error"] = "No se pudo enviar: " + r.Error;
        return RedirectToAction(nameof(Correo));
    }
}
