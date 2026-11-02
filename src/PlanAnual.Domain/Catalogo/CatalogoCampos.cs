using PlanAnual.Domain.Entities;

namespace PlanAnual.Domain.Catalogo;

/// <summary>
/// Definición de una columna de la planilla. Los formularios, validaciones, la exportación
/// a Excel y el tablero se generan desde este catálogo: agregar o corregir un campo no
/// requiere migración de base de datos.
/// </summary>
public sealed record CampoDef(
    string Codigo,
    string Grupo,
    string Nombre,
    string Unidad,
    TipoPlan[] Planes,
    string? Ayuda = null,
    string[]? Suma = null,
    string[]? Resta = null,
    bool Entero = false,
    int Decimales = 6,
    decimal? Maximo = null)
{
    /// <summary>Cuántos decimales admite la celda: 0 = número entero. El máximo (6) es lo que guarda la base de datos.</summary>
    public int DecimalesMax => Entero ? 0 : Decimales;
    public bool EsCalculado => Suma is not null;
    public bool Aplica(TipoPlan plan) => Planes.Contains(plan);
}

public static class CatalogoCampos
{
    static readonly TipoPlan[] Ambos = { TipoPlan.Convencional, TipoPlan.Cench };
    static readonly TipoPlan[] Conv = { TipoPlan.Convencional };
    static readonly TipoPlan[] Cench = { TipoPlan.Cench };

    public const string GPozos = "Pozos";
    public const string GCapacidad = "Capacidad actual y proyectada";
    public const string GInversion = "Inversión";
    public const string GAbandono = "Abandono de pozos";
    public const string GGastos = "Gastos operativos";
    public const string GProduccion = "Producción y venta";
    public const string GPrecios = "Precios";
    public const string GFlujo = "Ingresos, impuestos y flujo de fondos";

    static readonly string[] RubrosInversionComunes =
    {
        "inv_ampl_plantas", "inv_plantas_nuevas", "inv_ductos_petroleo", "inv_ductos_gas",
        "inv_instal_produccion", "inv_estudios_gg", "inv_sismica"
    };
    static readonly string[] SubrubrosDesarrolloConv = { "inv_perforacion", "inv_terminacion", "inv_workover", "inv_pulling" };
    static readonly string[] GastosConv =
    {
        "gto_salarios", "gto_pulling", "gto_workover", "gto_energia", "gto_quimicos", "gto_compresion",
        "gto_vehiculos", "gto_superficiarios", "gto_servidumbres", "gto_canon", "gto_tasas", "gto_otros"
    };

