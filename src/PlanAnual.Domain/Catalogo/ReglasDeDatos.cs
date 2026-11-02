namespace PlanAnual.Domain.Catalogo;

/// <summary>Qué valores admite cada celda. La usan el guardado, el control previo al envío y las pruebas.</summary>
public static class ReglasDeDatos
{
    /// <returns>null si el valor sirve para el campo; si no, el motivo en español.</returns>
    public static string? Motivo(CampoDef c, decimal valor)
    {
        if (valor < 0) return $"{c.Nombre}: no admite valores negativos.";
        if (c.DecimalesMax == 0 && valor != decimal.Truncate(valor)) return $"{c.Nombre}: debe ser un número entero (sin decimales).";
        if (c.DecimalesMax > 0 && Math.Round(valor, c.DecimalesMax) != valor)
            return $"{c.Nombre}: admite hasta {c.DecimalesMax} {(c.DecimalesMax == 1 ? "decimal" : "decimales")}.";
        if (c.Maximo is { } max && valor > max) return $"{c.Nombre}: no puede superar {max:0.##}.";
        return null;
    }

    /// <summary>Texto corto para el encabezado de la columna.</summary>
    public static string Descripcion(CampoDef c) =>
        c.DecimalesMax == 0 ? "entero" : c.DecimalesMax == 6 ? "decimales" : $"hasta {c.DecimalesMax} dec.";
}
