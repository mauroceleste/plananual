using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;

namespace PlanAnual.Infrastructure.Servicios;

public sealed record AreaPlantilla(int IdBloque, string Nombre, TipoPlan Tipo);

public sealed record ErrorImportacion(string Hoja, int? Fila, string? Columna, string Mensaje);

public sealed record ValorLeido(int Anio, int Mes, string Campo, decimal Valor);

public sealed class AreaLeida
{
    public required int IdBloque { get; init; }
    public required string Nombre { get; init; }
    public required TipoPlan Tipo { get; init; }
    public List<ValorLeido> Valores { get; } = [];
    public int AnioHasta => Valores.Count == 0 ? 0 : Valores.Max(v => v.Anio);
}

public sealed class ResultadoLectura
{
    public List<ErrorImportacion> Errores { get; } = [];
    public List<AreaLeida> Areas { get; } = [];
    public bool Truncado { get; set; }
}

/// <summary>
/// Planilla Excel de carga para todas las áreas de una empresa, y su lectura con validación. Hoja por tipo de plan
/// ("Plan Convencional" / "Plan CENCH"): una fila por mes del primer año (el de la convocatoria) y una por cada año siguiente.
/// La columna B (código del área) y la fila 1 (código de cada columna) van ocultas: la lectura se guía por ellas, no por los textos.
/// </summary>
public static class PlanillaExcel
{
    public const int ColArea = 1, ColCodigo = 2, ColAnio = 3, ColMes = 4, PrimeraColCampo = 5, PrimeraFila = 5, FilaCodigos = 1;
    const int MaximoErrores = 200;

    public static string NombreHoja(TipoPlan t) => t == TipoPlan.Cench ? "Plan CENCH" : "Plan Convencional";

    // ============================ Generación ============================

