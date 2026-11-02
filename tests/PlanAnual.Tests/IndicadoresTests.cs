using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;

namespace PlanAnual.Tests;

public class IndicadoresTests
{
    static readonly Ciclo Ciclo2026 = new() { Id = 1, Anio = 2026, Nombre = "Plan 2026", FechaLimite = new(2026, 2, 20), FechaProrroga = new(2026, 3, 6) };

    static Presentacion P(long id, string cuit, string razon, TipoPlan tipo, DateTime? enviada, int version = 1,
        EstadoPresentacion estado = EstadoPresentacion.Enviada, params (int Anio, string Campo, decimal V)[] v) => new()
    {
        Id = id, CicloId = 1, Cuit = cuit, RazonSocial = razon, IdBloque = (int)(id % 100), NombreArea = $"Area {id}", NombreBloque = $"Area {id}",
        TipoPlan = tipo, Version = version, Estado = estado, EnviadaEn = enviada, Ciclo = null,
        Valores = v.Select(x => new PlanValor { PresentacionId = id, Anio = x.Anio, Campo = x.Campo, Valor = x.V }).ToList()
    };

    static (int, string, decimal) C(string campo, decimal valor, int anio = 2026) => (anio, campo, valor);

    // Dos empresas, un área convencional (Alfa) y una CENCH (Beta). Las cuentas están hechas a mano en cada prueba.
    static (Presentacion Alfa, Presentacion Beta) Base() => (
        P(1, "1", "Alfa", TipoPlan.Convencional, new(2026, 3, 5), 1, EstadoPresentacion.Enviada,
            C("inv_perforacion", 3), C("inv_terminacion", 2), C("pozos_perforados", 4), C("pozos_conectados", 3), C("equipos_perforacion", 2),
            C("prod_petroleo", 1), C("prod_gas", 100), C("prod_agua", 0.5m), C("cap_proceso_petroleo", 3000),
            C("venta_petroleo", 0.98m), C("venta_gas", 90), C("precio_petroleo", 60), C("precio_gas", 3),
            C("ingresos", 70), C("regalias", 8.4m), C("iibb_otros", 2.45m), C("iigg", 5), C("gto_salarios", 2),
            C("pozos_abandonar", 1), C("abandono_usd", 0.5m), C("inv_perforacion", 1, 2027), C("ingresos", 40, 2027)),
        P(2, "2", "Beta", TipoPlan.Cench, new(2026, 3, 10), 1, EstadoPresentacion.Enviada,
            C("inv_desarrollo", 20), C("pozos_perforados", 2), C("pozos_conectados", 2), C("equipos_perforacion", 1), C("fracturas", 90),
            C("prod_petroleo", 2), C("prod_gas", 200), C("prod_agua", 1.5m), C("cap_proceso_petroleo", 4000),
            C("venta_petroleo", 1.95m), C("venta_gas", 180), C("precio_petroleo", 62), C("precio_gas", 3.5m),
            C("ingresos", 150), C("regalias", 18), C("iibb_otros", 5.25m), C("iigg", 10), C("gto_fijo", 3), C("gto_variable", 2)));

    static IndicadoresTablero Calc(params Presentacion[] ps) => Indicadores.Calcular(ps, ps, Ciclo2026, 2026, 5);

    // ---------- Fiscal y economía ----------

    [Fact]
    public void Renta_y_carga_fiscal_y_reinversion_del_anio_firme()
    {
        var (a, b) = Base();
        var f = Calc(a, b).Anios[0];

        Assert.Equal(220m, f.Ingresos);
        Assert.Equal(25m, f.Inversion);                                   // 5 (Alfa) + 20 (Beta)
        Assert.Equal(34.1m, f.RentaFiscal);                               // regalías 26,4 + IIBB 7,7
        Assert.Equal(34.1m / 220m * 100m, f.RentaFiscalPct);
        Assert.Equal((34.1m + 15m) / 220m * 100m, f.CargaFiscalPct);      // más Ganancias (5 + 10)
        Assert.Equal(25m / 220m * 100m, f.ReinversionPct);
    }

