using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Legajos;

namespace PlanAnual.Infrastructure.Servicios;

/// <summary>De dónde sale el tipo de planilla (Convencional o CENCH) de cada bloque.</summary>
public interface ITipoActividadProvider
{
    /// <summary>Tipo vigente de cada bloque que tenga uno definido (idbloque -> tipo).</summary>
    Task<IReadOnlyDictionary<int, TipoPlan>> TodosAsync();
    Task<TipoPlan?> DeAsync(int idBloque);
}

public static class TipoActividadTexto
{
    /// <summary>Interpreta lo que devuelva el servicio de Legajos: "No convencional", "CENCH", "NC", "Convencional"...</summary>
    public static TipoPlan? Interpretar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = new string(texto.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .ToLowerInvariant().Replace('_', ' ').Replace('-', ' ').Trim();
        if (t is "cench" or "nc" or "no convencional" or "noconvencional" || t.StartsWith("no convencional") || t.Contains("cench")) return TipoPlan.Cench;
        if (t is "c" or "convencional" || t.StartsWith("convencional")) return TipoPlan.Convencional;
        return null;
    }
}

/// <summary>
/// 1) Si la Subsecretaría lo fijó a mano (Administración → Tipo de actividad), manda esa decisión.
/// 2) Si no, se usa el tipo que informe el servicio de Legajos (campo `tipo_actividad`, cuando exista).
/// 3) Si ninguno lo define, el área no puede comenzar.
/// </summary>
public sealed class TipoActividadProvider(PlanAnualDbContext db, ILegajosClient legajos) : ITipoActividadProvider
{
    public async Task<IReadOnlyDictionary<int, TipoPlan>> TodosAsync()
    {
        var res = new Dictionary<int, TipoPlan>();
        foreach (var b in await legajos.TodosLosBloquesAsync())
            if (TipoActividadTexto.Interpretar(b.TipoActividad) is { } t) res[b.IdBloque] = t;
        foreach (var l in await db.BloquesTipoActividad.AsNoTracking().ToListAsync()) res[l.IdBloque] = l.TipoPlan;   // lo manual pisa
        return res;
    }

    public async Task<TipoPlan?> DeAsync(int idBloque) =>
        (await TodosAsync()).TryGetValue(idBloque, out var t) ? t : null;
}
