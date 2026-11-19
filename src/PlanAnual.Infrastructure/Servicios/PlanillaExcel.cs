using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;

namespace PlanAnual.Infrastructure.Servicios;

public sealed record AreaPlantilla(int IdBloque, string Nombre);

public sealed record ErrorImportacion(string Hoja, int? Fila, string? Columna, string Mensaje);

public sealed record ValorLeido(int Anio, int Mes, string Campo, decimal Valor);

/// <summary>El plan de una actividad (Convencional o No Convencional) de un área, tal como vino en el Excel.</summary>
public sealed class PlanLeido
{
    public required TipoPlan Tipo { get; init; }
    /// <summary>La empresa eligió «No» en ¿Tiene actividad?: el sistema carga ceros.</summary>
    public bool SinActividad { get; set; }
    public List<ValorLeido> Valores { get; } = [];
    public int AnioHasta => Valores.Count == 0 ? 0 : Valores.Max(v => v.Anio);
}

public sealed class AreaLeida
{
    public required int IdBloque { get; init; }
    public required string Nombre { get; init; }
    public List<PlanLeido> Planes { get; } = [];
    public int AnioHasta => Planes.Count == 0 ? 0 : Planes.Max(p => p.AnioHasta);
}

public sealed class ResultadoLectura
{
    public List<ErrorImportacion> Errores { get; } = [];
    public List<AreaLeida> Areas { get; } = [];
    public bool Truncado { get; set; }
}

/// <summary>
/// Planilla Excel de carga para todas las áreas de una empresa, y su lectura con validación. Una sola hoja ("Plan"): por cada área, un bloque
/// por tipo de actividad (Convencional y No Convencional), con una fila por mes del primer año (el de la convocatoria) y una por cada año siguiente.
/// El tipo de cada bloque viene fijo; las columnas que no corresponden a ese tipo quedan en gris y bloqueadas; y en la primera fila de cada
/// bloque se elige ¿Tiene actividad? Sí/No (con «No» el sistema carga ceros). La columna B (código del área) y la fila 1 (código de cada
/// columna) van ocultas: la lectura se guía por ellas, no por los textos.
/// </summary>
public static class PlanillaExcel
{
    public const string NombreHoja = "Plan";
    public const int ColArea = 1, ColCodigo = 2, ColAnio = 3, ColMes = 4, ColTipo = 5, ColActividad = 6, PrimeraColCampo = 7, PrimeraFila = 5, FilaCodigos = 1;
    const int MaximoErrores = 200;
    const string CodigoTipo = "tipo_actividad", CodigoActividad = "tiene_actividad";

    /// <summary>Años anuales que trae la planilla después del primer año (cubre concesiones largas; los que no se usan se dejan en blanco).</summary>
    public const int AniosAnualesPorDefecto = 25;

    /// <summary>
    /// Clave de protección de las hojas: evita que se modifiquen por descuido los encabezados, el código de área, el tipo y las fórmulas.
    /// Es un freno, no seguridad: lo que se importa se vuelve a validar completo.
    /// </summary>
    public const string ClaveProteccion = "PAI-Hidrocarburos";

    public static string TextoTipo(TipoPlan t) => t == TipoPlan.Cench ? "No Convencional" : "Convencional";

    static TipoPlan? TipoDeTexto(string? texto)
    {
        var t = (texto ?? "").Trim().ToLowerInvariant();
        if (t is "no convencional" or "noconvencional" or "cench" or "nc" || t.StartsWith("no convenc")) return TipoPlan.Cench;
        if (t is "convencional" or "c") return TipoPlan.Convencional;
        return null;
    }

    // ============================ Generación ============================