    [Fact]
    public void El_flujo_de_fondos_descuenta_el_abandono_solo_en_convencionales()
    {
        var (a, b) = Base();
        var f = Calc(a, b).Anios[0];
        // Alfa: 70 - 5 - 0,5 - 2 - 8,4 - 2,45 - 5 = 46,65 | Beta: 150 - 20 - 5 - 18 - 5,25 - 10 = 91,75
        Assert.Equal(138.4m, f.FlujoFondos);
        Assert.Equal(f.FlujoFondos, f.FlujoAcumulado);
    }

    [Fact]
    public void Los_totales_del_horizonte_suman_todos_los_anios_mostrados()
    {
        var (a, b) = Base();
        var i = Calc(a, b);
        Assert.Equal(260m, i.IngresosHorizonte);                           // 220 + 40 (2027)
        Assert.Equal(26m, i.InversionHorizonte);                           // 25 + 1 (2027)
        Assert.Equal(5, i.Anios.Count);
        var corto = Indicadores.Calcular([a, b], [a, b], Ciclo2026, 2026, 1);
        Assert.Equal(220m, corto.IngresosHorizonte);                       // un solo año: no entra 2027
    }

    [Fact]
    public void Concentracion_top3_top5_y_herfindahl()
    {
        var (a, b) = Base();
        var i = Calc(a, b);
        Assert.Equal(100m, i.Top3Pct);
        Assert.Equal(100m, i.Top5Pct);
        Assert.Equal(6800m, i.Hhi);                                        // 80² + 20²
    }

    [Fact]
    public void Con_una_sola_empresa_la_concentracion_es_total()
    {
        var (a, _) = Base();
        Assert.Equal(10000m, Calc(a).Hhi);
    }

    [Fact]
    public void La_mezcla_reparte_inversion_produccion_e_ingresos_entre_cench_y_convencional()
    {
        var (a, b) = Base();
        var mix = Calc(a, b).Mix.ToDictionary(m => m.Nombre);
        Assert.Equal(6m, mix["Inversión"].Convencional);                   // 5 + 1
        Assert.Equal(20m, mix["Inversión"].Cench);
        Assert.Equal(1m, mix["Producción de petróleo"].Convencional);
        Assert.Equal(2m, mix["Producción de petróleo"].Cench);
        Assert.Equal(2m / 3m * 100m, mix["Producción de petróleo"].PctCench);
    }

    // ---------- Payback ----------

    static IndicadoresTablero Payback(params (int, string, decimal)[] v) =>
        Calc(P(9, "9", "Gamma", TipoPlan.Cench, new(2026, 3, 1), 1, EstadoPresentacion.Enviada, v));

    [Fact]
    public void Payback_es_el_anio_en_que_el_flujo_acumulado_pasa_de_negativo_a_positivo()
    {
        var i = Payback(C("inv_desarrollo", 100), C("ingresos", 60, 2027), C("ingresos", 60, 2028));   // -100, -40, +20
        Assert.Equal("Recupera", i.PaybackEstado);
        Assert.Equal(2028, i.PaybackAnio);
    }

    [Fact]
    public void Si_nunca_hubo_deficit_no_hay_payback()
    {
        var i = Payback(C("ingresos", 60));
        Assert.Equal("SinDeficit", i.PaybackEstado);
        Assert.Null(i.PaybackAnio);
    }

    [Fact]
    public void Si_no_se_recupera_dentro_del_horizonte_lo_dice()
    {
        var i = Payback(C("inv_desarrollo", 500), C("ingresos", 10, 2027));
        Assert.Equal("NoRecupera", i.PaybackEstado);
        Assert.Null(i.PaybackAnio);
    }

    [Fact]
    public void Sin_presentaciones_el_payback_es_sin_datos()
    {
        var i = Indicadores.Calcular([], [], Ciclo2026, 2026, 5);
        Assert.Equal("SinDatos", i.PaybackEstado);
        Assert.Equal(0m, i.IngresosHorizonte);
        Assert.Null(i.CargaFiscalPct);
    }

    // ---------- Sensibilidad ----------

    [Fact]
    public void El_escenario_base_es_igual_a_lo_declarado()
    {
        var (a, b) = Base();
        var i = Calc(a, b);
        var baseE = i.Sensibilidad.Single(e => e.PctPrecioPetroleo == 0);
        Assert.Equal(i.IngresosHorizonte, baseE.Ingresos);
        Assert.Equal(i.RentaFiscalHorizonte, baseE.RentaFiscal);
        Assert.Equal(i.FlujoFondosHorizonte, baseE.FlujoFondos);
    }

