using Microsoft.AspNetCore.Mvc;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Web.Componentes;

/// <summary>Cifras públicas que usa el cierre de la introducción animada (empresas y áreas de Legajos de Áreas y año de la convocatoria). Si Legajos no responde, la intro sale sin esas cifras.</summary>
public sealed record IntroDatos(int Empresas, int Areas, int Anio);

public class IntroViewComponent(ILegajosClient legajos, PresentacionService svc) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        int empresas = 0, areas = 0;
        try
        {
            var bloques = await legajos.TodosLosBloquesAsync();
            areas = bloques.Count; empresas = bloques.Select(b => b.Cuit).Distinct().Count();
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        var ciclo = await svc.CicloParaConsultaAsync(null);
        return View(new IntroDatos(empresas, areas, ciclo?.Anio ?? DateTime.Today.Year));
    }
}
