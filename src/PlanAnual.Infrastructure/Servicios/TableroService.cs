using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Legajos;

namespace PlanAnual.Infrastructure.Servicios;

/// <param name="Anios">Cuántos años mostrar desde el año firme; 0 = todos los que tengan datos cargados (hasta 30).</param>
public sealed record FiltroTablero(TipoPlan? Tipo, string? Cuit, int Anios = 10);

public sealed class TableroService(PlanAnualDbContext db, ILegajosClient legajos, ITipoActividadProvider tiposProvider)
{
    /// <summary>Tablero del ciclo: lo enviado (última versión de cada área) más el cumplimiento contra las áreas de Legajos.</summary>
    public async Task<DatosTablero> ObtenerAsync(Ciclo ciclo, FiltroTablero filtro)
    {
        var tipos = await tiposProvider.TodosAsync();
        var bloques = (await legajos.TodosLosBloquesAsync()).AsEnumerable();
        if (filtro.Cuit is not null) bloques = bloques.Where(b => b.Cuit == filtro.Cuit);
        if (filtro.Tipo is { } t) bloques = bloques.Where(b => tipos.TryGetValue(b.IdBloque, out var tb) && tb == t);
        var areas = bloques.ToList();
        var idsAreas = areas.Select(a => a.IdBloque).ToHashSet();

        var q = db.Presentaciones.AsNoTracking().Include(p => p.Valores).Include(p => p.Anexos)
            .Where(p => p.CicloId == ciclo.Id && p.Estado != EstadoPresentacion.Borrador);
        if (filtro.Tipo is { } tp) q = q.Where(p => p.TipoPlan == tp);
        if (filtro.Cuit is not null) q = q.Where(p => p.Cuit == filtro.Cuit);
        var versiones = (await q.AsSplitQuery().ToListAsync()).Where(p => idsAreas.Contains(p.IdBloque)).ToList();
        var enviadas = versiones.Where(p => p.Estado != EstadoPresentacion.Reemplazada).ToList();

        var borradores = await db.Presentaciones.AsNoTracking()
            .Where(p => p.CicloId == ciclo.Id && p.Estado == EstadoPresentacion.Borrador).Select(p => new { p.Cuit, p.IdBloque }).ToListAsync();
        var idsBorrador = borradores.Where(b => idsAreas.Contains(b.IdBloque)).Select(b => b.IdBloque).ToHashSet();

        var ultimoCargado = enviadas.SelectMany(p => p.Valores).Select(v => v.Anio).DefaultIfEmpty(ciclo.Anio).Max();
        var cantidad = filtro.Anios > 0 ? filtro.Anios : Math.Clamp(ultimoCargado - ciclo.Anio + 1, 5, 30);
        var datos = Agregador.Calcular(enviadas, ciclo.Anio, cantidad);
        datos.Indicadores = Indicadores.Calcular(enviadas, versiones, ciclo, ciclo.Anio, cantidad);

        var enviadasPorEmpresa = enviadas.GroupBy(p => p.Cuit).ToDictionary(g => g.Key, g => g.Select(p => p.IdBloque).Distinct().Count());
        datos.AreasTotales = areas.Count;
        datos.AreasEnviadas = enviadas.Select(p => p.IdBloque).Distinct().Count();
        datos.AreasEnBorrador = idsBorrador.Except(enviadas.Select(p => p.IdBloque)).Count();
        foreach (var g in areas.GroupBy(a => a.Cuit))
        {
            datos.EmpresasTotales++;
            var env = enviadasPorEmpresa.GetValueOrDefault(g.Key);
            if (env >= g.Count()) datos.EmpresasCompletas++; else if (env > 0) datos.EmpresasParciales++; else datos.EmpresasSinPresentar++;
        }
        return datos;
    }

    public async Task<List<(string Cuit, string Razon)>> EmpresasAsync() =>
        (await legajos.TodosLosBloquesAsync()).GroupBy(b => b.Cuit).Select(g => (g.Key, g.First().RazonSocial)).OrderBy(e => e.RazonSocial).ToList();
}
