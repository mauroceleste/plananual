using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanAnual.Web.Infraestructura;

namespace PlanAnual.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        // La primera vez que alguien entra, se le muestra la animación de introducción (después queda el enlace "Ver introducción").
        if (!Request.Cookies.ContainsKey(IntroduccionController.Cookie)) return RedirectToAction("Index", "Introduccion");
        return User.ToSesion().VeTodo ? RedirectToAction("Index", "Tablero") : RedirectToAction("Index", "Plan");
    }
}
