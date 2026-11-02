using PlanAnual.Domain.Entities;

namespace PlanAnual.Domain.Catalogo;

/// <summary>Calcula los campos derivados (subtotales, total de inversión, gastos y flujo de fondos).</summary>
public static class Calculadora
{
    /// <param name="ingresados">Valores cargados por la empresa para un año (código -> valor).</param>
    /// <returns>Los mismos valores más los campos calculados del tipo de plan.</returns>
    public static Dictionary<string, decimal> CalcularAnio(TipoPlan plan, IReadOnlyDictionary<string, decimal?> ingresados)
    {
        var res = new Dictionary<string, decimal>();
        foreach (var kv in ingresados)
            if (kv.Value.HasValue) res[kv.Key] = kv.Value.Value;

        // El orden del catálogo garantiza que un campo calculado solo depende de los anteriores.
        foreach (var c in CatalogoCampos.Para(plan).Where(c => c.EsCalculado))
        {
            decimal total = c.Suma!.Sum(k => res.GetValueOrDefault(k));
            if (c.Resta is not null) total -= c.Resta.Sum(k => res.GetValueOrDefault(k));
            res[c.Codigo] = Math.Round(total, 6);
        }
        return res;
    }

    public static SortedDictionary<int, Dictionary<string, decimal>> CalcularPlan(
        TipoPlan plan, IEnumerable<PlanValor> valores) =>
        new(valores.GroupBy(v => v.Anio).ToDictionary(
            g => g.Key,
            g => CalcularAnio(plan, g.ToDictionary(v => v.Campo, v => v.Valor))));
}