    [Fact]
    public void Un_precio_del_petroleo_10_por_ciento_mas_alto_sube_ingresos_y_renta_en_proporcion()
    {
        var (a, b) = Base();
        var s = Calc(a, b).Sensibilidad.ToDictionary(e => e.PctPrecioPetroleo);
        // Δingresos = 10 % de (0,98 × 60 + 1,95 × 62) = 5,88 + 12,09
        Assert.Equal(17.97m, s[10].Ingresos - s[0].Ingresos);
        // regalías e IIBB acompañan al ingreso: 0,7056 + 0,2058 + 1,4508 + 0,42315
        Assert.Equal(2.78535m, s[10].RentaFiscal - s[0].RentaFiscal, 5);
        Assert.True(s[10].FlujoFondos > s[0].FlujoFondos);
        Assert.True(s[-20].FlujoFondos < s[-10].FlujoFondos && s[-10].FlujoFondos < s[0].FlujoFondos);
    }

    [Fact]
    public void Sensibilidad_es_simetrica_en_ingresos()
    {
        var (a, b) = Base();
        var s = Calc(a, b).Sensibilidad.ToDictionary(e => e.PctPrecioPetroleo);
        Assert.Equal(s[20].Ingresos - s[0].Ingresos, s[0].Ingresos - s[-20].Ingresos, 6);
    }

    // ---------- Operación ----------

    [Fact]
    public void Pozos_sin_conectar_productividad_de_equipos_y_etapas_por_pozo()
    {
        var (a, b) = Base();
        var i = Calc(a, b);
        Assert.Equal(1m, i.SinConectarFirme);                              // (4 + 2) perforados - (3 + 2) conectados
        Assert.Equal(2m, i.PozosPorEquipo);                                // 6 pozos / 3 equipos
        Assert.Equal(15m, i.EtapasPorPozo);                                // 90 etapas / 6 pozos
    }

    [Fact]
    public void El_inventario_de_pozos_sin_conectar_se_acumula_anio_a_anio()
    {
        var p = P(9, "9", "Gamma", TipoPlan.Cench, new(2026, 3, 1), 1, EstadoPresentacion.Enviada,
            C("pozos_perforados", 10), C("pozos_conectados", 4), C("pozos_perforados", 5, 2027), C("pozos_conectados", 9, 2027));
        var a = Calc(p).Anios;
        Assert.Equal(6m, a[0].SinConectarAcumulado);
        Assert.Equal(6m + 5m - 9m, a[1].SinConectarAcumulado);
    }

    [Fact]
    public void Produccion_diaria_relacion_gas_petroleo_y_agua_petroleo()
    {
        var (a, b) = Base();
        var f = Calc(a, b).Anios[0];
        Assert.Equal(3m * 1000m / 365m, f.PetroleoKbbld);                  // 3 MMbbl/año
        Assert.Equal(300m / 365m, f.GasMMm3d);
        Assert.Equal(300m / (3m * Indicadores.MMm3PorMMbbl), f.Gor);
        Assert.Equal(2m / (3m * Indicadores.MMm3PorMMbbl), f.Wor);
    }

    [Fact]
    public void Utilizacion_de_capacidad_cuenta_las_areas_al_limite_y_las_que_la_superan()
    {
        var (a, b) = Base();
        var i = Calc(a, b);
        // Alfa: 2.739,7 bbl/d sobre 3.000 (91 %) | Beta: 5.479,5 bbl/d sobre 4.000 (137 %)
        Assert.Equal(2, i.AreasConCapacidad);
        Assert.Equal(1, i.AreasSobreCapacidad);
        Assert.Equal(1, i.AreasUtilizacionAlta);
        Assert.Equal((1_000_000m / 365m + 2_000_000m / 365m) / 7000m * 100m, i.UtilizacionFirmePct);
    }

    [Fact]
    public void Un_area_sin_capacidad_declarada_no_distorsiona_la_utilizacion()
    {
        var p = P(9, "9", "Gamma", TipoPlan.Cench, new(2026, 3, 1), 1, EstadoPresentacion.Enviada, C("prod_petroleo", 5));
        var i = Calc(p);
        Assert.Equal(0, i.AreasConCapacidad);
        Assert.Null(i.UtilizacionFirmePct);
    }

