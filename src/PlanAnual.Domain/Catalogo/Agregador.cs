using PlanAnual.Domain.Entities;

namespace PlanAnual.Domain.Catalogo;

public sealed record SerieAnio(int Anio, decimal Convencional, decimal Cench)
{
    public decimal Total => Convencional + Cench;
}

public sealed record ItemValor(string Nombre, decimal Valor);

public sealed record PozosAnio(int Anio, decimal Perforados, decimal Abandonos);

public sealed record ProduccionAnio(int Anio, decimal Petroleo, decimal Gas);

public sealed record FilaEmpresa(string Cuit, string Razon, int Areas, decimal InversionFirme, decimal PozosFirme, decimal PetroleoFirme);

public sealed class DatosTablero
{
    public int AnioFirme { get; init; }
    /// <summary>Último año del horizonte que se está mostrando.</summary>
    public int UltimoAnio { get; init; }
    public int Presentaciones { get; init; }

    public decimal InversionFirme { get; init; }
    public decimal InversionPeriodo { get; init; }
    public decimal PozosFirme { get; init; }
    public decimal AbandonosFirme { get; init; }
    public decimal PetroleoFirme { get; init; }
    public decimal GasFirme { get; init; }
    public decimal FlujoFondosPeriodo { get; init; }

    public List<SerieAnio> InversionPorAnio { get; init; } = [];
    public List<ItemValor> InversionPorRubro { get; init; } = [];
    public List<ItemValor> InversionPorEmpresa { get; init; } = [];
    public List<PozosAnio> Pozos { get; init; } = [];
    public List<ProduccionAnio> Produccion { get; init; } = [];
    public List<FilaEmpresa> Empresas { get; init; } = [];

    // Cumplimiento (lo completa el servicio con las áreas de Legajos).
    public int AreasTotales { get; set; }
    public int AreasEnviadas { get; set; }
    public int AreasEnBorrador { get; set; }
    public int EmpresasTotales { get; set; }
    public int EmpresasCompletas { get; set; }
    public int EmpresasParciales { get; set; }
    public int EmpresasSinPresentar { get; set; }

    /// <summary>Indicadores fiscales, operativos y de calidad (los completa el servicio).</summary>
    public IndicadoresTablero? Indicadores { get; set; }
}

/// <summary>Suma lo que presentaron las empresas. Recibe solo la última versión enviada de cada área.</summary>
public static class Agregador
{
    static readonly (string Nombre, string[] Campos)[] Rubros =
    [
        ("Perforación y terminación", ["inv_perforacion", "inv_terminacion", "inv_desarrollo_cench"]),
        ("Workover", ["inv_workover"]),
        ("Pulling", ["inv_pulling"]),
        ("Instalaciones de producción", ["inv_instal_produccion"]),
        ("Plantas (ampliación y nuevas)", ["inv_ampl_plantas", "inv_plantas_nuevas"]),
        ("Ductos de evacuación", ["inv_ductos_petroleo", "inv_ductos_gas"]),
        ("Estudios G&G y sísmica", ["inv_estudios_gg", "inv_sismica"]),
    ];   // Suma exactamente la inversión total (el abandono es un gasto aparte y no entra).

    public static DatosTablero Calcular(IReadOnlyCollection<Presentacion> enviadas, int anioFirme, int cantidadAnios = 5)
    {
        var anios = Enumerable.Range(anioFirme, cantidadAnios).ToList();
        var calc = enviadas.ToDictionary(p => p.Id, p => Calculadora.CalcularPlan(p.TipoPlan, p.Valores));

        decimal V(Presentacion p, int anio, string campo) =>
            calc[p.Id].TryGetValue(anio, out var f) && f.TryGetValue(campo, out var v) ? v : 0m;

        // En CENCH "Desarrollo" es un solo campo; en Convencionales ya viene abierto en perforación y terminación.
        decimal Rubro(Presentacion p, int anio, string campo) =>
            campo == "inv_desarrollo_cench" ? (p.TipoPlan == TipoPlan.Cench ? V(p, anio, "inv_desarrollo") : 0m) : V(p, anio, campo);

        decimal SumaAnios(Presentacion p, string campo) => anios.Sum(a => V(p, a, campo));

        var porEmpresa = enviadas.GroupBy(p => p.Cuit).Select(g => new FilaEmpresa(
            g.Key, g.First().RazonSocial, g.Count(),
            g.Sum(p => V(p, anioFirme, "inv_total")), g.Sum(p => V(p, anioFirme, "pozos_perforados")), g.Sum(p => V(p, anioFirme, "prod_petroleo"))))
            .OrderByDescending(e => e.InversionFirme).ThenBy(e => e.Razon).ToList();

        var top = porEmpresa.Where(e => e.InversionFirme > 0).Take(8).Select(e => new ItemValor(e.Razon, e.InversionFirme)).ToList();
        var resto = porEmpresa.Where(e => e.InversionFirme > 0).Skip(8).ToList();
        if (resto.Count > 0) top.Add(new ItemValor($"Otras ({resto.Count})", resto.Sum(e => e.InversionFirme)));

        return new DatosTablero
        {
            AnioFirme = anioFirme,
            UltimoAnio = anios[^1],
            Presentaciones = enviadas.Count,
            InversionFirme = enviadas.Sum(p => V(p, anioFirme, "inv_total")),
            InversionPeriodo = enviadas.Sum(p => SumaAnios(p, "inv_total")),
            PozosFirme = enviadas.Sum(p => V(p, anioFirme, "pozos_perforados")),
            AbandonosFirme = enviadas.Sum(p => V(p, anioFirme, "pozos_abandonar")),
            PetroleoFirme = enviadas.Sum(p => V(p, anioFirme, "prod_petroleo")),
            GasFirme = enviadas.Sum(p => V(p, anioFirme, "prod_gas")),
            FlujoFondosPeriodo = enviadas.Sum(p => SumaAnios(p, "flujo_fondos")),

            InversionPorAnio = anios.Select(a => new SerieAnio(a,
                enviadas.Where(p => p.TipoPlan == TipoPlan.Convencional).Sum(p => V(p, a, "inv_total")),
                enviadas.Where(p => p.TipoPlan == TipoPlan.Cench).Sum(p => V(p, a, "inv_total")))).ToList(),

            InversionPorRubro = Rubros
                .Select(r => new ItemValor(r.Nombre, enviadas.Sum(p => r.Campos.Sum(c => Rubro(p, anioFirme, c)))))
                .Where(r => r.Valor > 0).OrderByDescending(r => r.Valor).ToList(),

            InversionPorEmpresa = top,
            Pozos = anios.Select(a => new PozosAnio(a, enviadas.Sum(p => V(p, a, "pozos_perforados")), enviadas.Sum(p => V(p, a, "pozos_abandonar")))).ToList(),
            Produccion = anios.Select(a => new ProduccionAnio(a, enviadas.Sum(p => V(p, a, "prod_petroleo")), enviadas.Sum(p => V(p, a, "prod_gas")))).ToList(),
            Empresas = porEmpresa,
        };
    }
}
