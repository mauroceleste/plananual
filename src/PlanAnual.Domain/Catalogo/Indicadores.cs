using PlanAnual.Domain.Entities;

namespace PlanAnual.Domain.Catalogo;

// ===================== Resultados =====================

/// <summary>Un año del horizonte, sumando todas las áreas enviadas. Los cocientes se calculan sobre esas sumas.</summary>
public sealed record IndicadoresAnio(
    int Anio,
    decimal Ingresos, decimal Regalias, decimal IibbOtros, decimal Iigg, decimal Inversion, decimal Opex,
    decimal FlujoFondos, decimal FlujoAcumulado,
    decimal PozosPerforados, decimal PozosConectados, decimal SinConectarAcumulado, decimal Equipos, decimal Fracturas,
    decimal PozosAbandonar, decimal AbandonoUsd,
    decimal PetroleoMMbbl, decimal GasMMm3, decimal AguaMMm3,
    decimal ProduccionConCapacidadBbld, decimal CapacidadProcesoBbld)
{
    public decimal RentaFiscal => Regalias + IibbOtros;
    public decimal? RentaFiscalPct => Ingresos > 0 ? RentaFiscal / Ingresos * 100 : null;
    public decimal? CargaFiscalPct => Ingresos > 0 ? (RentaFiscal + Iigg) / Ingresos * 100 : null;
    public decimal? ReinversionPct => Ingresos > 0 ? Inversion / Ingresos * 100 : null;
    /// <summary>MMbbl por año a miles de barriles por día.</summary>
    public decimal PetroleoKbbld => PetroleoMMbbl * 1000m / 365m;
    public decimal GasMMm3d => GasMMm3 / 365m;
    /// <summary>Relación gas/petróleo en m³ de gas por m³ de petróleo.</summary>
    public decimal? Gor => PetroleoMMbbl > 0 ? GasMMm3 / (PetroleoMMbbl * Indicadores.MMm3PorMMbbl) : null;
    /// <summary>Relación agua/petróleo en m³ de agua por m³ de petróleo.</summary>
    public decimal? Wor => PetroleoMMbbl > 0 ? AguaMMm3 / (PetroleoMMbbl * Indicadores.MMm3PorMMbbl) : null;
    public decimal? UtilizacionPct => CapacidadProcesoBbld > 0 ? ProduccionConCapacidadBbld / CapacidadProcesoBbld * 100 : null;
}

public sealed record Escenario(int PctPrecioPetroleo, decimal Ingresos, decimal RentaFiscal, decimal FlujoFondos);

public sealed record MixFila(string Nombre, decimal Convencional, decimal Cench)
{
    public decimal Total => Convencional + Cench;
    public decimal PctCench => Total > 0 ? Cench / Total * 100 : 0;
}

public sealed record AlertaPlanilla(long Id, string Empresa, string Area, int Cantidad, string Ejemplo);

public sealed record Atipico(long Id, string Empresa, string Area, string Metrica, string Unidad, decimal Valor, decimal Mediana, decimal Veces, bool MasAlto);

public sealed record CambioVersion(long Id, string Empresa, string Area, int VersionNueva, decimal InversionAntes, decimal InversionDespues,
    decimal? InversionDeltaPct, decimal? PetroleoDeltaPct);

public sealed record MapaFila(string Nombre, List<decimal> Valores)
{
    public decimal Total => Valores.Sum();
}

public sealed class Cumplimiento
{
    public int Enviadas { get; init; }
    public int EnPlazo { get; init; }
    public int FueraDePlazo { get; init; }
    public decimal DemoraPromedioDias { get; init; }
    public int DemoraMaximaDias { get; init; }
    public int Rectificatorias { get; init; }
    public int ConAnexosRecomendadosCompletos { get; init; }
    public int ConAdvertenciasDeDatos { get; init; }
}

public sealed class IndicadoresTablero
{
    public List<IndicadoresAnio> Anios { get; init; } = [];

    // ---- Fiscal y economía (horizonte mostrado)
    public decimal IngresosHorizonte { get; init; }
    public decimal RentaFiscalHorizonte { get; init; }
    public decimal InversionHorizonte { get; init; }
    public decimal FlujoFondosHorizonte { get; init; }
    public decimal? RentaFiscalPct { get; init; }
    public decimal? CargaFiscalPct { get; init; }
    public decimal? ReinversionPct { get; init; }
    /// <summary>SinDeficit | Recupera | NoRecupera | SinDatos</summary>
    public string PaybackEstado { get; init; } = "SinDatos";
    public int? PaybackAnio { get; init; }
    public decimal Top3Pct { get; init; }
    public decimal Top5Pct { get; init; }
    public decimal Hhi { get; init; }
    public List<MixFila> Mix { get; init; } = [];
    public List<Escenario> Sensibilidad { get; init; } = [];