    /// <param name="aniosAnuales">Cantidad de años anuales que se ofrecen después del primer año.</param>
    /// <param name="cargadas">Última presentación de cada área (por código de área) para traer lo ya cargado: la planilla sirve para corregir y volver a importar. Null = planilla en blanco.</param>
    public static byte[] Generar(Ciclo ciclo, string razonSocial, IReadOnlyList<AreaPlantilla> areas, int aniosAnuales = AniosAnualesPorDefecto, IReadOnlyDictionary<int, Presentacion>? cargadas = null)
    {
        using var wb = new XLWorkbook();
        Instrucciones(wb, ciclo, razonSocial, areas);
        Hoja(wb, ciclo, areas, aniosAnuales, cargadas);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    static void Instrucciones(XLWorkbook wb, Ciclo ciclo, string razon, IReadOnlyList<AreaPlantilla> areas)
    {
        var ws = wb.Worksheets.Add("Instrucciones");
        var lineas = new List<string>
        {
            $"{ciclo.Nombre} · {razon}",
            "",
            "CÓMO COMPLETAR ESTA PLANILLA",
            $"1. Hay una sola hoja («{NombreHoja}»). Cada área tiene dos bloques: Convencional y No Convencional (CENCH). Cada bloque trae una fila por mes del año {ciclo.Anio} (el primer año se informa mes por mes, y es compromiso firme) y una fila por cada año siguiente.",
            "2. La columna «Tipo de Actividad» viene fija: no se puede modificar. Las columnas en gris oscuro no corresponden a ese tipo de actividad y están bloqueadas.",
            "3. En la PRIMERA fila de cada bloque (enero) elegí en «¿Tiene actividad?» Sí o No. Si tenés actividad de ese tipo, elegí Sí y completá el bloque. Si no la tenés, elegí No, no completes nada más de ese bloque y el sistema lo carga en 0.",
            "4. En los bloques con Sí, completá las celdas en blanco. NO puede quedar ninguna celda vacía: si no hay actividad o valor, ingresá 0.",
            "5. La hoja está protegida: solo se pueden completar las celdas de carga. Los encabezados, el área, el año, el mes, el tipo y las celdas grises (que se calculan solas o no corresponden) están bloqueados.",
            $"6. Completá los años hasta el último de la concesión. La planilla trae hasta {ciclo.Anio + AniosAnualesPorDefecto}: las filas de los años que no uses dejalas totalmente en blanco y no se importan. Los años tienen que ser consecutivos.",
            "7. Decimales con coma (por ejemplo 12,5). No uses letras, signos ni unidades dentro de las celdas.",
            "8. Guardá el archivo como .xlsx y subilo en Mis áreas → Importar planilla. Si hay errores, el sistema te dice en qué fila y columna, y no importa nada hasta que estén corregidos.",
            "9. Lo importado queda como borrador: podés revisarlo y completarlo en el formulario antes de la presentación final. Importar reemplaza lo que ya cargaste en las áreas incluidas en el archivo.",
            "",
            "UNIDADES DE MEDIDA",
        };
        foreach (var u in new[] { "MMUSD", "#", "MMm3", "MMbbl", "bbl/d", "MMm3/d", "USD/bbl", "USD/MMBtu", "%" })
            lineas.Add($"{u}: {CatalogoCampos.DescripcionUnidad(u)}");
        lineas.Add("");
        lineas.Add("CÓMO SE PASA DE MENSUAL A ANUAL (el sistema lo hace solo)");
        lineas.Add("Se suman: inversiones, gastos, producción, ventas y cantidades de pozos, fracturas, pulling y workover.");
        lineas.Add("Se promedian: precios y capacidades diarias. Máximo por periodo: equipos de perforación y set de fracturas. Último mes: desarrollo de recursos (acumulado).");
        lineas.Add("");
        lineas.Add("ÁREAS INCLUIDAS EN ESTE ARCHIVO");
        foreach (var a in areas.OrderBy(a => a.Nombre)) lineas.Add(a.Nombre);
        for (int i = 0; i < lineas.Count; i++) ws.Cell(i + 1, 1).Value = lineas[i];
        ws.Column(1).Width = 150; ws.Column(1).Style.Alignment.WrapText = true;
        ws.Protect(ClaveProteccion, XLProtectionAlgorithm.Algorithm.SHA512, XLSheetProtectionElements.SelectEverything);
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        for (int i = 1; i <= lineas.Count; i++) if (lineas[i - 1] is "CÓMO COMPLETAR ESTA PLANILLA" or "UNIDADES DE MEDIDA" or "ÁREAS INCLUIDAS EN ESTE ARCHIVO" || lineas[i - 1].StartsWith("CÓMO SE PASA")) ws.Cell(i, 1).Style.Font.Bold = true;
    }

    static void Hoja(XLWorkbook wb, Ciclo ciclo, IReadOnlyList<AreaPlantilla> areas, int aniosAnuales, IReadOnlyDictionary<int, Presentacion>? cargadas)
    {
        var ws = wb.Worksheets.Add(NombreHoja);
        var cols = ExportadorExcel.ColumnasUnificadas();
        int Col(int i) => PrimeraColCampo + i;
        int ultimaCol = PrimeraColCampo + cols.Count - 1;

        // Fila 1 (oculta): códigos que guían la importación.
        ws.Cell(FilaCodigos, ColArea).Value = "area"; ws.Cell(FilaCodigos, ColCodigo).Value = "codigo_area"; ws.Cell(FilaCodigos, ColAnio).Value = "anio"; ws.Cell(FilaCodigos, ColMes).Value = "mes";
        ws.Cell(FilaCodigos, ColTipo).Value = CodigoTipo; ws.Cell(FilaCodigos, ColActividad).Value = CodigoActividad;
        for (int i = 0; i < cols.Count; i++) ws.Cell(FilaCodigos, Col(i)).Value = cols[i].Codigo;

        // Encabezados (filas 2 a 4), como la planilla oficial.
        ws.Cell(2, ColArea).Value = "Área"; ws.Cell(2, ColCodigo).Value = "Código de área"; ws.Cell(2, ColAnio).Value = "Año"; ws.Cell(2, ColMes).Value = "Mes";
        ws.Cell(2, ColTipo).Value = "Tipo de Actividad"; ws.Cell(2, ColActividad).Value = "¿Tiene actividad? (Sí / No)";
        for (int c = ColArea; c <= ColActividad; c++) ws.Range(2, c, 4, c).Merge();
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

        // Filas: por área y por tipo de actividad, 12 meses del primer año y después un año por fila.
        int fila = PrimeraFila;
        var gris = XLColor.FromHtml("#E9EEF2");            // se calcula solo
        var noCorresponde = XLColor.FromHtml("#9AA5B1");   // no corresponde a ese tipo de actividad
        var pregunta = XLColor.FromHtml("#FFF2B3");
        var sino = new List<string>();                      // celdas ¿Tiene actividad? de la primera fila de cada bloque
        foreach (var a in areas)
        {
            int inicioArea = fila;
            foreach (var tipo in Presentacion.Actividades)
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
                    ws.Cell(fila, ColTipo).Value = TextoTipo(tipo);
                    for (int i = 0; i < cols.Count; i++)
                        if (CatalogoCampos.Buscar(tipo, cols[i].Codigo) is { EsCalculado: true } calc)
                            ws.Cell(fila, Col(i)).FormulaA1 = Formula(tipo, cols, calc, fila);
                    fila++;
                }
                int fin = fila - 1;
                ws.Range(inicio, 1, fin, ColActividad).Style.Fill.BackgroundColor = gris;     // área, año, mes y tipo: fijos
                ws.Range(inicio, ColTipo, fin, ColTipo).Style.Font.Bold = true;

                // ¿Tiene actividad? solo en la primera fila del bloque.
                var celda = ws.Cell(inicio, ColActividad);
                celda.Style.Fill.BackgroundColor = pregunta; celda.Style.Protection.Locked = false;
                celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; celda.Style.Font.Bold = true;
                sino.Add(celda.Address.ToStringRelative());
                if (cargadas is not null && cargadas.TryGetValue(a.IdBloque, out var presentacion)) RellenarConLoCargado(ws, presentacion, tipo, inicio, fin, cols);

                for (int i = 0; i < cols.Count; i++)
                {
                    var def = CatalogoCampos.Buscar(tipo, cols[i].Codigo);
                    var rango = ws.Range(inicio, Col(i), fin, Col(i));
                    if (def is null) rango.Style.Fill.BackgroundColor = noCorresponde;           // no corresponde a este tipo: bloqueada
                    else if (def.EsCalculado) rango.Style.Fill.BackgroundColor = gris;
                    else rango.Style.Protection.Locked = false;                                    // lo único editable: las celdas de carga
                }
                // Con «No» el bloque se ve apagado: no hay que completarlo.
                var apagar = ws.Range(inicio, PrimeraColCampo, fin, ultimaCol).AddConditionalFormat();
                apagar.WhenIsTrue($"=${XLHelper.GetColumnLetterFromNumber(ColActividad)}${inicio}=\"No\"").Fill.SetBackgroundColor(XLColor.FromHtml("#C9CFD6"));
                ws.Range(inicio, 1, inicio, ultimaCol).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            }
            ws.Range(inicioArea, 1, inicioArea, ultimaCol).Style.Border.TopBorder = XLBorderStyleValues.Medium;
        }
        int ultima = fila - 1;
        ws.Range(2, 1, ultima, ultimaCol).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range(PrimeraFila, 1, ultima, ultimaCol).Style.Border.InsideBorder = XLBorderStyleValues.Hair;

