using ClosedXML.Excel;
using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

/// <summary>
/// Celdas de carga que traen fórmulas (la empresa pega desde su propio libro): la importación toma el valor que Excel dejó guardado,
/// nunca recalcula, y si la fórmula no tiene valor, tiene un error o apunta a otra hoja u otro libro lo informa en esa celda en vez de romperse.
/// </summary>
public class PlanillaFormulasTests
{
    static readonly IReadOnlyList<CampoDef> Cols = ExportadorExcel.ColumnasUnificadas();
    static int Col(string codigo) => PlanillaExcel.PrimeraColCampo + Cols.ToList().FindIndex(c => c.Codigo == codigo);

    /// <summary>Planilla con un área: Convencional «Sí» con todo en 1 (los doce meses) y No Convencional «No».</summary>
    static XLWorkbook Completa()
    {
        var bytes = PlanillaExcel.Generar(new Ciclo { Anio = 2026, Nombre = "Plan 2026" }, "Alfa", [new(10, "Área Alfa")]);
        var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(PlanillaExcel.NombreHoja);
        ws.Cell(PlanillaExcel.PrimeraFila, PlanillaExcel.ColActividad).Value = "Sí";
        ws.Cell(PlanillaExcel.PrimeraFila + 12 + PlanillaExcel.AniosAnualesPorDefecto, PlanillaExcel.ColActividad).Value = "No";
        for (int r = PlanillaExcel.PrimeraFila; r < PlanillaExcel.PrimeraFila + 12; r++)
            foreach (var c in CatalogoCampos.Para(TipoPlan.Convencional).Where(c => !c.EsCalculado)) ws.Cell(r, Col(c.Codigo)).Value = 1;
        return wb;
    }

    static ResultadoLectura Importar(XLWorkbook wb)
    {
        var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0;
        return PlanillaExcel.Leer(ms, new Dictionary<int, AreaPlantilla> { [10] = new(10, "Área Alfa") }, 2026);
    }

    static int FebreroGas => PlanillaExcel.PrimeraFila + 1;

    [Fact]
    public void Una_formula_con_valor_guardado_valido_se_importa_con_ese_valor()
    {
        var wb = Completa();
        wb.Worksheet(PlanillaExcel.NombreHoja).Cell(FebreroGas, Col("prod_gas")).FormulaA1 = "=2+3";
        var r = Importar(wb);
        Assert.Empty(r.Errores);
        Assert.Equal(5m, r.Areas[0].Planes[0].Valores.Single(v => v.Campo == "prod_gas" && v.Mes == 2).Valor);
    }

    [Theory]
    [InlineData("=Otra!A1")]                      // hoja que no está en el archivo
    [InlineData("='[otro.xlsx]Hoja1'!$A$1")]      // otro libro
    [InlineData("=1/0")]                          // error de cálculo
    public void Una_formula_rota_o_externa_se_informa_en_esa_celda_y_no_rompe_la_importacion(string formula)
    {
        var wb = Completa();
        wb.Worksheet(PlanillaExcel.NombreHoja).Cell(FebreroGas, Col("prod_gas")).FormulaA1 = formula;

        var r = Importar(wb);                                                      // no lanza excepción

        var e = Assert.Single(r.Errores);
        Assert.Equal(FebreroGas, e.Fila);
        Assert.StartsWith("Producción de gas", e.Columna);
        Assert.Contains("fórmula", e.Mensaje);
        Assert.Contains("Pegá solo valores", e.Mensaje);
    }

    [Fact]
    public void Una_formula_rota_en_otras_columnas_fijas_tampoco_rompe()
    {
        var wb = Completa();
        var ws = wb.Worksheet(PlanillaExcel.NombreHoja);
        ws.Cell(FebreroGas, PlanillaExcel.ColTipo).FormulaA1 = "=Otra!A1";         // tipo de actividad con una fórmula huérfana
        ws.Cell(FebreroGas + 1, PlanillaExcel.ColAnio).FormulaA1 = "='[x.xlsx]H'!A1";
        ws.Cell(FebreroGas + 2, PlanillaExcel.ColMes).FormulaA1 = "=Otra!A1";

        var r = Importar(wb);

        Assert.NotEmpty(r.Errores);
        Assert.Contains(r.Errores, e => e.Fila == FebreroGas && e.Columna == "Tipo de Actividad");
    }
}