    /// <param name="aniosAnuales">Cantidad de años anuales que se ofrecen después del primer año.</param>
    public static byte[] Generar(Ciclo ciclo, string razonSocial, IReadOnlyList<AreaPlantilla> areas, IReadOnlyList<string> areasSinTipo, int aniosAnuales = 4)
    {
        using var wb = new XLWorkbook();
        Instrucciones(wb, ciclo, razonSocial, areas, areasSinTipo);
        foreach (var tipo in new[] { TipoPlan.Convencional, TipoPlan.Cench })
        {
            var deEsteTipo = areas.Where(a => a.Tipo == tipo).OrderBy(a => a.Nombre).ToList();
            if (deEsteTipo.Count > 0) Hoja(wb, tipo, ciclo, deEsteTipo, aniosAnuales);
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    static void Instrucciones(XLWorkbook wb, Ciclo ciclo, string razon, IReadOnlyList<AreaPlantilla> areas, IReadOnlyList<string> sinTipo)
    {
        var ws = wb.Worksheets.Add("Instrucciones");
        var lineas = new List<string>
        {
            $"{ciclo.Nombre} · {razon}",
            "",
            "CÓMO COMPLETAR ESTA PLANILLA",
            $"1. Cada hoja (Plan Convencional / Plan CENCH) tiene una fila por mes del año {ciclo.Anio} (el primer año se informa mes por mes, y es compromiso firme) y una fila por cada año siguiente.",
            "2. Completá las celdas en blanco. NO puede quedar ninguna celda vacía: si no hay actividad o valor, ingresá 0.",
            "3. Las celdas grises se calculan solas (no las completes). No modifiques las columnas A a D, la fila de encabezados ni los códigos ocultos.",
            "4. Para más años, copiá la última fila de un área, pegala debajo y cambiá el año. Los años tienen que ser consecutivos y sin repetir.",
            "5. Decimales con coma (por ejemplo 12,5). No uses letras, signos ni unidades dentro de las celdas.",
            "6. Guardá el archivo como .xlsx y subilo en Mis áreas → Importar planilla. Si hay errores, el sistema te dice en qué hoja, fila y columna, y no importa nada hasta que estén corregidos.",
            "7. Lo importado queda como borrador: podés revisarlo y completarlo en el formulario antes de la presentación final. Importar reemplaza lo que ya cargaste en las áreas incluidas en el archivo.",
            "",
            "UNIDADES DE MEDIDA",
        };
        foreach (var u in new[] { "MMUSD", "#", "MMm3", "MMbbl", "bbl/d", "MMm3/d", "USD/bbl", "USD/MMBtu", "%" })
            lineas.Add($"{u}: {CatalogoCampos.DescripcionUnidad(u)}");
        lineas.Add("");
        lineas.Add("CÓMO SE PASA DE MENSUAL A ANUAL (el sistema lo hace solo)");
        lineas.Add("Se suman: inversiones, gastos, producción, ventas y cantidades de pozos, fracturas, pulling y workover.");
        lineas.Add("Se promedian: precios y capacidades diarias. Máximo del año: equipos de perforación y set de fracturas. Último mes: desarrollo de recursos (acumulado).");
        lineas.Add("");
        lineas.Add("ÁREAS INCLUIDAS EN ESTE ARCHIVO");
        foreach (var a in areas.OrderBy(a => a.Nombre)) lineas.Add($"{a.Nombre} · {(a.Tipo == TipoPlan.Cench ? "CENCH" : "Convencional")}");
        if (sinTipo.Count > 0)
        {
            lineas.Add("");
            lineas.Add("ÁREAS SIN INCLUIR: la Subsecretaría todavía no definió su tipo de actividad (CENCH o convencional)");
            lineas.AddRange(sinTipo.Select(n => n));
        }
        for (int i = 0; i < lineas.Count; i++) ws.Cell(i + 1, 1).Value = lineas[i];
        ws.Column(1).Width = 150; ws.Column(1).Style.Alignment.WrapText = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        for (int i = 1; i <= lineas.Count; i++) if (lineas[i - 1] is "CÓMO COMPLETAR ESTA PLANILLA" or "UNIDADES DE MEDIDA" or "ÁREAS INCLUIDAS EN ESTE ARCHIVO" || lineas[i - 1].StartsWith("CÓMO SE PASA") || lineas[i - 1].StartsWith("ÁREAS SIN INCLUIR")) ws.Cell(i, 1).Style.Font.Bold = true;
    }

    static void Hoja(XLWorkbook wb, TipoPlan tipo, Ciclo ciclo, IReadOnlyList<AreaPlantilla> areas, int aniosAnuales)
    {
        var ws = wb.Worksheets.Add(NombreHoja(tipo));
        var cols = ExportadorExcel.Columnas(tipo);
        int Col(int i) => PrimeraColCampo + i;
        int ultimaCol = PrimeraColCampo + cols.Count - 1;

        // Fila 1 (oculta): códigos que guían la importación.
        ws.Cell(FilaCodigos, ColArea).Value = "area"; ws.Cell(FilaCodigos, ColCodigo).Value = "codigo_area"; ws.Cell(FilaCodigos, ColAnio).Value = "anio"; ws.Cell(FilaCodigos, ColMes).Value = "mes";
        for (int i = 0; i < cols.Count; i++) ws.Cell(FilaCodigos, Col(i)).Value = cols[i].Codigo;

        // Encabezados (filas 2 a 4), como la planilla oficial.
        ws.Cell(2, ColArea).Value = "Área"; ws.Cell(2, ColCodigo).Value = "Código de área"; ws.Cell(2, ColAnio).Value = "Año"; ws.Cell(2, ColMes).Value = "Mes";
        for (int c = ColArea; c <= ColMes; c++) ws.Range(2, c, 4, c).Merge();
        for (int i = 0; i < cols.Count; i++)
        {
            ws.Cell(2, Col(i)).Value = ExportadorExcel.GrupoDe(cols[i]);
            ws.Cell(3, Col(i)).Value = cols[i].Nombre;
            ws.Cell(4, Col(i)).Value = cols[i].Unidad;
        }
        for (int i = 0; i < cols.Count;)
        {
            int j = i; while (j + 1 < cols.Count && ExportadorExcel.GrupoDe(cols[j + 1]) == ExportadorExcel.GrupoDe(cols[i])) j++;
            if (j > i) ws.Range(2, Col(i), 2, Col(j)).Merge();
            i = j + 1;
        }
        var cab = ws.Range(2, 1, 4, ultimaCol);
        cab.Style.Font.Bold = true; cab.Style.Fill.BackgroundColor = XLColor.FromHtml("#00374C"); cab.Style.Font.FontColor = XLColor.FromHtml("#FCF2DC");
        cab.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; cab.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center; cab.Style.Alignment.WrapText = true;

        // Filas: por área, 12 meses del primer año y después un año por fila.
        int fila = PrimeraFila;
        var gris = XLColor.FromHtml("#E9EEF2");
        foreach (var a in areas)
        {
            int inicio = fila;
            var periodos = Enumerable.Range(1, 12).Select(m => (ciclo.Anio, m)).Concat(Enumerable.Range(1, aniosAnuales).Select(k => (ciclo.Anio + k, 0)));
            foreach (var (anio, mes) in periodos)
            {
                ws.Cell(fila, ColArea).Value = a.Nombre;
                ws.Cell(fila, ColCodigo).Value = a.IdBloque;
                ws.Cell(fila, ColAnio).Value = anio;
                if (mes > 0) ws.Cell(fila, ColMes).Value = mes; else ws.Cell(fila, ColMes).Value = "Anual";
                ws.Cell(fila, ColMes).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                for (int i = 0; i < cols.Count; i++)
                    if (cols[i].EsCalculado)
                    {
                        ws.Cell(fila, Col(i)).FormulaA1 = Formula(tipo, cols, cols[i], fila);
                        ws.Cell(fila, Col(i)).Style.Fill.BackgroundColor = gris;
                    }
                fila++;
            }
            ws.Range(inicio, 1, inicio, ultimaCol).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            ws.Range(inicio, ColArea, fila - 1, ColMes).Style.Fill.BackgroundColor = gris;
        }
        int ultima = fila - 1;
        ws.Range(2, 1, ultima, ultimaCol).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range(PrimeraFila, 1, ultima, ultimaCol).Style.Border.InsideBorder = XLBorderStyleValues.Hair;

        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            var rango = ws.Range(PrimeraFila, Col(i), ultima, Col(i));
            rango.Style.NumberFormat.Format = c.DecimalesMax == 0 ? "#,##0" : c.DecimalesMax == 2 ? "#,##0.00" : "#,##0.00####";
            if (c.EsCalculado) continue;
            var dv = rango.CreateDataValidation();
            dv.ShowErrorMessage = true; dv.ErrorTitle = c.Nombre;
            if (c.DecimalesMax == 0) { dv.WholeNumber.Between(0, 1_000_000_000); dv.ErrorMessage = "Ingresá un número entero, 0 o mayor."; }
            else { dv.Decimal.Between(0, (double)(c.Maximo ?? 1_000_000_000m)); dv.ErrorMessage = c.Maximo is { } m ? $"Ingresá un número entre 0 y {m:0.##}." : "Ingresá un número, 0 o mayor."; }
        }

        ws.Column(ColArea).Width = 30; ws.Column(ColAnio).Width = 7; ws.Column(ColMes).Width = 8;
        ws.Columns(PrimeraColCampo, ultimaCol).Width = 16; ws.Row(3).Height = 48;
        ws.Column(ColCodigo).Hide(); ws.Row(FilaCodigos).Hide();
        ws.SheetView.FreezeRows(4); ws.SheetView.FreezeColumns(ColMes);
    }

    /// <summary>Fórmula de un campo calculado: suma de sus componentes menos los que resta (los que no son columna se abren en sus componentes).</summary>
    static string Formula(TipoPlan tipo, IReadOnlyList<CampoDef> cols, CampoDef calc, int fila)
    {
        IEnumerable<string> Refs(IEnumerable<string> codigos)
        {
            foreach (var cod in codigos)
            {
                int i = -1; for (int k = 0; k < cols.Count; k++) if (cols[k].Codigo == cod) { i = k; break; }
                if (i >= 0) { yield return XLHelper.GetColumnLetterFromNumber(PrimeraColCampo + i) + fila; continue; }
                if (CatalogoCampos.Buscar(tipo, cod) is { EsCalculado: true } def)
                    foreach (var r in Refs(def.Suma!)) yield return r;
            }
        }
        var suma = string.Join("+", Refs(calc.Suma!));
        var resta = calc.Resta is null ? "" : string.Join("+", Refs(calc.Resta));
        return $"={(suma == "" ? "0" : suma)}{(resta == "" ? "" : $"-({resta})")}";
    }

    // ============================ Lectura y validación ============================

    /// <param name="areasPermitidas">Las áreas de la empresa (código de Legajos) con su tipo de plan vigente.</param>
    public static ResultadoLectura Leer(Stream archivo, IReadOnlyDictionary<int, AreaPlantilla> areasPermitidas, int anioCiclo)
    {
        var res = new ResultadoLectura();
        XLWorkbook wb;
        try { wb = new XLWorkbook(archivo); }
        catch (Exception) { res.Errores.Add(new("(archivo)", null, null, "No se pudo abrir el archivo. Tiene que ser un Excel (.xlsx) sin contraseña, el que se descarga desde el sistema.")); return res; }

        using (wb)
        {
            var hojas = new[] { TipoPlan.Convencional, TipoPlan.Cench }.Where(t => wb.Worksheets.Any(w => w.Name == NombreHoja(t))).ToList();
            if (hojas.Count == 0)
                res.Errores.Add(new("(archivo)", null, null, $"No encontramos las hojas \"{NombreHoja(TipoPlan.Convencional)}\" ni \"{NombreHoja(TipoPlan.Cench)}\". Usá la planilla que se descarga desde el sistema."));
            foreach (var tipo in hojas) LeerHoja(wb.Worksheet(NombreHoja(tipo)), tipo, areasPermitidas, anioCiclo, res);
        }

        // Una misma área no puede venir en las dos hojas.
        foreach (var g in res.Areas.GroupBy(a => a.IdBloque).Where(g => g.Count() > 1))
            Agregar(res, new("(archivo)", null, null, $"El área {g.First().Nombre} aparece en las dos hojas: tiene un único tipo de plan."));
        if (res.Errores.Count == 0 && res.Areas.Count == 0)
            res.Errores.Add(new("(archivo)", null, null, "El archivo no tiene ninguna fila con datos para importar."));
        return res;
    }

    static void Agregar(ResultadoLectura res, ErrorImportacion e)
    {
        if (res.Errores.Count >= MaximoErrores) { res.Truncado = true; return; }
        res.Errores.Add(e);
    }

    static void LeerHoja(IXLWorksheet ws, TipoPlan tipo, IReadOnlyDictionary<int, AreaPlantilla> permitidas, int anioCiclo, ResultadoLectura res)
    {
        var hoja = ws.Name;
        var esperadas = ExportadorExcel.Columnas(tipo).Where(c => !c.EsCalculado).ToList();
        var ultimaCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var colDe = new Dictionary<string, int>();
        for (int c = PrimeraColCampo; c <= ultimaCol; c++)
            if (ws.Cell(FilaCodigos, c).GetString().Trim() is { Length: > 0 } cod) colDe[cod] = c;
        var faltantes = esperadas.Where(c => !colDe.ContainsKey(c.Codigo)).ToList();
        foreach (var c in faltantes) Agregar(res, new(hoja, FilaCodigos, null, $"Falta la columna \"{c.Nombre}\": no borres ni muevas columnas de la planilla."));
        if (faltantes.Count > 0) return;

        var ultimaFila = ws.LastRowUsed()?.RowNumber() ?? 0;
        var porArea = new Dictionary<int, (AreaLeida Area, Dictionary<(int, int), int> Filas)>();

        for (int r = PrimeraFila; r <= ultimaFila; r++)
        {
            var filaVacia = ws.Cell(r, ColCodigo).IsEmpty() && ws.Cell(r, ColAnio).IsEmpty() && ws.Cell(r, ColMes).IsEmpty() && esperadas.All(c => ws.Cell(r, colDe[c.Codigo]).IsEmpty());
            if (filaVacia) continue;

            if (!EntEro(ws.Cell(r, ColCodigo), out var codigo)) { Agregar(res, new(hoja, r, "Código de área", "Falta el código de área (columna oculta B). No la borres.")); continue; }
            if (!permitidas.TryGetValue(codigo, out var area)) { Agregar(res, new(hoja, r, "Código de área", $"El código {codigo} no corresponde a ninguna de las áreas de tu empresa.")); continue; }
            if (area.Tipo != tipo) { Agregar(res, new(hoja, r, "Área", $"{area.Nombre} es de tipo {(area.Tipo == TipoPlan.Cench ? "CENCH" : "convencional")}: cargala en la hoja \"{NombreHoja(area.Tipo)}\".")); continue; }

            // Período
            if (!EntEro(ws.Cell(r, ColAnio), out var anio)) { Agregar(res, new(hoja, r, "Año", $"{area.Nombre}: el año no es un número.")); continue; }
            int mes;
            var mesTxt = ws.Cell(r, ColMes).GetString().Trim();
            if (mesTxt == "" || mesTxt.Equals("Anual", StringComparison.OrdinalIgnoreCase)) mes = 0;
            else if (!EntEro(ws.Cell(r, ColMes), out mes) || mes is < 1 or > 12) { Agregar(res, new(hoja, r, "Mes", $"{area.Nombre} {anio}: el mes tiene que ser un número de 1 a 12.")); continue; }
            if (anio < anioCiclo) { Agregar(res, new(hoja, r, "Año", $"{area.Nombre}: el plan empieza en {anioCiclo}; {anio} no corresponde.")); continue; }
            if (anio == anioCiclo && mes == 0) { Agregar(res, new(hoja, r, "Mes", $"{area.Nombre}: el año {anio} se informa mes por mes (1 a 12).")); continue; }
            if (anio > anioCiclo && mes != 0) { Agregar(res, new(hoja, r, "Mes", $"{area.Nombre}: a partir de {anioCiclo + 1} los valores son anuales: dejá el mes en blanco o \"Anual\".")); continue; }

            if (!porArea.TryGetValue(codigo, out var entrada))
                porArea[codigo] = entrada = (new AreaLeida { IdBloque = codigo, Nombre = area.Nombre, Tipo = tipo }, []);
            if (entrada.Filas.TryGetValue((anio, mes), out var antes)) { Agregar(res, new(hoja, r, "Año", $"{area.Nombre}: {Periodo(anio, mes)} está repetido (ya estaba en la fila {antes}).")); continue; }
            entrada.Filas[(anio, mes)] = r;

            // Valores: ninguna celda vacía (si no hay valor, 0)
            foreach (var c in esperadas)
            {
                var celda = ws.Cell(r, colDe[c.Codigo]);
                var donde = $"{area.Nombre} {Periodo(anio, mes)}";
                if (celda.IsEmpty() || celda.GetString().Trim() == "") { Agregar(res, new(hoja, r, c.Nombre, $"{donde}: celda vacía. Si no hay valor, ingresá 0.")); continue; }
                if (!Numero(celda, out var valor)) { Agregar(res, new(hoja, r, c.Nombre, $"{donde}: \"{celda.GetString().Trim()}\" no es un número. Usá solo números (decimales con coma).")); continue; }
                if (ReglasDeDatos.Motivo(c, valor) is { } motivo) { Agregar(res, new(hoja, r, c.Nombre, $"{donde}: {motivo}")); continue; }
                entrada.Area.Valores.Add(new ValorLeido(anio, mes, c.Codigo, valor));
            }
        }

        // Integridad por área: 12 meses del primer año y años siguientes consecutivos.
        foreach (var (codigo, (area, filas)) in porArea)
        {
            var faltan = Enumerable.Range(1, 12).Where(m => !filas.ContainsKey((anioCiclo, m))).ToList();
            if (faltan.Count > 0)
                Agregar(res, new(hoja, null, null, $"{area.Nombre}: faltan los meses de {anioCiclo}: {string.Join(", ", faltan.Select(m => Validador.Meses[m - 1]))}."));
            var anuales = filas.Keys.Where(k => k.Item1 > anioCiclo).Select(k => k.Item1).OrderBy(a => a).ToList();
            if (anuales.Count > 0)
            {
                var huecos = Enumerable.Range(anioCiclo + 1, anuales.Max() - anioCiclo).Where(a => !anuales.Contains(a)).ToList();
                if (huecos.Count > 0) Agregar(res, new(hoja, null, null, $"{area.Nombre}: faltan los años {string.Join(", ", huecos)}. Los años tienen que ser consecutivos hasta el último que informes."));
            }
            res.Areas.Add(area);
        }
    }

    static string Periodo(int anio, int mes) => mes > 0 ? $"{Validador.Meses[mes - 1]} {anio}" : anio.ToString();

    static bool EntEro(IXLCell c, out int valor)
    {
        valor = 0;
        if (c.IsEmpty()) return false;
        if (c.DataType == XLDataType.Number) { var d = c.GetDouble(); valor = (int)d; return d == Math.Floor(d) && d is > 0 and < 1_000_000_000; }
        return int.TryParse(c.GetString().Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out valor) && valor > 0;
    }

    /// <summary>Número de una celda: numérico de Excel, o texto en convención es-AR ("1.234,5", "12,5").</summary>
    static bool Numero(IXLCell c, out decimal valor)
    {
        valor = 0;
        if (c.DataType == XLDataType.Number)
        {
            var d = c.GetDouble();
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e12) return false;
            return decimal.TryParse(d.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out valor);
        }
        var t = Regex.Replace(c.GetString(), @"\s", "");
        if (t.Contains(',')) t = t.Replace(".", "").Replace(',', '.');
        else if (Regex.IsMatch(t, @"^[1-9]\d{0,2}(\.\d{3})+$")) t = t.Replace(".", "");
        return Regex.IsMatch(t, @"^\d+(\.\d+)?$") && decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
    }
}
