using ClosedXML.Excel;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

public class ExportadorExcelTests
{
    /// <summary>El año de la convocatoria (2026) va por mes; los siguientes, anuales (Mes 0).</summary>
    static Presentacion P(TipoPlan tipo, params (int Anio, int Mes, string Campo, decimal V)[] v) => new()
    {
        Id = 5, Cuit = "111", RazonSocial = "Alfa S.A.", NombreArea = "Área Alfa", NombreBloque = "Área Alfa", TipoPlan = tipo,
        AnioDesde = 2026, AnioHasta = 2028, Version = 1, Estado = EstadoPresentacion.Borrador, Ciclo = new Ciclo { Anio = 2026, Nombre = "Plan 2026" },
        Valores = v.Select(x => new PlanValor { PresentacionId = 5, Anio = x.Anio, Mes = x.Mes, Campo = x.Campo, Valor = x.V }).ToList()
    };

    static XLWorkbook Abrir(byte[] b) => new(new MemoryStream(b));

    [Theory]
    [InlineData(TipoPlan.Cench, 34)]
    [InlineData(TipoPlan.Convencional, 51)]
    public void Tiene_las_mismas_columnas_que_la_planilla_oficial(TipoPlan tipo, int esperadas)
    {
        Assert.Equal(esperadas, ExportadorExcel.Columnas(tipo).Count);
        using var wb = Abrir(ExportadorExcel.Planilla(P(tipo, (2026, 1, "pozos_perforados", 1))));
        var ws = wb.Worksheet("Plan de Desarrollo ");
        Assert.Equal("Período", ws.Cell(2, 2).GetString());
        Assert.False(ws.Cell(3, 2 + esperadas).IsEmpty());
        Assert.True(ws.Cell(3, 3 + esperadas).IsEmpty());                 // ni una columna de más
    }

    [Fact]
    public void En_convencionales_desarrollo_va_antes_de_sus_componentes()
    {
        var nombres = ExportadorExcel.Columnas(TipoPlan.Convencional).Select(c => c.Codigo).ToList();
        Assert.True(nombres.IndexOf("inv_desarrollo") < nombres.IndexOf("inv_perforacion"));
        Assert.DoesNotContain("gto_total", nombres);
    }

    [Fact]
    public void El_primer_anio_sale_mes_por_mes_con_su_subtotal_y_el_total_no_cuenta_dos_veces()
    {
        var p = P(TipoPlan.Convencional, (2026, 1, "inv_perforacion", 3), (2026, 1, "inv_terminacion", 2), (2026, 2, "inv_perforacion", 1), (2027, 0, "inv_perforacion", 4), (2026, 1, "pozos_perforados", 4));
        using var wb = Abrir(ExportadorExcel.Planilla(p));
        var ws = wb.Worksheet("Plan de Desarrollo ");
        var cols = ExportadorExcel.Columnas(TipoPlan.Convencional).Select(c => c.Codigo).ToList();
        int Col(string cod) => 3 + cols.IndexOf(cod);

        Assert.Equal("ene-2026", ws.Cell(5, 2).GetString());
        Assert.Equal("dic-2026", ws.Cell(16, 2).GetString());
        Assert.Equal("2026 (total)", ws.Cell(17, 2).GetString());
        Assert.Equal(2027, ws.Cell(18, 2).GetValue<int>());
        Assert.Equal(2028, ws.Cell(19, 2).GetValue<int>());
        Assert.Equal("TOTAL", ws.Cell(20, 2).GetString());

        Assert.Equal(5m, ws.Cell(5, Col("inv_total")).GetValue<decimal>());            // enero, calculado
        Assert.Equal(1m, ws.Cell(6, Col("inv_total")).GetValue<decimal>());            // febrero
        Assert.Equal(6m, ws.Cell(17, Col("inv_total")).GetValue<decimal>());           // subtotal 2026 = suma de los meses
        Assert.Equal(4m, ws.Cell(17, Col("pozos_perforados")).GetValue<decimal>());
        Assert.Equal(4m, ws.Cell(18, Col("inv_total")).GetValue<decimal>());
        Assert.True(ws.Cell(19, Col("inv_total")).IsEmpty());                          // año sin ningún dato: fila vacía, no ceros inventados
        Assert.True(ws.Cell(19, Col("pozos_perforados")).IsEmpty());
        Assert.Equal(10.0, ws.Cell(20, Col("inv_total")).Value.GetNumber(), 6);        // 5 + 1 (meses) + 4 (2027): el subtotal no se cuenta de nuevo
        Assert.Contains("SUM(", ws.Cell(20, Col("inv_total")).FormulaA1);
    }

    [Fact]
    public void El_subtotal_anual_aplica_la_regla_de_cada_campo()
    {
        var p = P(TipoPlan.Convencional, (2026, 1, "precio_gas", 2), (2026, 2, "precio_gas", 4), (2026, 1, "equipos_perforacion", 1), (2026, 2, "equipos_perforacion", 3),
            (2026, 1, "desarrollo_recursos", 10), (2026, 2, "desarrollo_recursos", 12), (2026, 1, "prod_gas", 5), (2026, 2, "prod_gas", 7));
        using var wb = Abrir(ExportadorExcel.Planilla(p));
        var ws = wb.Worksheet("Plan de Desarrollo ");
        var cols = ExportadorExcel.Columnas(TipoPlan.Convencional).Select(c => c.Codigo).ToList();
        decimal Sub(string cod) => ws.Cell(17, 3 + cols.IndexOf(cod)).GetValue<decimal>();

        Assert.Equal(3m, Sub("precio_gas"));            // promedio
        Assert.Equal(3m, Sub("equipos_perforacion"));   // máximo del año
        Assert.Equal(12m, Sub("desarrollo_recursos"));  // último mes con dato (acumulado)
        Assert.Equal(12m, Sub("prod_gas"));             // suma
    }

    [Fact]
    public void La_hoja_de_presentacion_deja_claro_si_es_un_borrador()
    {
        using var wb = Abrir(ExportadorExcel.Planilla(P(TipoPlan.Cench, (2026, 1, "inv_desarrollo", 1))));
        var info = wb.Worksheet("Presentación");
        Assert.Contains("BORRADOR", info.Cells().Select(c => c.GetString()).First(s => s.Contains("BORRADOR")));
        Assert.Equal("Alfa S.A.", info.Cell(2, 2).GetString());
    }

    [Fact]
    public void El_formato_de_las_celdas_sigue_el_tipo_de_dato()
    {
        using var wb = Abrir(ExportadorExcel.Planilla(P(TipoPlan.Convencional, (2026, 1, "pozos_perforados", 4))));
        var ws = wb.Worksheet("Plan de Desarrollo ");
        var cols = ExportadorExcel.Columnas(TipoPlan.Convencional).Select(c => c.Codigo).ToList();
        string Fmt(string cod) => ws.Cell(5, 3 + cols.IndexOf(cod)).Style.NumberFormat.Format;

        Assert.Equal("#,##0", Fmt("pozos_perforados"));          // entero
        Assert.Equal("#,##0.00", Fmt("precio_gas"));             // 2 decimales
        Assert.Equal("#,##0.00####", Fmt("inv_perforacion"));    // hasta 6 decimales
    }
}