    // ---- Operación (año firme, salvo donde se aclara)
    public decimal SinConectarFirme { get; init; }
    public decimal? PozosPorEquipo { get; init; }
    public decimal? EtapasPorPozo { get; init; }
    public decimal PetroleoKbbldFirme { get; init; }
    public decimal GasMMm3dFirme { get; init; }
    public decimal? GorFirme { get; init; }
    public decimal? WorFirme { get; init; }
    public decimal? UtilizacionFirmePct { get; init; }
    public int AreasConCapacidad { get; init; }
    public int AreasSobreCapacidad { get; init; }
    public int AreasUtilizacionAlta { get; init; }
    public decimal? CapexPorPozoFirme { get; init; }
    public decimal? OpexPorBoeFirme { get; init; }
    /// <summary>Salarios declarados en el año firme (MMUSD); aproximación al empleo, no cuenta personas.</summary>
    public decimal SalariosFirme { get; init; }
    public decimal? SalariosPctGastosFirme { get; init; }
    /// <summary>Salarios por boe producido (USD/boe), sobre las áreas que producen.</summary>
    public decimal? SalariosPorBoeFirme { get; init; }
    public decimal AbandonoPozosHorizonte { get; init; }
    public decimal AbandonoUsdHorizonte { get; init; }
    public decimal? AbandonoPctInversion { get; init; }

    // ---- Calidad y cumplimiento
    public Cumplimiento Cumplimiento { get; init; } = new();
    public List<AlertaPlanilla> Alertas { get; init; } = [];
    public List<Atipico> Atipicos { get; init; } = [];
    public List<CambioVersion> Cambios { get; init; } = [];
    public List<MapaFila> MapaInversion { get; init; } = [];
}

// ===================== Cálculo =====================

public static class Indicadores
{
    /// <summary>1 MMbbl = 0,158987 MMm³ (conversión de volumen de petróleo).</summary>
    public const decimal MMm3PorMMbbl = 0.158987m;
    /// <summary>Barriles equivalentes de petróleo por cada MMm³ de gas (≈ 6,3 boe por 1.000 m³), expresado en MMboe.</summary>
    public const decimal MMboePorMMm3Gas = 0.0063m;
    /// <summary>Tasa de ganancias que se usa solo para estimar el efecto de un cambio de precio.</summary>
    public const decimal TasaGananciasEstimada = 0.35m;
    public static readonly int[] EscenariosPrecio = [-20, -10, 0, 10, 20];

