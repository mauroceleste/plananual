using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Servicios;
using PlanAnual.Web.Models;

namespace PlanAnual.Web.Controllers;

/// <summary>Vista de la Subsecretaría: quién presentó, quién no. Es la base del tablero.</summary>
[Authorize(Policy = "Subsecretaria")]
public class SeguimientoController(PresentacionService svc, ILegajosClient legajos, PlanAnualDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var vm = new SeguimientoVm { Ciclo = await svc.CicloAbiertoAsync() };
        try
        {
            var bloques = await legajos.TodosLosBloquesAsync();
            var pres = vm.Ciclo is null ? [] : await db.Presentaciones.AsNoTracking().Where(p => p.CicloId == vm.Ciclo.Id && p.Estado != EstadoPresentacion.Reemplazada).ToListAsync();
            vm.Empresas = bloques.GroupBy(b => b.Cuit).Select(g =>
            {
                var ids = g.Select(b => b.IdBloque).ToHashSet();
                var propias = pres.Where(p => p.Cuit == g.Key && ids.Contains(p.IdBloque)).ToList();
                return new EmpresaSeguimiento(g.Key, g.First().RazonSocial, ids.Count,
                    propias.Count(p => p.Estado == EstadoPresentacion.Enviada), propias.Count(p => p.Estado == EstadoPresentacion.Borrador));
            }).OrderBy(e => e.Razon).ToList();
            vm.Recientes = pres.Where(p => p.Estado == EstadoPresentacion.Enviada).OrderByDescending(p => p.EnviadaEn).Take(15).ToList();
        }
        catch (HttpRequestException) { vm.ErrorServicio = "No se pudo consultar Legajos de Áreas."; }
        return View(vm);
    }
}
