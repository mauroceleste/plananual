using ClosedXML.Excel;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

public class ExportadorExcelTests
{
    static Presentacion P(TipoPlan tipo, params (int Anio, string Campo, decimal V)[] v) => new()
    {
        Id = 5, Cuit = "111", RazonSocial = "Alfa S.A.", NombreArea = "Área Alfa", NombreBloque = "Área Alfa", TipoPlan = tipo,
        AnioDesde = 2026, AnioHasta = 2028, Version = 1, Estado = EstadoPresentacion.Borrador, Ciclo = new Ciclo { Anio = 2026, Nombre = "Plan 2026" },
        Valores = v.Select(x => new PlanValor { PresentacionId = 5, Anio = x.Anio, Campo = x.Campo, Valor = x.V }).ToList()
    };

    static XLWorkbook Abrir(byte[] b) => new(new MemoryStream(b));

    [Theory]
    [InlineData(TipoPlan.Cench, 34)]
    [InlineData(TipoPlan.Convencional, 51)]
    public void Tiene_las_mismas_columnas_que_la_planilla_oficial(TipoPlan tipo, int esperadas)
    {
        Assert.Equal(esperadas, ExportadorExcel.Columnas(tipo).Count);
        using var wb = Abrir(ExportadorExcel.Planilla(P(tipo, (2026, "pozos_perforados", 1))));
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
    public void Escribe_valores_calculados_una_fila_por_anio_y_el_total()
    {
        var p = P(TipoPlan.Convencional, (2026, "inv_perforacion", 3), (2026, "inv_terminacion", 2), (2027, "inv_perforacion", 4), (2026, "pozos_perforados", 4));
        using var wb = Abrir(ExportadorExcel.Planilla(p));
        var ws = wb.Worksheet("Plan de Desarrollo ");
        var cols = ExportadorExcel.Columnas(TipoPlan.Convencional).Select(c => c.Codigo).ToList();
        int Col(string cod) => 3 + cols.IndexOf(cod);

        Assert.Equal(2026, ws.Cell(5, 2).GetValue<int>());
        Assert.Equal(2028, ws.Cell(7, 2).GetValue<int>());
        Assert.Equal("TOTAL", ws.Cell(8, 2).GetString());
        Assert.Equal(5m, ws.Cell(5, Col("inv_desarrollo")).GetValue<decimal>());     // calculado
        Assert.Equal(5m, ws.Cell(5, Col("inv_total")).GetValue<decimal>());
        Assert.Equal(4m, ws.Cell(6, Col("inv_total")).GetValue<decimal>());
        Assert.True(ws.Cell(7, Col("inv_total")).IsEmpty());                         // año sin ningún dato: fila vacía, no ceros inventados
        Assert.True(ws.Cell(7, Col("pozos_perforados")).IsEmpty());                  // lo no cargado queda vacío, no en cero
        Assert.Equal(9.0, ws.Cell(8, Col("inv_total")).Value.GetNumber(), 6);        // 5 + 4, evaluado por la fórmula SUM
        Assert.Contains("SUM(", ws.Cell(8, Col("inv_total")).FormulaA1);
    }

    [Fact]
    public void La_hoja_de_presentacion_deja_claro_si_es_un_borrador()
    {
        using var wb = Abrir(ExportadorExcel.Planilla(P(TipoPlan.Cench, (2026, "inv_desarrollo", 1))));
        var info = wb.Worksheet("Presentación");
        Assert.Contains("BORRADOR", info.Cells().Select(c => c.GetString()).First(s => s.Contains("BORRADOR")));
        Assert.Equal("Alfa S.A.", info.Cell(2, 2).GetString());
    }

    [Fact]
    public void El_formato_de_las_celdas_sigue_el_tipo_de_dato()
    {
        using var wb = Abrir(ExportadorExcel.Planilla(P(TipoPlan.Convencional, (2026, "pozos_perforados", 4))));
        var ws = wb.Worksheet("Plan de Desarrollo ");
        var cols = ExportadorExcel.Columnas(TipoPlan.Convencional).Select(c => c.Codigo).ToList();
        string Fmt(string cod) => ws.Cell(5, 3 + cols.IndexOf(cod)).Style.NumberFormat.Format;

        Assert.Equal("#,##0", Fmt("pozos_perforados"));          // entero
        Assert.Equal("#,##0.00", Fmt("precio_gas"));             // 2 decimales
        Assert.Equal("#,##0.00####", Fmt("inv_perforacion"));    // hasta 6 decimales
    }
}
