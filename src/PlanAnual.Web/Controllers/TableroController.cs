using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Servicios;
using PlanAnual.Web.Models;

namespace PlanAnual.Web.Controllers;

[Authorize(Policy = "Subsecretaria")]
public class TableroController(TableroService tablero, PresentacionService presentaciones) : Controller
{
    public async Task<IActionResult> Index(TipoPlan? tipo, string? cuit, int anios = 10)
    {
        if (cuit is not null && !cuit.All(char.IsDigit)) cuit = null;
        if (anios is not (0 or 5 or 10 or 15)) anios = 10;
        var vm = new TableroVm { Ciclo = await presentaciones.CicloAbiertoAsync(), Tipo = tipo, Cuit = cuit, Anios = anios };
        if (vm.Ciclo is null) return View(vm);
        try
        {
            vm.Datos = await tablero.ObtenerAsync(vm.Ciclo, new FiltroTablero(tipo, cuit, anios));
            vm.Empresas = await tablero.EmpresasAsync();
        }
        catch (HttpRequestException) { vm.ErrorServicio = "No se pudo consultar Legajos de Áreas. Probá de nuevo en unos minutos."; }
        return View(vm);
    }
}