    [Fact]
    public void Costos_unitarios_inversion_por_pozo_y_gastos_por_boe()
    {
        var (a, b) = Base();
        var i = Calc(a, b);
        Assert.Equal(25m / 6m, i.CapexPorPozoFirme);                       // (5 + 20) / (4 + 2)
        Assert.Equal(7m / (1m + 100m * Indicadores.MMboePorMMm3Gas + 2m + 200m * Indicadores.MMboePorMMm3Gas), i.OpexPorBoeFirme);   // gastos 2 + 5
    }

    [Fact]
    public void Los_salarios_se_miden_contra_los_gastos_y_la_produccion()
    {
        var (a, b) = Base();
        var i = Calc(a, b);
        Assert.Equal(2m, i.SalariosFirme);
        Assert.Equal(2m / 7m * 100m, i.SalariosPctGastosFirme);                       // gastos 2 + 5
        Assert.Equal(2m / (1m + 100m * Indicadores.MMboePorMMm3Gas + 2m + 200m * Indicadores.MMboePorMMm3Gas), i.SalariosPorBoeFirme);
    }

    [Fact]
    public void Sin_gastos_los_salarios_no_dan_porcentaje()
    {
        var p = P(9, "9", "Gamma", TipoPlan.Cench, new(2026, 3, 1), 1, EstadoPresentacion.Enviada, C("inv_desarrollo", 10));
        var i = Calc(p);
        Assert.Null(i.SalariosPctGastosFirme);
        Assert.Null(i.SalariosPorBoeFirme);
    }

    [Fact]
    public void Sin_pozos_no_se_inventa_un_costo_por_pozo()
    {
        var p = P(9, "9", "Gamma", TipoPlan.Cench, new(2026, 3, 1), 1, EstadoPresentacion.Enviada, C("inv_desarrollo", 10));
        var i = Calc(p);
        Assert.Null(i.CapexPorPozoFirme);
        Assert.Null(i.PozosPorEquipo);
        Assert.Null(i.OpexPorBoeFirme);
    }

    [Fact]
    public void El_pasivo_de_abandono_suma_pozos_y_dolares_y_su_peso_sobre_la_inversion()
    {
        var (a, b) = Base();
        var i = Calc(a, b);
        Assert.Equal(1m, i.AbandonoPozosHorizonte);
        Assert.Equal(0.5m, i.AbandonoUsdHorizonte);
        Assert.Equal(0.5m / 26m * 100m, i.AbandonoPctInversion);
    }

    // ---------- Calidad y cumplimiento ----------

    [Fact]
    public void Cumplimiento_en_plazo_fuera_de_plazo_y_demora_contra_la_prorroga()
    {
        var (a, b) = Base();                                              // plazo vigente: 6/3/2026 (con prórroga)
        var c = Calc(a, b).Cumplimiento;
        Assert.Equal(2, c.Enviadas);
        Assert.Equal(1, c.EnPlazo);                                       // Alfa, 5/3
        Assert.Equal(1, c.FueraDePlazo);                                  // Beta, 10/3
        Assert.Equal(4m, c.DemoraPromedioDias);
        Assert.Equal(4, c.DemoraMaximaDias);
    }

    [Fact]
    public void El_dia_del_vencimiento_cuenta_como_en_plazo()
    {
        var p = P(9, "9", "Gamma", TipoPlan.Cench, new(2026, 3, 6, 23, 59, 0), 1, EstadoPresentacion.Enviada, C("pozos_perforados", 1));
        Assert.Equal(1, Calc(p).Cumplimiento.EnPlazo);
    }

    [Fact]
    public void Las_rectificatorias_se_cuentan_y_se_comparan_con_la_version_anterior()
    {
        var v1 = P(3, "1", "Alfa", TipoPlan.Convencional, new(2026, 3, 1), 1, EstadoPresentacion.Reemplazada,
            C("inv_perforacion", 4), C("prod_petroleo", 0.8m));
        var (a, b) = Base();
        a.Version = 2;
        a.IdBloque = v1.IdBloque;                                          // misma área
        a.Cuit = v1.Cuit;
        var i = Indicadores.Calcular([a, b], [a, b, v1], Ciclo2026, 2026, 5);

        Assert.Equal(1, i.Cumplimiento.Rectificatorias);
        var c = Assert.Single(i.Cambios);
        Assert.Equal(2, c.VersionNueva);
        Assert.Equal(4m, c.InversionAntes);
        Assert.Equal(6m, c.InversionDespues);                              // 5 en 2026 + 1 en 2027
        Assert.Equal(50m, c.InversionDeltaPct);
        Assert.Equal(25m, c.PetroleoDeltaPct);                             // 0,8 -> 1,0
    }

