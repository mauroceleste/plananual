using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanAnual.Web.Infraestructura;

namespace PlanAnual.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    public IActionResult Index() =>
        User.ToSesion().VeTodo ? RedirectToAction("Index", "Tablero") : RedirectToAction("Index", "Plan");
}
