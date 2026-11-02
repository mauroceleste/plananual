using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanAnual.Infrastructure.Acceso;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Servicios;
using PlanAnual.Web.Infraestructura;

namespace PlanAnual.Web.Controllers;

[AllowAnonymous]
public class AccountController(ILegajosClient legajos, AutenticadorUsersAccess autenticador, IConfiguration config, ILogger<AccountController> log) : Controller
{
    bool AccesoDeDesarrollo => config.GetValue<bool>("Acceso:AccesoDeDesarrollo");

    /// <summary>
    /// Llega del portal de UsersAccess con ?IdSesion=N: se valida la sesión, se leen los permisos del sistema
    /// y se abre la sesión propia. Sin IdSesion: en desarrollo, pantalla de prueba; en producción, vuelve al portal.
    /// </summary>
    [HttpGet("/login")]
    public async Task<IActionResult> Login(int? idSesion)
    {
        if (idSesion is > 0)
        {
            ResultadoIngreso r;
            try { r = await autenticador.IngresarAsync(idSesion.Value, HttpContext.RequestAborted); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                log.LogError(ex, "No se pudo consultar UsersAccess para la sesión {IdSesion}", idSesion);
                return View("NoIngreso", "No pudimos validar tu sesión con UsersAccess en este momento. Probá de nuevo en unos minutos.");
            }
            if (!r.Ok) return View("NoIngreso", r.Motivo);
            await HttpContext.SignOutAsync();   // si había otra persona en este navegador, se reemplaza
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Sesion.Crear(r.Usuario!));
            return RedirectToAction("Index", "Home");
        }

        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        if (!AccesoDeDesarrollo) return Redirect(config["Acceso:PortalUrl"]!);
        return View(await EmpresasAsync());
    }

    [HttpPost("/login/desarrollo")]
    public async Task<IActionResult> LoginDesarrollo(string cuit, Rol rol, string email)
    {
        if (!AccesoDeDesarrollo) return NotFound();
        var empresas = await EmpresasAsync();
        var emp = empresas.FirstOrDefault(e => e.Cuit == cuit);
        var nombre = rol == Rol.Empresa ? $"Usuario de prueba ({emp?.Razon ?? cuit})" : $"{rol} de prueba";
        var u = new UsuarioSesion(rol == Rol.Empresa ? 900000 + (int)(long.Parse(cuit) % 1000) : 1, nombre,
            string.IsNullOrWhiteSpace(email) ? "prueba@example.com" : email.Trim(),
            rol == Rol.Empresa ? [cuit] : [], rol);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Sesion.Crear(u));
        return RedirectToAction("Index", "Home");
    }

    [HttpPost("/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return AccesoDeDesarrollo ? RedirectToAction(nameof(Login)) : Redirect(config["Acceso:PortalUrl"]!);
    }

    [HttpGet("/sin-permiso")]
    public IActionResult SinPermiso() => View();

    [HttpGet("/error")]
    public IActionResult Error() => View();

    async Task<List<EmpresaItem>> EmpresasAsync() =>
        (await legajos.TodosLosBloquesAsync()).GroupBy(b => b.Cuit)
            .Select(g => new EmpresaItem(g.Key, g.First().RazonSocial, g.Count())).OrderBy(e => e.Razon).ToList();
}

public sealed record EmpresaItem(string Cuit, string Razon, int Bloques);