        if (sino.Count > 0)
        {
            var dv = ws.Ranges(string.Join(",", sino)).CreateDataValidation();
            dv.List("\"Sí,No\"", true);
            dv.ShowErrorMessage = true; dv.ErrorTitle = "¿Tiene actividad?"; dv.ErrorMessage = "Elegí Sí o No.";
        }

        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            var rango = ws.Range(PrimeraFila, Col(i), ultima, Col(i));
            rango.Style.NumberFormat.Format = c.DecimalesMax == 0 ? "#,##0" : c.DecimalesMax == 2 ? "#,##0.00" : "#,##0.00####";
            if (c.EsCalculado && c.Codigo != "inv_desarrollo") continue;   // (Desarrollo es de carga en CENCH y se calcula en Convencional: la validación solo aplica a lo editable)
            var dv = rango.CreateDataValidation();
            dv.ShowErrorMessage = true; dv.ErrorTitle = c.Nombre;
            if (c.DecimalesMax == 0) { dv.WholeNumber.Between(0, 1_000_000_000); dv.ErrorMessage = "Ingresá un número entero, 0 o mayor."; }
            else { dv.Decimal.Between(0, (double)(c.Maximo ?? 1_000_000_000m)); dv.ErrorMessage = c.Maximo is { } m ? $"Ingresá un número entre 0 y {m:0.##}." : "Ingresá un número, 0 o mayor."; }
        }

        ws.Column(ColArea).Width = 30; ws.Column(ColAnio).Width = 7; ws.Column(ColMes).Width = 8; ws.Column(ColTipo).Width = 17; ws.Column(ColActividad).Width = 16;
        ws.Columns(PrimeraColCampo, ultimaCol).Width = 16; ws.Row(3).Height = 48;
        ws.Column(ColCodigo).Hide(); ws.Row(FilaCodigos).Hide();
        ws.SheetView.FreezeRows(4); ws.SheetView.FreezeColumns(ColActividad);
        // Protegida y sin permiso de dar formato: no se pueden mostrar la columna y la fila ocultas ni cambiar nada fuera de las celdas de carga.
        ws.Protect(ClaveProteccion, XLProtectionAlgorithm.Algorithm.SHA512, XLSheetProtectionElements.SelectEverything);
    }

    /// <summary>
    /// Completa un bloque con lo que ya está cargado: «Sí» y los valores si el plan tiene datos; «No» si es un plan declarado sin actividad (todo en 0);
    /// sin contestar si ese plan todavía no se cargó. Lo que se baja así se puede corregir y volver a importar tal cual.
    /// </summary>
    static void RellenarConLoCargado(IXLWorksheet ws, Presentacion p, TipoPlan tipo, int inicio, int fin, IReadOnlyList<CampoDef> cols)
    {
        var valores = p.Valores.Where(v => p.ActividadDe(v) == tipo && v.Valor.HasValue).ToList();
        if (valores.Count == 0) return;
        if (valores.All(v => v.Valor == 0)) { ws.Cell(inicio, ColActividad).Value = "No"; return; }
        ws.Cell(inicio, ColActividad).Value = "Sí";
        var porClave = valores.ToDictionary(v => (v.Anio, v.Mes, v.Campo), v => v.Valor!.Value);
        for (int r = inicio; r <= fin; r++)
        {
            var anio = ws.Cell(r, ColAnio).GetValue<int>();
            if (anio > p.AnioHasta) break;
            var mes = ws.Cell(r, ColMes).GetString() == "Anual" ? 0 : ws.Cell(r, ColMes).GetValue<int>();
            for (int i = 0; i < cols.Count; i++)
                if (CatalogoCampos.Buscar(tipo, cols[i].Codigo) is { EsCalculado: false } def && porClave.TryGetValue((anio, mes, def.Codigo), out var v))
                    ws.Cell(r, PrimeraColCampo + i).Value = (double)v;
        }
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

    /// <param name="areasPermitidas">Las áreas de la empresa (código de Legajos).</param>
    public static ResultadoLectura Leer(Stream archivo, IReadOnlyDictionary<int, AreaPlantilla> areasPermitidas, int anioCiclo)
    {
        var res = new ResultadoLectura();
        XLWorkbook wb;
        try { wb = new XLWorkbook(archivo); }
        catch (Exception) { res.Errores.Add(new("(archivo)", null, null, "No se pudo abrir el archivo. Tiene que ser un Excel (.xlsx) sin contraseña, el que se descarga desde el sistema.")); return res; }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault(w => w.Name == NombreHoja);
            if (ws is null)
                res.Errores.Add(new("(archivo)", null, null, $"No encontramos la hoja «{NombreHoja}». Usá la planilla que se descarga desde el sistema (si bajaste una anterior, volvé a bajarla: ahora es una sola hoja para los dos tipos de actividad)."));
            else LeerHoja(ws, areasPermitidas, anioCiclo, res);
        }
        if (res.Errores.Count == 0 && res.Areas.Count == 0)
            res.Errores.Add(new("(archivo)", null, null, "El archivo no tiene ninguna fila con datos para importar."));
        return res;
    }

    static void Agregar(ResultadoLectura res, ErrorImportacion e)
    {
        if (res.Errores.Count >= MaximoErrores) { res.Truncado = true; return; }
        res.Errores.Add(e);
    }

    sealed class Bloque
    {
        public required AreaPlantilla Area { get; init; }
        public required TipoPlan Tipo { get; init; }
        public required PlanLeido Plan { get; init; }
        public Dictionary<(int, int), int> Filas { get; } = [];
        public bool? Tiene { get; set; }
        public int FilaPregunta { get; set; }
    }

    static void LeerHoja(IXLWorksheet ws, IReadOnlyDictionary<int, AreaPlantilla> permitidas, int anioCiclo, ResultadoLectura res)
    {
        var hoja = ws.Name;
        if (Texto(ws.Cell(FilaCodigos, ColTipo)) != CodigoTipo || Texto(ws.Cell(FilaCodigos, ColActividad)) != CodigoActividad)
        { Agregar(res, new(hoja, FilaCodigos, null, "Esta planilla es de una versión anterior (no tiene la columna «Tipo de Actividad»). Volvé a descargarla desde el sistema.")); return; }

        var ultimaCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var colDe = new Dictionary<string, int>();
        for (int c = PrimeraColCampo; c <= ultimaCol; c++)
            if (Texto(ws.Cell(FilaCodigos, c)) is { Length: > 0 } cod) colDe[cod] = c;
        var faltantes = CatalogoCampos.Todos.Where(c => !c.EsCalculado && !colDe.ContainsKey(c.Codigo)).GroupBy(c => c.Codigo).Select(g => g.First()).ToList();
        foreach (var c in faltantes) Agregar(res, new(hoja, FilaCodigos, null, $"Falta la columna \"{c.Nombre}\": no borres ni muevas columnas de la planilla."));
        if (faltantes.Count > 0) return;

        var ultimaFila = ws.LastRowUsed()?.RowNumber() ?? 0;
        var bloques = new Dictionary<(int, TipoPlan), Bloque>();
        bool CeldaVacia(int r, int c) => Leer(ws.Cell(r, c)).Vacia;

        for (int r = PrimeraFila; r <= ultimaFila; r++)
        {
            var todasVacias = Enumerable.Range(PrimeraColCampo, Math.Max(0, ultimaCol - PrimeraColCampo + 1)).All(c => CeldaVacia(r, c));
            if (ws.Cell(r, ColCodigo).IsEmpty() && ws.Cell(r, ColAnio).IsEmpty() && ws.Cell(r, ColMes).IsEmpty() && CeldaVacia(r, ColTipo) && CeldaVacia(r, ColActividad) && todasVacias) continue;

            if (!EntEro(ws.Cell(r, ColCodigo), out var codigo)) { Agregar(res, new(hoja, r, "Código de área", "Falta el código de área (columna oculta B). No la borres.")); continue; }
            if (!permitidas.TryGetValue(codigo, out var area)) { Agregar(res, new(hoja, r, "Código de área", $"El código {codigo} no corresponde a ninguna de las áreas de tu empresa.")); continue; }
            if (TipoDeTexto(Texto(ws.Cell(r, ColTipo))) is not { } tipo) { Agregar(res, new(hoja, r, "Tipo de Actividad", $"{area.Nombre}: el tipo de actividad tiene que ser «Convencional» o «No Convencional». No lo modifiques.")); continue; }

            // Período
            if (!EntEro(ws.Cell(r, ColAnio), out var anio)) { Agregar(res, new(hoja, r, "Año", $"{area.Nombre}: el año no es un número.")); continue; }
            int mes;
            var mesTxt = Texto(ws.Cell(r, ColMes));
            if (mesTxt == "" || mesTxt.Equals("Anual", StringComparison.OrdinalIgnoreCase)) mes = 0;
            else if (!EntEro(ws.Cell(r, ColMes), out mes) || mes is < 1 or > 12) { Agregar(res, new(hoja, r, "Mes", $"{area.Nombre} {anio}: el mes tiene que ser un número de 1 a 12.")); continue; }
            if (anio < anioCiclo) { Agregar(res, new(hoja, r, "Año", $"{area.Nombre}: el plan empieza en {anioCiclo}; {anio} no corresponde.")); continue; }
            if (anio == anioCiclo && mes == 0) { Agregar(res, new(hoja, r, "Mes", $"{area.Nombre}: el año {anio} se informa mes por mes (1 a 12).")); continue; }
            if (anio > anioCiclo && mes != 0) { Agregar(res, new(hoja, r, "Mes", $"{area.Nombre}: a partir de {anioCiclo + 1} los valores son anuales: dejá el mes en blanco o \"Anual\".")); continue; }

            if (!bloques.TryGetValue((codigo, tipo), out var bloque))
                bloques[(codigo, tipo)] = bloque = new Bloque { Area = area, Tipo = tipo, Plan = new PlanLeido { Tipo = tipo } };
            var donde = $"{area.Nombre} ({TextoTipo(tipo)}) {Periodo(anio, mes)}";
            if (bloque.Filas.TryGetValue((anio, mes), out var antes)) { Agregar(res, new(hoja, r, "Año", $"{donde} está repetido (ya estaba en la fila {antes}).")); continue; }
            bloque.Filas[(anio, mes)] = r;

            // ¿Tiene actividad? se responde en la primera fila del bloque (enero del primer año).
            if (anio == anioCiclo && mes == 1)
            {
                bloque.FilaPregunta = r;
                var resp = Texto(ws.Cell(r, ColActividad)).ToLowerInvariant();
                if (resp is "sí" or "si") bloque.Tiene = true;
                else if (resp == "no") bloque.Tiene = false;
                else Agregar(res, new(hoja, r, "¿Tiene actividad?", $"{area.Nombre} ({TextoTipo(tipo)}): elegí Sí o No en ¿Tiene actividad? (primera fila del bloque)."));
            }

            // Un año posterior totalmente en blanco es un año que la empresa no usa (la planilla trae de más): no se importa.
            var entradas = CatalogoCampos.Para(tipo).Where(c => !c.EsCalculado).ToList();
            if (anio > anioCiclo && entradas.All(c => CeldaVacia(r, colDe[c.Codigo]))) { bloque.Filas.Remove((anio, mes)); continue; }

            // Valores: ninguna celda vacía (si no hay valor, 0). Si el bloque es «No», se controla después que no haya valores.
            foreach (var c in entradas)
            {
                var leida = Leer(ws.Cell(r, colDe[c.Codigo]));
                if (leida.Problema is not null) { Agregar(res, new(hoja, r, c.Nombre, $"{donde}: {leida.Problema}")); continue; }
                if (leida.Vacia) { bloque.Plan.Valores.Add(new ValorLeido(anio, mes, c.Codigo, decimal.MinValue)); continue; }   // marcador de «vacía»; se resuelve al cerrar el bloque
                if (!Numero(leida.Valor, out var valor)) { Agregar(res, new(hoja, r, c.Nombre, $"{donde}: \"{leida.Texto}\" no es un número. Usá solo números (decimales con coma).")); continue; }
                if (ReglasDeDatos.Motivo(c, valor) is { } motivo) { Agregar(res, new(hoja, r, c.Nombre, $"{donde}: {motivo}")); continue; }
                bloque.Plan.Valores.Add(new ValorLeido(anio, mes, c.Codigo, valor));
            }
        }

        // Cierre de cada bloque: «No» (sin valores) o «Sí» (completo, 12 meses y años consecutivos).
        foreach (var b in bloques.Values.OrderBy(b => b.Area.Nombre).ThenBy(b => b.Tipo))
        {
            var nombre = $"{b.Area.Nombre} ({TextoTipo(b.Tipo)})";
            var vacias = b.Plan.Valores.Where(v => v.Valor == decimal.MinValue).ToList();
            var cargadas = b.Plan.Valores.Where(v => v.Valor != decimal.MinValue).ToList();
            if (b.Tiene is null)
            {
                if (b.FilaPregunta == 0) Agregar(res, new(hoja, null, null, $"{nombre}: falta la fila de enero {anioCiclo}, donde se elige ¿Tiene actividad?."));
                continue;   // el error de la celda ya se informó
            }
            if (b.Tiene == false)
            {
                if (cargadas.Count > 0)
                    Agregar(res, new(hoja, b.FilaPregunta, "¿Tiene actividad?", $"{nombre}: elegiste No pero cargaste valores. Borralos o elegí Sí."));
                b.Plan.Valores.Clear(); b.Plan.SinActividad = true;
                continue;
            }
            foreach (var v in vacias)
            {
                var campo = CatalogoCampos.Buscar(b.Tipo, v.Campo)!;
                Agregar(res, new(hoja, b.Filas[(v.Anio, v.Mes)], campo.Nombre, $"{nombre} {Periodo(v.Anio, v.Mes)}: celda vacía. Si no hay valor, ingresá 0."));
            }
            b.Plan.Valores.RemoveAll(v => v.Valor == decimal.MinValue);

            var faltan = Enumerable.Range(1, 12).Where(m => !b.Filas.ContainsKey((anioCiclo, m))).ToList();
            if (faltan.Count > 0)
                Agregar(res, new(hoja, null, null, $"{nombre}: faltan los meses de {anioCiclo}: {string.Join(", ", faltan.Select(m => Validador.Meses[m - 1]))}."));
            var anuales = b.Filas.Keys.Where(k => k.Item1 > anioCiclo).Select(k => k.Item1).OrderBy(a => a).ToList();
            if (anuales.Count > 0)
            {
                var huecos = Enumerable.Range(anioCiclo + 1, anuales.Max() - anioCiclo).Where(a => !anuales.Contains(a)).ToList();
                if (huecos.Count > 0) Agregar(res, new(hoja, null, null, $"{nombre}: faltan los años {string.Join(", ", huecos)}. Los años tienen que ser consecutivos hasta el último que informes."));
            }
        }

        // Cada área tiene que traer sus dos bloques; el resultado es una entrada por área con sus planes.
        foreach (var g in bloques.Values.GroupBy(b => b.Area.IdBloque).OrderBy(g => g.First().Area.Nombre))
        {
            var area = new AreaLeida { IdBloque = g.Key, Nombre = g.First().Area.Nombre };
            foreach (var tipo in Presentacion.Actividades)
            {
                var b = g.FirstOrDefault(x => x.Tipo == tipo);
                if (b is null) { Agregar(res, new(hoja, null, null, $"{area.Nombre}: falta el bloque {TextoTipo(tipo)}. No borres filas de la planilla.")); continue; }
                area.Planes.Add(b.Plan);
            }
            res.Areas.Add(area);
        }
    }

    static string Periodo(int anio, int mes) => mes > 0 ? $"{Validador.Meses[mes - 1]} {anio}" : anio.ToString();

    /// <summary>
    /// Lo que hay en una celda, SIN recalcular fórmulas: si tiene una, se toma el valor que Excel dejó guardado. Recalcular puede fallar
    /// (fórmulas a otra hoja que no está, a otro libro, con error): en vez de romper la importación, se informa en esa celda.
    /// </summary>
    sealed record Leida(XLCellValue Valor, string? Problema)
    {
        /// <summary>Sin nada para importar (una fórmula con problemas no cuenta como vacía: se informa).</summary>
        public bool Vacia => Problema is null && (Valor.IsBlank || (Valor.IsText && Valor.GetText().Trim() == ""));
        public string Texto => Valor.IsBlank ? "" : Valor.IsText ? Valor.GetText().Trim() : Valor.ToString(CultureInfo.InvariantCulture).Trim();
    }

    static Leida Leer(IXLCell c)
    {
        if (!c.HasFormula) return new(c.Value, null);
        const string pegar = "Pegá solo valores, no fórmulas.";
        try
        {
            var v = c.CachedValue;
            // Sin valor guardado (archivos hechos por herramientas que no calculan): solo se calcula una fórmula simple, que no toque otra hoja ni otro libro,
            // porque esas se resuelven mal (a veces dan 0) y un 0 de más no se vería.
            if (v.IsBlank && !(c.FormulaA1 ?? "").Contains('!') && !(c.FormulaA1 ?? "").Contains('[')) v = c.Value;
            if (v.IsError) return new(default, $"tiene una fórmula con error ({v.GetError()}). {pegar}");
            if (v.IsBlank) return new(default, $"tiene una fórmula sin valor calculado. {pegar}");
            return new(v, null);
        }
        catch (Exception) { return new(default, $"tiene una fórmula que no se pudo leer. {pegar}"); }
    }


    static string Texto(IXLCell c) => Leer(c).Texto;

    static bool EntEro(IXLCell c, out int valor)
    {
        valor = 0;
        var l = Leer(c);
        if (l.Problema is not null || l.Vacia) return false;
        if (l.Valor.IsNumber) { var d = l.Valor.GetNumber(); valor = (int)d; return d == Math.Floor(d) && d is > 0 and < 1_000_000_000; }
        return int.TryParse(l.Texto, NumberStyles.None, CultureInfo.InvariantCulture, out valor) && valor > 0;
    }

    /// <summary>Número de una celda: numérico de Excel, o texto en convención es-AR ("1.234,5", "12,5").</summary>
    static bool Numero(XLCellValue v, out decimal valor)
    {
        valor = 0;
        if (v.IsNumber)
        {
            var d = v.GetNumber();
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e12) return false;
            return decimal.TryParse(d.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out valor);
        }
        if (!v.IsText) return false;     // fecha, lógico, error…
        var t = Regex.Replace(v.GetText(), @"\s", "");
        if (t.Contains(',')) t = t.Replace(".", "").Replace(',', '.');
        else if (Regex.IsMatch(t, @"^[1-9]\d{0,2}(\.\d{3})+$")) t = t.Replace(".", "");
        return Regex.IsMatch(t, @"^\d+(\.\d+)?$") && decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
    }
}
