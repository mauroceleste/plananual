using ClosedXML.Excel;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

public class PlanillasModeloTests
{
    static XLWorkbook Abrir(string codigo) => new(new MemoryStream(PlanillasModelo.Generar(codigo, 2027, "Alfa S.A.", "Área Alfa")));

    [Fact]
    public void Cada_anexo_con_planilla_apunta_a_la_suya_y_los_demas_no_tienen()
    {
        Assert.Equal(PlanillasModelo.Instalaciones, PlanillasModelo.DeAnexo("flujograma"));
        Assert.Equal(PlanillasModelo.Paradas, PlanillasModelo.DeAnexo("paradas_planta"));
        Assert.Null(PlanillasModelo.DeAnexo("presentacion_plan"));
        Assert.True(PlanillasModelo.Existe("bloque4"));
        Assert.False(PlanillasModelo.Existe("../etc"));
    }

    [Fact]
    public void Instalaciones_trae_el_area_el_anio_y_las_capacidades()
    {
        using var wb = Abrir(PlanillasModelo.Instalaciones);
        var ws = wb.Worksheet("Instalaciones");
        Assert.Contains("Área Alfa", ws.Cell(1, 1).GetString());
        Assert.Contains("2027", ws.Cell(2, 1).GetString());
        var titulos = Enumerable.Range(1, 8).Select(c => ws.Cell(5, c).GetString()).ToList();
        Assert.Contains(titulos, t => t.Contains("tratamiento de petróleo"));
        Assert.Contains(titulos, t => t.Contains("almacenamiento"));
        Assert.Contains(titulos, t => t.Contains("medición fiscal"));
    }

    [Fact]
    public void Paradas_calcula_la_duracion_en_dias_inclusive()
    {
        using var wb = Abrir(PlanillasModelo.Paradas);
        var ws = wb.Worksheet("Paradas de planta");
        Assert.Contains("2027", ws.Cell(1, 1).GetString());
        ws.Cell(6, 2).Value = new DateTime(2027, 3, 10);
        ws.Cell(6, 3).Value = new DateTime(2027, 3, 14);
        Assert.Equal(5, ws.Cell(6, 4).Value.GetNumber());
        Assert.Equal("", ws.Cell(7, 4).Value.ToString());   // sin fechas, sin duración
    }

    [Fact]
    public void Bloque4_tiene_las_hojas_de_conflictos_y_equipos()
    {
        using var wb = Abrir(PlanillasModelo.Bloque4);
        Assert.NotNull(wb.Worksheet("Conflictos superficiarios"));
        var eq = wb.Worksheet("Equipos");
        Assert.Equal("Plazo (meses)", eq.Cell(5, 5).GetString());
        Assert.Equal("Costo (USD/día)", eq.Cell(5, 6).GetString());
    }
}
