using ClosedXML.Excel;

namespace PlanAnual.Infrastructure.Servicios;

/// <summary>
/// Planillas modelo en Excel para la documentación de los bloques 2 a 4 de la nota. La empresa las baja, las completa y las
/// adjunta (bloques 2 y 3) o se apoya en ellas para redactar los textos (bloque 4). No se importan: son un formato común
/// para que la Subsecretaría reciba la información ordenada y con las mismas columnas de todas las empresas.
/// </summary>
public static class PlanillasModelo
{
    public const string Instalaciones = "instalaciones", Paradas = "paradas", Bloque4 = "bloque4";
    const int FilasLibres = 30;

    public static bool Existe(string? codigo) => codigo is Instalaciones or Paradas or Bloque4;

    /// <summary>Código de planilla que corresponde a un tipo de anexo (null si ese anexo no tiene planilla modelo).</summary>
    public static string? DeAnexo(string codigoAnexo) => codigoAnexo switch
    {
        "flujograma" => Instalaciones,
        "paradas_planta" => Paradas,
        _ => null,
    };

    public static string Nombre(string codigo) => codigo switch
    {
        Instalaciones => "instalaciones",
        Paradas => "paradas_de_planta",
        _ => "conflictos_y_equipos",
    };

    public static byte[] Generar(string codigo, int anio, string razonSocial, string area)
    {
        using var wb = new XLWorkbook();
        switch (codigo)
        {
            case Instalaciones:
                Hoja(wb, "Instalaciones", $"Bloque 2 · Instalaciones y transporte de hidrocarburos · {area}", razonSocial, anio,
                    "Una fila por instalación. El flujograma (el esquema) se adjunta aparte, en el mismo punto del sistema.",
                    [
                        new("Instalación", 30), new("Tipo", 24, ["Planta de tratamiento", "Almacenamiento", "Ducto de transporte", "Punto de medición fiscal", "Otra"]),
                        new("Ubicación", 28), new("Capacidad de tratamiento de petróleo (m³/d)", 22, Decimal: true),
                        new("Capacidad de tratamiento de gas (MMm³/d)", 22, Decimal: true), new("Capacidad de almacenamiento (m³)", 20, Decimal: true),
                        new("Código de punto de medición fiscal", 22), new("Observaciones", 36),
                    ]);
                break;
            case Paradas:
                Hoja(wb, "Paradas de planta", $"Bloque 3 · Paradas programadas de planta proyectadas para {anio} · {area}", razonSocial, anio,
                    "Una fila por parada. Si no hay paradas previstas, dejá una fila con la instalación y escribí «Sin paradas» en Observaciones. La duración se calcula sola.",
                    [
                        new("Planta o instalación", 30), new("Fecha de inicio", 16, Fecha: true), new("Fecha de fin", 16, Fecha: true),
                        new("Duración (días)", 14, Calculada: true),
                        new("Motivo", 24, ["Mantenimiento programado", "Inspección", "Ampliación o modificación", "Otro"]),
                        new("Pérdida de producción de petróleo (m³)", 22, Decimal: true), new("Pérdida de producción de gas (MMm³)", 22, Decimal: true),
                        new("Observaciones", 36),
                    ]);
                break;
            default:
                Hoja(wb, "Conflictos superficiarios", $"Bloque 4 · Conflictos con superficiarios · {area}", razonSocial, anio,
                    "Una fila por conflicto vigente o previsto. Si no hay ninguno, escribí «Ninguno» en la primera fila.",
                    [
                        new("Superficiario", 30), new("Ubicación o lote", 26), new("Descripción del conflicto", 44),
                        new("Estado", 16, ["Vigente", "Previsto", "Resuelto"]), new("Actividad que afecta", 30),
                        new("Fecha estimada de resolución", 20, Fecha: true),
                    ]);
                Hoja(wb, "Equipos", $"Bloque 4 · Disponibilidad de equipos · {area}", razonSocial, anio,
                    "Una fila por equipo o contrato. Plazo en meses y costo en dólares por día. Si no hay contratos firmes, escribí «Ninguno» en la primera fila.",
                    [
                        new("Tipo de equipo", 24, ["Perforación", "Fractura", "Workover o pulling", "Otro"]), new("Contratista", 28),
                        new("Contrato firme", 14, ["Sí", "No"]), new("Fecha de inicio", 16, Fecha: true),
                        new("Plazo (meses)", 14, Decimal: true), new("Costo (USD/día)", 16, Decimal: true), new("Observaciones", 36),
                    ]);
                break;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    sealed record Col(string Titulo, double Ancho, string[]? Lista = null, bool Decimal = false, bool Fecha = false, bool Calculada = false);

    static void Hoja(XLWorkbook wb, string nombre, string titulo, string razon, int anio, string ayuda, Col[] cols)
    {
        var ws = wb.AddWorksheet(nombre);
        ws.Cell(1, 1).Value = titulo;
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        ws.Cell(2, 1).Value = $"{razon} · Plan Anual de Inversiones {anio}";
        ws.Cell(2, 1).Style.Font.SetFontColor(XLColor.DimGray);
        ws.Cell(3, 1).Value = ayuda;
        ws.Cell(3, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.DimGray);

        const int filaTitulos = 5, primera = filaTitulos + 1;
        for (var i = 0; i < cols.Length; i++)
        {
            var c = cols[i]; var n = i + 1;
            var h = ws.Cell(filaTitulos, n);
            h.Value = c.Titulo;
            h.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
            h.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#1F4E79"));
            h.Style.Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            ws.Column(n).Width = c.Ancho;

            var datos = ws.Range(primera, n, primera + FilasLibres - 1, n);
            datos.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            datos.Style.Border.SetInsideBorderColor(XLColor.LightGray).Border.SetOutsideBorderColor(XLColor.LightGray);
            if (c.Lista is not null) datos.CreateDataValidation().List(string.Join(",", c.Lista), true);
            if (c.Decimal)
            {
                datos.Style.NumberFormat.Format = "#,##0.00";
                var dv = datos.CreateDataValidation();
                dv.Decimal.Between(0, 1_000_000_000);
                dv.ErrorTitle = "Valor no válido"; dv.ErrorMessage = "Ingresá un número mayor o igual a 0.";
            }
            if (c.Fecha) datos.Style.DateFormat.Format = "dd/mm/yyyy";
        }
        ws.Row(filaTitulos).Height = 45;

        // Duración de las paradas: días entre inicio y fin (inclusive), solo si hay ambas fechas.
        var dur = Array.FindIndex(cols, c => c.Calculada) + 1;
        if (dur > 0)
        {
            for (var f = primera; f < primera + FilasLibres; f++)
                ws.Cell(f, dur).FormulaA1 = $"=IF(AND(ISNUMBER({ws.Cell(f, dur - 2).Address.ToStringRelative()}),ISNUMBER({ws.Cell(f, dur - 1).Address.ToStringRelative()})),{ws.Cell(f, dur - 1).Address.ToStringRelative()}-{ws.Cell(f, dur - 2).Address.ToStringRelative()}+1,\"\")";
            ws.Range(primera, dur, primera + FilasLibres - 1, dur).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F2F2F2"));
        }
        ws.SheetView.FreezeRows(filaTitulos);
    }
}
