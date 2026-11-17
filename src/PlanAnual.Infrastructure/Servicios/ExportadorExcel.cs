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

    public static string GrupoDe(CampoDef c) => Grupo(c);

    /// <summary>
    /// Columnas de la planilla unificada: todos los campos de los dos planes, cada código una sola vez y en el orden de la planilla oficial.
    /// Qué campos corresponden a cada tipo se resuelve fila por fila con <see cref="CatalogoCampos.Buscar"/>.
    /// </summary>
    public static IReadOnlyList<CampoDef> ColumnasUnificadas() =>
        CatalogoCampos.Todos.Where(c => c.Codigo != "gto_total").GroupBy(c => c.Codigo).Select(g => g.First()).ToList();

    static string Grupo(CampoDef c) => GrupoOficial.GetValueOrDefault(c.Codigo) ?? c.Grupo switch
    {
        CatalogoCampos.GProduccion => "Producción y Venta",
        CatalogoCampos.GGastos => "Gastos Operativos",
        _ => c.Grupo
    };

    /// <summary>Hoja de un plan (una sola actividad: <paramref name="p"/> viene de <see cref="Presentacion.ParaTipo"/>).</summary>
    static void HojaPlan(XLWorkbook wb, Presentacion p, string nombreHoja)
    {
        var periodos = Calculadora.CalcularPeriodos(p.TipoPlan, p.Valores);
        var cols = Columnas(p.TipoPlan);
        var ws = wb.Worksheets.Add(nombreHoja);

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

        // Primer año (el de la convocatoria): un renglón por mes y un subtotal anual; después, un renglón por año.
        int fila = 5;
        var anioMensual = p.Ciclo?.Anio ?? p.AnioDesde;
        var filasDatos = new List<int>();      // filas que se suman en TOTAL (sin el subtotal anual, que repite los meses)
        var filasSubtotal = new List<int>();
        void Escribir(int f, int clave)
        {
            if (!periodos.TryGetValue(clave, out var vals)) return;
            for (int i = 0; i < cols.Count; i++)
                if (vals.TryGetValue(cols[i].Codigo, out var v)) ws.Cell(f, 3 + i).Value = v;
        }
        for (int anio = p.AnioDesde; anio <= p.AnioHasta; anio++)
        {
            if (anio == anioMensual)
            {
                for (int m = 1; m <= 12; m++) { ws.Cell(fila, 2).Value = $"{Validador.Meses[m - 1][..3]}-{anio}"; Escribir(fila, Calculadora.Clave(anio, m)); filasDatos.Add(fila); fila++; }
                ws.Cell(fila, 2).Value = $"{anio} (total)"; Escribir(fila, Calculadora.Clave(anio, 0)); filasSubtotal.Add(fila); fila++;
            }
            else
            {
                ws.Cell(fila, 2).Value = anio; Escribir(fila, Calculadora.Clave(anio, 0));
                filasDatos.Add(fila);
                fila++;
            }
        }
        foreach (var s in filasSubtotal) ws.Row(s).Style.Font.Bold = true;

        var filasSuma = new List<(int Desde, int Hasta)>();
        foreach (var f in filasDatos) { if (filasSuma.Count > 0 && filasSuma[^1].Hasta == f - 1) filasSuma[^1] = (filasSuma[^1].Desde, f); else filasSuma.Add((f, f)); }
        ws.Cell(fila, 2).Value = "TOTAL";
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            bool suma = c.Unidad is "MMUSD" or "MMbbl" or "MMm3" || AditivosCantidad.Contains(c.Codigo);
            if (suma && filasSuma.Count > 0)
                ws.Cell(fila, 3 + i).FormulaA1 = "SUM(" + string.Join(",", filasSuma.Select(r => r.Desde == r.Hasta ? ws.Cell(r.Desde, 3 + i).Address.ToStringRelative() : $"{ws.Cell(r.Desde, 3 + i).Address.ToStringRelative()}:{ws.Cell(r.Hasta, 3 + i).Address.ToStringRelative()}")) + ")";
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
        ws.Column(2).Width = 14; ws.Columns(3, 2 + cols.Count).Width = 16; ws.Row(3).Height = 48;
        ws.SheetView.FreezeRows(4); ws.SheetView.FreezeColumns(2);
    }

    /// <summary>Una hoja por plan de actividad (Convencional y/o No Convencional) más la hoja con los datos de la presentación.</summary>
    public static byte[] Planilla(Presentacion p)
    {
        using var wb = new XLWorkbook();
        var tipos = p.TiposCargados().DefaultIfEmpty(p.TipoPlan).ToList();
        foreach (var t in tipos) HojaPlan(wb, p.ParaTipo(t), tipos.Count == 1 ? "Plan de Desarrollo " : Validador.NombreActividad(t));

        // Datos de la presentación: trazabilidad de lo que se está leyendo.
        var info = wb.Worksheets.Add("Presentación");
        var datos = new (string, string)[]
        {
            ("Convocatoria", p.Ciclo?.Nombre ?? ""), ("Empresa", p.RazonSocial), ("CUIT", p.Cuit),
            ("Área", p.NombreArea == p.NombreBloque ? p.NombreArea : $"{p.NombreArea} - {p.NombreBloque}"),
            ("Tipo de actividad", string.Join(" y ", tipos.Select(t => t == TipoPlan.Cench ? "No Convencional (CENCH)" : "Convencional"))),
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
