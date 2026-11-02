using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;

namespace PlanAnual.Tests;

public class TiposDeDatosTests
{
    static CampoDef C(string cod, TipoPlan plan = TipoPlan.Convencional) => CatalogoCampos.Buscar(plan, cod)!;

    [Theory]
    [InlineData("pozos_perforados")]
    [InlineData("pozos_conectados")]
    [InlineData("fracturas")]
    [InlineData("pozos_abandonar")]
    [InlineData("act_pulling")]
    [InlineData("act_workover")]
    [InlineData("cap_proceso_petroleo")]
    [InlineData("cap_evac_petroleo")]
    public void Estos_campos_son_enteros(string codigo) => Assert.Equal(0, C(codigo).DecimalesMax);

    [Theory]
    [InlineData("equipos_perforacion")]
    [InlineData("set_fracturas")]
    [InlineData("desarrollo_recursos")]
    [InlineData("precio_gas")]
    [InlineData("precio_petroleo")]
    public void Estos_campos_admiten_dos_decimales(string codigo) => Assert.Equal(2, C(codigo).DecimalesMax);

    [Theory]
    [InlineData("inv_perforacion")]
    [InlineData("inv_ductos_gas")]
    [InlineData("prod_petroleo")]
    [InlineData("prod_gas")]
    [InlineData("prod_agua")]
    [InlineData("cap_proceso_gas")]
    [InlineData("gto_salarios")]
    [InlineData("regalias")]
    public void Montos_y_volumenes_admiten_los_seis_decimales_que_guarda_la_base(string codigo) => Assert.Equal(6, C(codigo).DecimalesMax);

    [Fact]
    public void Ningun_campo_editable_admite_mas_decimales_que_la_base()
    {
        foreach (var plan in new[] { TipoPlan.Cench, TipoPlan.Convencional })
            Assert.All(CatalogoCampos.Para(plan).Where(c => !c.EsCalculado), c => Assert.InRange(c.DecimalesMax, 0, 6));
    }

    [Fact]
    public void Las_cantidades_de_pozos_nunca_son_decimales()
    {
        foreach (var plan in new[] { TipoPlan.Cench, TipoPlan.Convencional })
            // Las cantidades de pozos, fracturas e intervenciones son enteras. Equipos y sets se declaran como promedio del año
            // (en las planillas reales de 20 de 42 archivos vienen fraccionarios), por eso admiten 2 decimales.
            Assert.All(CatalogoCampos.Para(plan).Where(c => c.Unidad == "#" && c.Codigo is not ("equipos_perforacion" or "set_fracturas")),
                c => Assert.Equal(0, c.DecimalesMax));
    }

    [Theory]
    [InlineData("pozos_perforados", "4", true)]
    [InlineData("pozos_perforados", "0", true)]
    [InlineData("pozos_perforados", "2.5", false)]       // entero
    [InlineData("pozos_perforados", "-1", false)]        // negativo
    [InlineData("cap_proceso_petroleo", "6000", true)]
    [InlineData("cap_proceso_petroleo", "6000.5", false)]
    [InlineData("equipos_perforacion", "2", true)]
    [InlineData("equipos_perforacion", "1.14", true)]
    [InlineData("equipos_perforacion", "1.145", false)]  // más de 2 decimales
    [InlineData("set_fracturas", "0.28", true)]
    [InlineData("set_fracturas", "0.286", false)]
    [InlineData("desarrollo_recursos", "100", true)]
    [InlineData("desarrollo_recursos", "100.01", false)] // supera el máximo
    [InlineData("desarrollo_recursos", "45.25", true)]
    [InlineData("precio_gas", "3.45", true)]
    [InlineData("precio_gas", "3.455", false)]
    [InlineData("inv_perforacion", "3.123457", true)]
    [InlineData("inv_perforacion", "3.1234567", false)]  // 7 decimales: la base los redondearía en silencio
    [InlineData("prod_gas", "12.345678", true)]
    public void La_regla_acepta_o_rechaza_cada_valor(string codigo, string valor, bool valido)
    {
        var motivo = ReglasDeDatos.Motivo(C(codigo), decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(valido, motivo is null);
    }

    [Fact]
    public void El_motivo_nombra_el_campo_y_explica_en_castellano()
    {
        Assert.Equal("Pozos productores perforados: debe ser un número entero (sin decimales).", ReglasDeDatos.Motivo(C("pozos_perforados"), 2.5m));
        Assert.Equal("Equipos de perforación (máximo del año): admite hasta 2 decimales.", ReglasDeDatos.Motivo(C("equipos_perforacion"), 1.145m));
        Assert.Equal("Gas: admite hasta 2 decimales.", ReglasDeDatos.Motivo(C("precio_gas"), 3.455m));
        Assert.Contains("no puede superar 100", ReglasDeDatos.Motivo(C("desarrollo_recursos"), 101m));
    }

    [Fact]
    public void El_encabezado_describe_el_tipo()
    {
        Assert.Equal("entero", ReglasDeDatos.Descripcion(C("pozos_perforados")));
        Assert.Equal("hasta 2 dec.", ReglasDeDatos.Descripcion(C("precio_gas")));
        Assert.Equal("decimales", ReglasDeDatos.Descripcion(C("inv_perforacion")));
    }
}
