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

    /// <summary>Identifica una fila de la planilla: año * 100 + mes (mes 0 = la fila anual, o el subtotal anual del primer año).</summary>
    public static int Clave(int anio, int mes) => anio * 100 + mes;

    /// <summary>Pasa los valores de los meses de un año a un único valor anual según la regla del campo.</summary>
    public static decimal AgregarMeses(CampoDef? campo, IReadOnlyList<decimal> porMes) => (campo?.Agregacion ?? Agregacion.Suma) switch
    {
        Agregacion.Promedio => Math.Round(porMes.Average(), 6),
        Agregacion.Maximo => porMes.Max(),
        Agregacion.Ultimo => porMes[^1],
        _ => Math.Round(porMes.Sum(), 6)
    };

    /// <summary>
    /// Todas las filas de la planilla con sus campos calculados: una por mes del primer año (clave año*100+mes) y una anual por año
    /// (clave año*100). En el primer año la fila anual es el subtotal de los meses, con la regla de cada campo.
    /// </summary>
    public static SortedDictionary<int, Dictionary<string, decimal>> CalcularPeriodos(TipoPlan plan, IEnumerable<PlanValor> valores)
    {
        var res = new SortedDictionary<int, Dictionary<string, decimal>>();
        var lista = valores.Where(v => v.Valor.HasValue).ToList();

        foreach (var g in lista.Where(v => v.Mes > 0).GroupBy(v => Clave(v.Anio, v.Mes)))
            res[g.Key] = CalcularAnio(plan, g.ToDictionary(v => v.Campo, v => v.Valor));

        foreach (var anio in lista.Select(v => v.Anio).Distinct())
        {
            var anuales = new Dictionary<string, decimal?>();
            foreach (var v in lista.Where(v => v.Anio == anio && v.Mes == 0)) anuales[v.Campo] = v.Valor;
            foreach (var g in lista.Where(v => v.Anio == anio && v.Mes > 0).GroupBy(v => v.Campo))
                anuales[g.Key] = AgregarMeses(CatalogoCampos.Buscar(plan, g.Key), g.OrderBy(v => v.Mes).Select(v => v.Valor!.Value).ToList());
            res[Clave(anio, 0)] = CalcularAnio(plan, anuales);
        }
        return res;
    }

    /// <summary>Un valor por año (los meses ya agregados): es lo que usan el tablero, los indicadores y el comprobante.</summary>
    public static SortedDictionary<int, Dictionary<string, decimal>> CalcularPlan(TipoPlan plan, IEnumerable<PlanValor> valores) =>
        new(CalcularPeriodos(plan, valores).Where(kv => kv.Key % 100 == 0).ToDictionary(kv => kv.Key / 100, kv => kv.Value));
}
