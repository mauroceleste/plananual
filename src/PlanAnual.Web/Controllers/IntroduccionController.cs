using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlanAnual.Web.Controllers;

/// <summary>
/// Animación de introducción al sistema (unos 75 segundos). Es una página pública y autocontenida: no muestra datos de nadie (las cifras son ilustrativas).
/// Al abrirla se anota en una cookie que ya se vio, para que el inicio la muestre sola solo la primera vez.
/// </summary>
[AllowAnonymous]
public class IntroduccionController : Controller
{
    public const string Cookie = "plananual.intro";

    [HttpGet("Introduccion")]
    public IActionResult Index()
    {
        Response.Cookies.Append(Cookie, "1", new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true,
            Path = Request.PathBase.HasValue ? Request.PathBase.Value : "/",
        });
        return View();
    }
}
