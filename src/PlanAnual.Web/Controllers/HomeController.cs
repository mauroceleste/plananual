using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanAnual.Web.Infraestructura;

namespace PlanAnual.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    /// <summary>Lleva al Tablero (Subsecretaría y Administración) o a Mis áreas (empresas). Si venía con ?intro=, se lo pasa a esa pantalla para repetir la introducción.</summary>
    public IActionResult Index()
    {
        object? ruta = Request.Query.ContainsKey("intro") ? new { intro = Request.Query["intro"].ToString() } : null;
        return User.ToSesion().VeTodo ? RedirectToAction("Index", "Tablero", ruta) : RedirectToAction("Index", "Plan", ruta);
    }
}