    public static readonly IReadOnlyList<CampoDef> Todos = new List<CampoDef>
    {
        // Pozos
        new("pozos_perforados", GPozos, "Pozos productores perforados", "#", Ambos, Entero: true),
        new("pozos_conectados", GPozos, "Pozos productores conectados", "#", Ambos, Entero: true),
        new("equipos_perforacion", GPozos, "Equipos de perforación (máximo del año)", "#", Ambos, Decimales: 2),
        new("set_fracturas", GPozos, "Set de fracturas (máximo del año)", "#", Ambos, Decimales: 2),
        new("fracturas", GPozos, "Fracturas (etapas del año)", "#", Ambos, Entero: true),
        new("pozos_abandonar", GPozos, "Pozos a abandonar", "#", Ambos, Entero: true),
        new("act_pulling", GPozos, "Act. Pulling", "#", Conv, "Pozos intervenidos con pulling en el año.", Entero: true),
        new("act_workover", GPozos, "Act. Workover", "#", Conv, "Pozos intervenidos con workover en el año.", Entero: true),

        // Capacidad
        new("cap_proceso_petroleo", GCapacidad, "Proceso - Petróleo", "bbl/d", Ambos, Entero: true),
        new("cap_proceso_gas", GCapacidad, "Proceso - Gas", "MMm3/d", Ambos),
        new("cap_evac_petroleo", GCapacidad, "Evacuación - Petróleo", "bbl/d", Ambos, Entero: true),
        new("cap_evac_gas", GCapacidad, "Evacuación - Gas", "MMm3/d", Ambos),

        // Inversión. CENCH carga Desarrollo; Convencionales lo calcula de sus 4 componentes.
        new("inv_desarrollo", GInversion, "Desarrollo", "MMUSD", Cench, "Perforación y terminación de pozos."),
        new("inv_perforacion", GInversion, "Perforación", "MMUSD", Conv),
        new("inv_terminacion", GInversion, "Terminación", "MMUSD", Conv),
        new("inv_workover", GInversion, "Workover", "MMUSD", Conv),
        new("inv_pulling", GInversion, "Pulling", "MMUSD", Conv),
        new("inv_desarrollo", GInversion, "Desarrollo (subtotal)", "MMUSD", Conv, Suma: SubrubrosDesarrolloConv),
        new("inv_ampl_plantas", GInversion, "Ampliación de capacidad de plantas", "MMUSD", Ambos),
        new("inv_plantas_nuevas", GInversion, "Plantas nuevas", "MMUSD", Ambos),
        new("inv_ductos_petroleo", GInversion, "Ductos de evacuación de petróleo", "MMUSD", Ambos),
        new("inv_ductos_gas", GInversion, "Ductos de evacuación de gas", "MMUSD", Ambos),
        new("inv_instal_produccion", GInversion, "Instalaciones de producción", "MMUSD", Ambos),
        new("inv_estudios_gg", GInversion, "Estudios G&G", "MMUSD", Ambos),
        new("inv_sismica", GInversion, "Adquisición sísmica", "MMUSD", Ambos),
        new("inv_total", GInversion, "Total inversión", "MMUSD", Ambos,
            Suma: new[] { "inv_desarrollo" }.Concat(RubrosInversionComunes).ToArray()),

        // Abandono
        new("abandono_usd", GAbandono, "Abandono de pozos", "MMUSD", Conv),

        // Gastos operativos
        new("gto_fijo", GGastos, "Fijo", "MMUSD", Cench, "No asociado directamente a la producción."),
        new("gto_variable", GGastos, "Variables", "MMUSD", Cench, "Asociado directamente a la producción."),
        new("gto_salarios", GGastos, "Salarios", "MMUSD", Conv),
        new("gto_pulling", GGastos, "Pulling", "MMUSD", Conv),
        new("gto_workover", GGastos, "Workover", "MMUSD", Conv),
        new("gto_energia", GGastos, "Energía eléctrica", "MMUSD", Conv),
        new("gto_quimicos", GGastos, "Químicos", "MMUSD", Conv),
        new("gto_compresion", GGastos, "Compresión", "MMUSD", Conv),
        new("gto_vehiculos", GGastos, "Vehículos y combustibles", "MMUSD", Conv),
        new("gto_superficiarios", GGastos, "Superficiarios", "MMUSD", Conv),
        new("gto_servidumbres", GGastos, "Servidumbres", "MMUSD", Conv),
        new("gto_canon", GGastos, "Canon hidrocarburífero", "MMUSD", Conv),
        new("gto_tasas", GGastos, "Tasas", "MMUSD", Conv),
        new("gto_otros", GGastos, "Otros", "MMUSD", Conv, "Solo con justificación."),
        new("gto_total", GGastos, "Total gastos operativos", "MMUSD", Cench, Suma: new[] { "gto_fijo", "gto_variable" }),
        new("gto_total", GGastos, "Total gastos operativos", "MMUSD", Conv, Suma: GastosConv),

        // Producción y venta
        new("prod_petroleo", GProduccion, "Producción de petróleo", "MMbbl", Ambos),
        new("venta_petroleo", GProduccion, "Venta de petróleo", "MMbbl", Ambos),
        new("prod_gas", GProduccion, "Producción de gas (kcal reales)", "MMm3", Ambos),
        new("venta_gas", GProduccion, "Venta de gas (9300 kcal)", "MMm3", Ambos),
        new("prod_agua", GProduccion, "Agua", "MMm3", Ambos),
        new("desarrollo_recursos", GProduccion, "Desarrollo de recursos (acumulado)", "%", Ambos,
            "Porcentaje acumulado de los recursos técnicamente recuperables extraídos.", Decimales: 2, Maximo: 100),

        // Precios. El modelo oficial rotula petróleo en USD/MMBtu, lo que no es coherente: se usa USD/bbl.
        new("precio_gas", GPrecios, "Gas", "USD/MMBtu", Ambos, Decimales: 2),
        new("precio_petroleo", GPrecios, "Petróleo", "USD/bbl", Ambos, Decimales: 2),

        // Flujo
        new("ingresos", GFlujo, "Ingresos", "MMUSD", Ambos),
        new("regalias", GFlujo, "Regalías", "MMUSD", Ambos),
        new("iibb_otros", GFlujo, "IIBB y otros", "MMUSD", Ambos),
        new("iigg", GFlujo, "Impuesto a las ganancias", "MMUSD", Ambos),
        new("flujo_fondos", GFlujo, "Flujo de fondos (después de IIGG)", "MMUSD", Cench,
            Suma: new[] { "ingresos" },
            Resta: new[] { "inv_total", "gto_total", "regalias", "iibb_otros", "iigg" }),
        new("flujo_fondos", GFlujo, "Flujo de fondos (después de IIGG)", "MMUSD", Conv,
            Suma: new[] { "ingresos" },
            Resta: new[] { "inv_total", "abandono_usd", "gto_total", "regalias", "iibb_otros", "iigg" }),
    };

    public static IReadOnlyList<CampoDef> Para(TipoPlan plan) => Todos.Where(c => c.Aplica(plan)).ToList();

    public static CampoDef? Buscar(TipoPlan plan, string codigo) =>
        Todos.FirstOrDefault(c => c.Aplica(plan) && c.Codigo == codigo);
}