    [Fact]
    public void Sin_rectificatorias_no_hay_cambios_para_mostrar()
    {
        var (a, b) = Base();
        var i = Calc(a, b);
        Assert.Empty(i.Cambios);
        Assert.Equal(0, i.Cumplimiento.Rectificatorias);
    }

    [Fact]
    public void Los_anexos_recomendados_completos_se_cuentan_por_planilla()
    {
        var (a, b) = Base();
        foreach (var r in AnexosRequeridos.Todos.Where(x => x.Obligatorio))
            a.Anexos.Add(new Anexo { Tipo = r.Codigo });
        Assert.Equal(1, Calc(a, b).Cumplimiento.ConAnexosRecomendadosCompletos);
    }

    [Fact]
    public void Las_alertas_de_datos_no_incluyen_los_anexos_faltantes()
    {
        var p = P(9, "9", "Gamma", TipoPlan.Cench, new(2026, 3, 1), 1, EstadoPresentacion.Enviada,
            C("pozos_perforados", 1), C("prod_petroleo", 1), C("prod_gas", 1), C("venta_petroleo", 2));    // vende más de lo que produce
        var i = Calc(p);
        var alerta = Assert.Single(i.Alertas);
        Assert.Equal("Gamma", alerta.Empresa);
        Assert.Contains("venta de petróleo supera la producción", alerta.Ejemplo);
        Assert.Equal(1, i.Cumplimiento.ConAdvertenciasDeDatos);
    }

    [Fact]
    public void Una_planilla_sana_no_genera_alertas_aunque_falten_anexos()
    {
        var (a, b) = Base();
        Assert.Empty(Calc(a, b).Alertas);
    }

    [Fact]
    public void Un_area_con_inversion_por_pozo_muy_distinta_de_sus_pares_se_marca_como_atipica()
    {
        var normales = Enumerable.Range(10, 6).Select(i => P(i, $"{i}", $"Emp{i}", TipoPlan.Cench, new(2026, 3, 1), 1, EstadoPresentacion.Enviada,
            C("inv_desarrollo", 12), C("pozos_perforados", 1))).ToList();                                 // 12 MMUSD por pozo
        var raro = P(20, "20", "Rara", TipoPlan.Cench, new(2026, 3, 1), 1, EstadoPresentacion.Enviada, C("inv_desarrollo", 60), C("pozos_perforados", 1));
        var i = Calc([.. normales, raro]);

        var at = Assert.Single(i.Atipicos);
        Assert.Equal("Rara", at.Empresa);
        Assert.Equal(60m, at.Valor);
        Assert.Equal(12m, at.Mediana);
        Assert.Equal(5m, at.Veces);
        Assert.True(at.MasAlto);
    }

    [Fact]
    public void Con_pocos_pares_no_se_marcan_atipicos()
    {
        var (a, b) = Base();
        Assert.Empty(Calc(a, b).Atipicos);                                 // dos áreas no alcanzan para hablar de "lo normal"
    }

    [Fact]
    public void El_mapa_de_calor_ordena_las_empresas_por_inversion_y_agrupa_el_resto()
    {
        var ps = Enumerable.Range(1, 14).Select(i => P(i, $"{i}", $"Emp{i:00}", TipoPlan.Cench, new(2026, 3, 1), 1, EstadoPresentacion.Enviada,
            C("inv_desarrollo", i * 10))).ToArray();
        var m = Calc(ps).MapaInversion;

        Assert.Equal(13, m.Count);                                         // 12 + "Otras"
        Assert.Equal("Emp14", m[0].Nombre);
        Assert.Equal(5, m[0].Valores.Count);
        Assert.Equal("Otras (2)", m[^1].Nombre);
        Assert.Equal(10m + 20m, m[^1].Total);                              // las dos más chicas
    }
}
