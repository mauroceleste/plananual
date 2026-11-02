using ClosedXML.Excel;
using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;

namespace PlanAnual.Infrastructure.Servicios;

/// <summary>
/// Arma el Excel de una presentación con el mismo diseño que las planillas oficiales
/// ("Plan de Desarrollo CENCH" / "Plan de Desarrollo Convencionales"): Período en la columna B,
/// grupos en la fila 2, columnas en la fila 3, unidades en la fila 4 y una fila TOTAL al final.
/// </summary>
public static class ExportadorExcel
{
    static readonly string[] AditivosCantidad = ["pozos_perforados", "pozos_conectados", "fracturas", "pozos_abandonar", "act_pulling", "act_workover"];

    // Los últimos cinco campos de flujo son grupos propios en la planilla oficial.
    static readonly Dictionary<string, string> GrupoOficial = new()
    {
        ["ingresos"] = "Ingresos", ["regalias"] = "Regalías", ["iibb_otros"] = "IIBB y Otros", ["iigg"] = "IIGG", ["flujo_fondos"] = "FF",
    };

    /// <summary>Orden de columnas de la planilla oficial (en Convencionales, "Desarrollo" va antes de sus cuatro componentes).</summary>
    public static IReadOnlyList<CampoDef> Columnas(TipoPlan plan)
    {
        var campos = CatalogoCampos.Para(plan).Where(c => c.Codigo != "gto_total").ToList();   // el total de gastos no está en el modelo oficial
        var desarrollo = campos.FirstOrDefault(c => c.Codigo == "inv_desarrollo" && c.EsCalculado);
        if (desarrollo is not null) { campos.Remove(desarrollo); campos.Insert(campos.FindIndex(c => c.Codigo == "inv_perforacion"), desarrollo); }
        return campos;
    }

    static string Grupo(CampoDef c) => GrupoOficial.GetValueOrDefault(c.Codigo) ?? c.Grupo switch
    {
        CatalogoCampos.GProduccion => "Producción y Venta",
        CatalogoCampos.GGastos => "Gastos Operativos",
        _ => c.Grupo
    };

    public static byte[] Planilla(Presentacion p)
    {
        var calc = Calculadora.CalcularPlan(p.TipoPlan, p.Valores);
        var cols = Columnas(p.TipoPlan);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Plan de Desarrollo ");

        ws.Cell(2, 2).Value = "Período";
        ws.Range(2, 2, 4, 2).Merge();
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i]; int col = 3 + i;
            ws.Cell(2, col).Value = Grupo(c);
            ws.Cell(3, col).Value = c.Nombre;
            ws.Cell(4, col).Value = c.Unidad.StartsWith('#') ? "#" : c.Unidad;
        }
        // Une los encabezados de grupo contiguos iguales, como en la planilla oficial.
        for (int i = 0; i < cols.Count;)
        {
            int j = i; while (j + 1 < cols.Count && Grupo(cols[j + 1]) == Grupo(cols[i])) j++;
            if (j > i) ws.Range(2, 3 + i, 2, 3 + j).Merge();
            i = j + 1;
        }

        int fila = 5;
        var anios = Enumerable.Range(p.AnioDesde, p.AnioHasta - p.AnioDesde + 1).ToList();
        foreach (var anio in anios)
        {
            ws.Cell(fila, 2).Value = anio;
            for (int i = 0; i < cols.Count; i++)
                if (calc.GetValueOrDefault(anio) is { } f && f.TryGetValue(cols[i].Codigo, out var v) && (cols[i].EsCalculado || p.Valores.Any(x => x.Anio == anio && x.Campo == cols[i].Codigo && x.Valor.HasValue)))
                    ws.Cell(fila, 3 + i).Value = v;
            fila++;
        }

        ws.Cell(fila, 2).Value = "TOTAL";
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            bool suma = c.Unidad is "MMUSD" or "MMbbl" or "MMm3" || AditivosCantidad.Contains(c.Codigo);
            if (suma) ws.Cell(fila, 3 + i).FormulaA1 = $"SUM({ws.Cell(5, 3 + i).Address}:{ws.Cell(fila - 1, 3 + i).Address})";
        }

        var rango = ws.Range(2, 2, fila, 2 + cols.Count);
        rango.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        rango.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        var cab = ws.Range(2, 2, 4, 2 + cols.Count);
        cab.Style.Font.Bold = true; cab.Style.Fill.BackgroundColor = XLColor.FromHtml("#00374C"); cab.Style.Font.FontColor = XLColor.FromHtml("#FCF2DC");
        cab.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; cab.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center; cab.Style.Alignment.WrapText = true;
        ws.Row(fila).Style.Font.Bold = true;
        for (int i = 0; i < cols.Count; i++)
            ws.Range(5, 3 + i, fila, 3 + i).Style.NumberFormat.Format =
                cols[i].DecimalesMax == 0 ? "#,##0" : cols[i].DecimalesMax == 2 ? "#,##0.00" : "#,##0.00####";
        ws.Column(2).Width = 10; ws.Columns(3, 2 + cols.Count).Width = 16; ws.Row(3).Height = 48;
        ws.SheetView.FreezeRows(4); ws.SheetView.FreezeColumns(2);

        // Datos de la presentación: trazabilidad de lo que se está leyendo.
        var info = wb.Worksheets.Add("Presentación");
        var datos = new (string, string)[]
        {
            ("Convocatoria", p.Ciclo?.Nombre ?? ""), ("Empresa", p.RazonSocial), ("CUIT", p.Cuit),
            ("Área", p.NombreArea == p.NombreBloque ? p.NombreArea : $"{p.NombreArea} - {p.NombreBloque}"),
            ("Tipo de plan", p.TipoPlan == TipoPlan.Cench ? "CENCH (no convencional)" : "Convencional"),
            ("Versión", p.Version.ToString()), ("Estado", p.Estado == EstadoPresentacion.Borrador ? "BORRADOR (no enviada)" : p.Estado.ToString()),
            ("Comprobante", p.NumeroComprobante ?? "—"), ("Enviada", p.EnviadaEn?.ToString("dd/MM/yyyy HH:mm") ?? "—"),
            ("Enviada por", p.EnviadaPor ?? "—"), ("Asistencia de Administración", p.AsistidaPor ?? "No"),
            ("Huella SHA-256", p.HuellaEnvio ?? "—"), ("Años cubiertos", $"{p.AnioDesde} a {p.AnioHasta}"),
            ("Nota", "El precio del petróleo se informa en USD/bbl (el modelo oficial lo rotula USD/MMBtu). Los campos de inversión total, desarrollo y flujo de fondos se calculan."),
        };
        for (int i = 0; i < datos.Length; i++) { info.Cell(i + 1, 1).Value = datos[i].Item1; info.Cell(i + 1, 1).Style.Font.Bold = true; info.Cell(i + 1, 2).Value = datos[i].Item2; }
        info.Column(1).Width = 18; info.Column(2).Width = 90; info.Column(2).Style.Alignment.WrapText = true;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
