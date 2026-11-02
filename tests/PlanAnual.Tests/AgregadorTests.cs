using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;

namespace PlanAnual.Tests;

public class AgregadorTests
{
    static Presentacion P(long id, string cuit, string razon, TipoPlan tipo, params (int Anio, string Campo, decimal V)[] v) => new()
    {
        Id = id, Cuit = cuit, RazonSocial = razon, TipoPlan = tipo, IdBloque = (int)id, Estado = EstadoPresentacion.Enviada,
        Valores = v.Select(x => new PlanValor { PresentacionId = id, Anio = x.Anio, Campo = x.Campo, Valor = x.V }).ToList()
    };

    static List<Presentacion> Muestra() =>
    [
        P(1, "1", "Alfa", TipoPlan.Convencional,
            (2026, "inv_perforacion", 3), (2026, "inv_terminacion", 2), (2026, "inv_workover", 1), (2026, "inv_pulling", 0.5m), (2026, "inv_instal_produccion", 1.5m),
            (2027, "inv_perforacion", 4), (2026, "pozos_perforados", 4), (2026, "pozos_abandonar", 1), (2026, "prod_petroleo", 0.8m), (2026, "prod_gas", 12)),
        P(2, "2", "Beta", TipoPlan.Cench,
            (2026, "inv_desarrollo", 100), (2026, "inv_plantas_nuevas", 20), (2027, "inv_desarrollo", 50), (2026, "pozos_perforados", 10), (2026, "prod_petroleo", 5)),
        P(3, "2", "Beta", TipoPlan.Cench, (2026, "inv_desarrollo", 10)),
    ];

    [Fact]
    public void Suma_inversion_por_tipo_y_por_anio()
    {
        var d = Agregador.Calcular(Muestra(), 2026);

        Assert.Equal(8m + 120m + 10m, d.InversionFirme);
        var a26 = d.InversionPorAnio.Single(x => x.Anio == 2026);
        Assert.Equal(8m, a26.Convencional);
        Assert.Equal(130m, a26.Cench);
        Assert.Equal(4m, d.InversionPorAnio.Single(x => x.Anio == 2027).Convencional);
        Assert.Equal(5, d.InversionPorAnio.Count);
    }

    [Fact]
    public void Los_rubros_suman_exactamente_la_inversion_firme()
    {
        var d = Agregador.Calcular(Muestra(), 2026);
        Assert.Equal(d.InversionFirme, d.InversionPorRubro.Sum(r => r.Valor));
        Assert.Equal(d.InversionPorRubro.OrderByDescending(r => r.Valor), d.InversionPorRubro);   // ordenados de mayor a menor
    }

    [Fact]
    public void Agrupa_por_empresa_y_junta_el_resto_en_otras()
    {
        var muchas = Enumerable.Range(1, 10).Select(i => P(i, $"{i}", $"E{i}", TipoPlan.Cench, (2026, "inv_desarrollo", i))).ToList();
        var d = Agregador.Calcular(muchas, 2026);

        Assert.Equal(9, d.InversionPorEmpresa.Count);                    // 8 más "Otras"
        Assert.Equal("E10", d.InversionPorEmpresa[0].Nombre);
        Assert.Equal("Otras (2)", d.InversionPorEmpresa[^1].Nombre);
        Assert.Equal(1m + 2m, d.InversionPorEmpresa[^1].Valor);
        Assert.Equal(55m, d.InversionPorEmpresa.Sum(x => x.Valor));
    }

    [Fact]
    public void Pozos_y_produccion_del_anio_firme()
    {
        var d = Agregador.Calcular(Muestra(), 2026);
        Assert.Equal(14m, d.PozosFirme);
        Assert.Equal(1m, d.AbandonosFirme);
        Assert.Equal(5.8m, d.PetroleoFirme);
        Assert.Equal(12m, d.GasFirme);
    }

    [Fact]
    public void Sin_datos_devuelve_ceros_y_listas_vacias()
    {
        var d = Agregador.Calcular([], 2026);
        Assert.Equal(0m, d.InversionFirme);
        Assert.Empty(d.InversionPorRubro);
        Assert.Empty(d.Empresas);
        Assert.Equal(5, d.Pozos.Count);
    }

    [Theory]
    [InlineData(5, 2030)]
    [InlineData(10, 2035)]
    [InlineData(15, 2040)]
    public void El_horizonte_define_hasta_que_anio_se_muestra(int anios, int ultimo)
    {
        var d = Agregador.Calcular(Muestra(), 2026, anios);
        Assert.Equal(ultimo, d.UltimoAnio);
        Assert.Equal(anios, d.InversionPorAnio.Count);
        Assert.Equal(anios, d.Pozos.Count);
        Assert.Equal(anios, d.Produccion.Count);
        Assert.Equal(ultimo, d.InversionPorAnio[^1].Anio);
    }

    [Fact]
    public void Un_horizonte_mas_largo_suma_mas_inversion_proyectada_sin_cambiar_la_firme()
    {
        var p = P(1, "1", "Alfa", TipoPlan.Cench, (2026, "inv_desarrollo", 10), (2032, "inv_desarrollo", 7), (2040, "inv_desarrollo", 5));
        var corto = Agregador.Calcular([p], 2026, 5);
        var largo = Agregador.Calcular([p], 2026, 15);
        Assert.Equal(10m, corto.InversionPeriodo);
        Assert.Equal(17m, Agregador.Calcular([p], 2026, 10).InversionPeriodo);      // entra 2032
        Assert.Equal(22m, largo.InversionPeriodo);                                  // entra 2040
        Assert.Equal(corto.InversionFirme, largo.InversionFirme);
    }
}