    /// <param name="enviadas">Última versión enviada de cada área (la vigente).</param>
    /// <param name="todasLasVersiones">Enviadas y reemplazadas (todo lo que no es borrador): sirve para rectificatorias.</param>
    public static IndicadoresTablero Calcular(
        IReadOnlyCollection<Presentacion> enviadas, IReadOnlyCollection<Presentacion> todasLasVersiones,
        Ciclo ciclo, int anioFirme, int cantidadAnios)
    {
        var anios = Enumerable.Range(anioFirme, cantidadAnios).ToList();
        var calc = todasLasVersiones.Concat(enviadas).DistinctBy(p => p.Id)
            .ToDictionary(p => p.Id, p => Calculadora.CalcularPlan(p.TipoPlan, p.Valores));

        decimal V(Presentacion p, int a, string c) => calc[p.Id].TryGetValue(a, out var f) && f.TryGetValue(c, out var v) ? v : 0m;
        decimal S(int a, string c) => enviadas.Sum(p => V(p, a, c));

        // ---------- serie por año ----------
        var serie = new List<IndicadoresAnio>();
        decimal acumFlujo = 0, acumSinConectar = 0;
        foreach (var a in anios)
        {
            var ing = S(a, "ingresos"); var reg = S(a, "regalias"); var iib = S(a, "iibb_otros"); var gan = S(a, "iigg");
            var ff = S(a, "flujo_fondos");
            acumFlujo += ff;
            var perf = S(a, "pozos_perforados"); var con = S(a, "pozos_conectados");
            acumSinConectar += perf - con;
            decimal prodCap = 0, cap = 0;
            foreach (var p in enviadas)
            {
                var c = V(p, a, "cap_proceso_petroleo");
                if (c > 0) { prodCap += V(p, a, "prod_petroleo") * 1_000_000m / 365m; cap += c; }
            }
            serie.Add(new IndicadoresAnio(a, ing, reg, iib, gan, S(a, "inv_total"), S(a, "gto_total"), ff, acumFlujo,
                perf, con, acumSinConectar, S(a, "equipos_perforacion"), S(a, "fracturas"), S(a, "pozos_abandonar"), S(a, "abandono_usd"),
                S(a, "prod_petroleo"), S(a, "prod_gas"), S(a, "prod_agua"), prodCap, cap));
        }

        var firme = serie[0];
        decimal ingH = serie.Sum(x => x.Ingresos), rentaH = serie.Sum(x => x.RentaFiscal), invH = serie.Sum(x => x.Inversion),
                ffH = serie.Sum(x => x.FlujoFondos), ganH = serie.Sum(x => x.Iigg);

        // ---------- payback del sector ----------
        string estado = "SinDatos"; int? anioPayback = null;
        if (enviadas.Count > 0)
        {
            bool huboDeficit = false; estado = "NoRecupera";
            foreach (var x in serie)
            {
                if (x.FlujoAcumulado < 0) huboDeficit = true;
                else if (huboDeficit) { anioPayback = x.Anio; estado = "Recupera"; break; }
            }
            if (!huboDeficit) estado = "SinDeficit";
        }

        // ---------- concentración (inversión del año firme por empresa) ----------
        var porEmpresa = enviadas.GroupBy(p => p.Cuit).Select(g => g.Sum(p => V(p, anioFirme, "inv_total"))).OrderByDescending(x => x).ToList();
        var totalFirme = porEmpresa.Sum();
        decimal top3 = 0, top5 = 0, hhi = 0;
        if (totalFirme > 0)
        {
            top3 = porEmpresa.Take(3).Sum() / totalFirme * 100;
            top5 = porEmpresa.Take(5).Sum() / totalFirme * 100;
            hhi = porEmpresa.Sum(x => (x / totalFirme * 100) * (x / totalFirme * 100));
        }

        // ---------- mezcla CENCH / convencional (horizonte) ----------
        MixFila Mezcla(string nombre, string campo) => new(nombre,
            enviadas.Where(p => p.TipoPlan == TipoPlan.Convencional).Sum(p => anios.Sum(a => V(p, a, campo))),
            enviadas.Where(p => p.TipoPlan == TipoPlan.Cench).Sum(p => anios.Sum(a => V(p, a, campo))));

        // ---------- sensibilidad al precio del petróleo (estimación lineal) ----------
        var escenarios = new List<Escenario>();
        foreach (var pct in EscenariosPrecio)
        {
            decimal ing = 0, renta = 0, ff = 0;
            foreach (var p in enviadas)
                foreach (var a in anios)
                {
                    var ingreso = V(p, a, "ingresos"); var reg = V(p, a, "regalias"); var iib = V(p, a, "iibb_otros"); var gan = V(p, a, "iigg");
                    var dIng = V(p, a, "venta_petroleo") * V(p, a, "precio_petroleo") * pct / 100m;
                    var dReg = ingreso > 0 ? reg / ingreso * dIng : 0m;           // regalías e IIBB acompañan al ingreso
                    var dIib = ingreso > 0 ? iib / ingreso * dIng : 0m;
                    var dGan = Math.Max(-gan, TasaGananciasEstimada * (dIng - dReg - dIib));
                    ing += ingreso + dIng;
                    renta += reg + iib + dReg + dIib;
                    ff += V(p, a, "flujo_fondos") + dIng - dReg - dIib - dGan;
                }
            escenarios.Add(new Escenario(pct, ing, renta, ff));
        }

        // ---------- operación: año firme por área ----------
        decimal sumPoz = 0, sumEq = 0, sumFrac = 0, invConPozos = 0, pozConInv = 0, opexTot = 0, salProd = 0, mmboeTot = 0, prodCapF = 0, capF = 0;
        int areasCap = 0, sobre = 0, alta = 0;
        foreach (var p in enviadas)
        {
            var poz = V(p, anioFirme, "pozos_perforados"); sumPoz += poz; sumEq += V(p, anioFirme, "equipos_perforacion"); sumFrac += V(p, anioFirme, "fracturas");
            if (poz > 0) { invConPozos += V(p, anioFirme, "inv_total"); pozConInv += poz; }
            var pet = V(p, anioFirme, "prod_petroleo"); var gas = V(p, anioFirme, "prod_gas");
            var mmboe = pet + gas * MMboePorMMm3Gas;
            if (mmboe > 0) { opexTot += V(p, anioFirme, "gto_total"); salProd += V(p, anioFirme, "gto_salarios"); mmboeTot += mmboe; }
            var cap = V(p, anioFirme, "cap_proceso_petroleo");
            if (cap > 0)
            {
                var bpd = pet * 1_000_000m / 365m; areasCap++; prodCapF += bpd; capF += cap;
                if (bpd > cap) sobre++; else if (bpd >= cap * 0.9m) alta++;
            }
        }

        // ---------- calidad y cumplimiento ----------
        var recomendados = AnexosRequeridos.Todos.Where(x => x.Obligatorio).Select(x => x.Codigo).ToList();
        var limite = ciclo.LimiteVigente;
        var enPlazo = 0; var demoras = new List<int>(); var alertas = new List<AlertaPlanilla>(); var conAnexos = 0;
        foreach (var p in enviadas)
        {
            if (p.EnviadaEn is { } env)
            {
                var dias = DateOnly.FromDateTime(env).DayNumber - limite.DayNumber;
                if (dias <= 0) enPlazo++; else demoras.Add(dias);
            }
            if (recomendados.All(r => p.Anexos.Any(x => x.Tipo == r))) conAnexos++;

            p.Ciclo ??= ciclo;
            var obs = Validador.Validar(p, p.Anexos.Select(x => new AnexoTipoPresentado(x.Tipo)).ToList())
                .Where(o => !o.Mensaje.StartsWith("Falta adjuntar", StringComparison.Ordinal)).ToList();
            if (obs.Count > 0)
                alertas.Add(new AlertaPlanilla(p.Id, p.RazonSocial, Area(p), obs.Count, (obs[0].Anio is { } an ? an + ": " : "") + obs[0].Mensaje));
        }
        var cumplimiento = new Cumplimiento
        {
            Enviadas = enviadas.Count, EnPlazo = enPlazo, FueraDePlazo = demoras.Count,
            DemoraPromedioDias = demoras.Count > 0 ? (decimal)demoras.Average() : 0, DemoraMaximaDias = demoras.Count > 0 ? demoras.Max() : 0,
            Rectificatorias = todasLasVersiones.Count(p => p.Version > 1), ConAnexosRecomendadosCompletos = conAnexos,
            ConAdvertenciasDeDatos = alertas.Count,
        };

        // ---------- atípicos entre pares (mismo tipo de plan) ----------
        var atipicos = new List<Atipico>();
        foreach (var grupo in enviadas.GroupBy(p => p.TipoPlan))
        {
            Agregar(grupo.ToList(), "Inversión por pozo perforado", "MMUSD/pozo",
                p => V(p, anioFirme, "pozos_perforados") > 0 ? V(p, anioFirme, "inv_total") / V(p, anioFirme, "pozos_perforados") : null);
            Agregar(grupo.ToList(), "Gastos operativos por boe", "USD/boe",
                p => { var mm = V(p, anioFirme, "prod_petroleo") + V(p, anioFirme, "prod_gas") * MMboePorMMm3Gas; return mm > 0 ? V(p, anioFirme, "gto_total") / mm : null; });
        }
        void Agregar(List<Presentacion> grupo, string metrica, string unidad, Func<Presentacion, decimal?> f)
        {
            var valores = grupo.Select(p => (p, v: f(p))).Where(x => x.v is > 0).Select(x => (x.p, v: x.v!.Value)).ToList();
            if (valores.Count < 5) return;                        // con menos de 5 pares la mediana no dice nada
            var med = Mediana(valores.Select(x => x.v).ToList());
            if (med <= 0) return;
            foreach (var (p, v) in valores)
            {
                var veces = v >= med ? v / med : med / v;
                if (veces >= 2m) atipicos.Add(new Atipico(p.Id, p.RazonSocial, Area(p), metrica, unidad, v, med, veces, v >= med));
            }
        }

        // ---------- cambios entre versiones ----------
        var cambios = new List<CambioVersion>();
        foreach (var g in todasLasVersiones.GroupBy(p => (p.CicloId, p.Cuit, p.IdBloque)))
        {
            var v = g.OrderByDescending(p => p.Version).Take(2).ToList();
            if (v.Count < 2) continue;
            decimal Inv(Presentacion p) => anios.Sum(a => V(p, a, "inv_total"));
            var antes = Inv(v[1]); var despues = Inv(v[0]);
            var petA = V(v[1], anioFirme, "prod_petroleo"); var petD = V(v[0], anioFirme, "prod_petroleo");
            cambios.Add(new CambioVersion(v[0].Id, v[0].RazonSocial, Area(v[0]), v[0].Version, antes, despues,
                antes > 0 ? (despues - antes) / antes * 100 : null, petA > 0 ? (petD - petA) / petA * 100 : null));
        }

        // ---------- mapa de inversión empresa × año ----------
        var filas = enviadas.GroupBy(p => p.Cuit).Select(g => new MapaFila(g.First().RazonSocial, anios.Select(a => g.Sum(p => V(p, a, "inv_total"))).ToList()))
            .OrderByDescending(f => f.Total).ToList();
        var mapa = filas.Take(12).ToList();
        if (filas.Count > 12)
            mapa.Add(new MapaFila($"Otras ({filas.Count - 12})", anios.Select((_, i) => filas.Skip(12).Sum(f => f.Valores[i])).ToList()));

        return new IndicadoresTablero
        {
            Anios = serie,
            IngresosHorizonte = ingH, RentaFiscalHorizonte = rentaH, InversionHorizonte = invH, FlujoFondosHorizonte = ffH,
            RentaFiscalPct = ingH > 0 ? rentaH / ingH * 100 : null,
            CargaFiscalPct = ingH > 0 ? (rentaH + ganH) / ingH * 100 : null,
            ReinversionPct = ingH > 0 ? invH / ingH * 100 : null,
            PaybackEstado = estado, PaybackAnio = anioPayback,
            Top3Pct = top3, Top5Pct = top5, Hhi = hhi,
            Mix = [Mezcla("Inversión", "inv_total"), Mezcla("Producción de petróleo", "prod_petroleo"), Mezcla("Ingresos", "ingresos")],
            Sensibilidad = escenarios,

            SinConectarFirme = firme.SinConectarAcumulado,
            PozosPorEquipo = sumEq > 0 ? sumPoz / sumEq : null,
            EtapasPorPozo = sumPoz > 0 ? sumFrac / sumPoz : null,
            PetroleoKbbldFirme = firme.PetroleoKbbld, GasMMm3dFirme = firme.GasMMm3d, GorFirme = firme.Gor, WorFirme = firme.Wor,
            UtilizacionFirmePct = capF > 0 ? prodCapF / capF * 100 : null,
            AreasConCapacidad = areasCap, AreasSobreCapacidad = sobre, AreasUtilizacionAlta = alta,
            CapexPorPozoFirme = pozConInv > 0 ? invConPozos / pozConInv : null,
            OpexPorBoeFirme = mmboeTot > 0 ? opexTot / mmboeTot : null,
            SalariosFirme = S(anioFirme, "gto_salarios"),
            SalariosPctGastosFirme = firme.Opex > 0 ? S(anioFirme, "gto_salarios") / firme.Opex * 100 : null,
            SalariosPorBoeFirme = mmboeTot > 0 ? salProd / mmboeTot : null,
            AbandonoPozosHorizonte = serie.Sum(x => x.PozosAbandonar), AbandonoUsdHorizonte = serie.Sum(x => x.AbandonoUsd),
            AbandonoPctInversion = invH > 0 ? serie.Sum(x => x.AbandonoUsd) / invH * 100 : null,

            Cumplimiento = cumplimiento,
            Alertas = alertas.OrderByDescending(x => x.Cantidad).ThenBy(x => x.Empresa).Take(10).ToList(),
            Atipicos = atipicos.OrderByDescending(x => x.Veces).Take(10).ToList(),
            Cambios = cambios.OrderByDescending(c => Math.Abs(c.InversionDeltaPct ?? 0)).Take(10).ToList(),
            MapaInversion = mapa,
        };
    }

    static string Area(Presentacion p) => p.NombreArea == p.NombreBloque ? p.NombreArea : $"{p.NombreArea} - {p.NombreBloque}";

    static decimal Mediana(List<decimal> v)
    {
        v.Sort();
        var n = v.Count;
        return n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2;
    }
}
