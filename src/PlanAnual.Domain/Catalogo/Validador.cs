using PlanAnual.Domain.Entities;

namespace PlanAnual.Domain.Catalogo;

/// <param name="EsRequisito">Falta algo que pide la nota (dato del año firme, documentación o texto del bloque 4). Bloquea la presentación final aunque no sea un error de datos.</param>
public sealed record Observacion(bool EsError, string Mensaje, int? Anio = null, bool EsRequisito = false)
{
    public bool Bloquea => EsError || EsRequisito;
}

/// <summary>
/// Controles previos al envío. Los errores y los requisitos de la nota faltantes bloquean la presentación final;
/// el resto son advertencias de coherencia que se muestran pero no bloquean (la Subsecretaría las verá en el tablero).
/// </summary>
public static class Validador
{
    public static readonly string[] Meses = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    /// <summary>Los cuatro textos del bloque 4 de la nota, en el orden en que se piden.</summary>
    public static IReadOnlyList<(string Titulo, Func<Presentacion, string?> Valor)> TextosBloque4 { get; } =
    [
        ("Conflictos con superficiarios", p => p.ConflictosSuperficiarios),
        ("Disponibilidad de equipos: contratos firmes", p => p.EquiposContratosFirmes),
        ("Disponibilidad de equipos: plazos", p => p.EquiposPlazos),
        ("Disponibilidad de equipos: costos", p => p.EquiposCostos),
    ];

    public static string NombreActividad(TipoPlan t) => t == TipoPlan.Cench ? "Plan No Convencional (CENCH)" : "Plan Convencional";

    public static List<Observacion> Validar(Presentacion p, IReadOnlyCollection<AnexoTipoPresentado> anexos)
    {
        var obs = new List<Observacion>();
        int? anioFirme = p.Ciclo?.Anio;

        // Un área puede tener plan Convencional y No Convencional: cada uno se controla por separado. Lo que no tiene hay que
        // declararlo («sin actividad», todo en 0): así la Subsecretaría distingue «no tiene» de «se olvidó».
        var cargados = p.TiposCargados();
        if (!p.Valores.Any(v => v.Valor.HasValue))
            obs.Add(new(true, "La planilla no tiene ningún dato cargado."));
        else
            foreach (var tipo in Presentacion.Actividades)
            {
                if (!cargados.Contains(tipo))
                { obs.Add(new(false, $"{NombreActividad(tipo)}: cargá los valores o marcalo como «sin actividad» (se completa con 0).", EsRequisito: true)); continue; }
                var prefijo = cargados.Count > 1 ? NombreActividad(tipo) + ": " : "";
                obs.AddRange(ValidarPlan(p.ParaTipo(tipo), anioFirme).Select(o => prefijo == "" ? o : o with { Mensaje = prefijo + o.Mensaje }));
            }

        foreach (var req in AnexosRequeridos.Todos.Where(a => a.Obligatorio))
            if (!anexos.Any(a => a.Tipo == req.Codigo))
                obs.Add(new(false, $"Falta adjuntar: {req.Titulo(anioFirme ?? p.AnioDesde)}.", EsRequisito: true));

        foreach (var (titulo, valor) in TextosBloque4)
            if (string.IsNullOrWhiteSpace(valor(p)))
                obs.Add(new(false, $"Restricciones a la actividad: falta completar «{titulo}» (si no hay, escribí «Ninguno»).", EsRequisito: true));

        return obs;
    }

    /// <summary>Controles de los valores de un plan (una sola actividad: <paramref name="p"/> viene de <see cref="Presentacion.ParaTipo"/>).</summary>
    static List<Observacion> ValidarPlan(Presentacion p, int? anioFirme)
    {
        var obs = new List<Observacion>();
        var campos = CatalogoCampos.Para(p.TipoPlan);
        var calc = Calculadora.CalcularPlan(p.TipoPlan, p.Valores);

        foreach (var v in p.Valores.Where(v => v.Valor.HasValue))
        {
            var c = campos.FirstOrDefault(x => x.Codigo == v.Campo);
            if (c is null) { obs.Add(new(true, $"El campo '{v.Campo}' no pertenece a este tipo de planilla.", v.Anio)); continue; }
            if (anioFirme is { } af && (v.Anio == af ? v.Mes is < 1 or > 12 : v.Mes != 0))
            { obs.Add(new(false, v.Anio == af ? $"{c.Nombre}: el año {af} se carga mes por mes." : $"{c.Nombre}: a partir de {af + 1} los valores son anuales.", v.Anio, EsRequisito: true)); continue; }
            var motivo = ReglasDeDatos.Motivo(c, v.Valor!.Value);
            if (motivo is not null) obs.Add(new(true, v.Mes > 0 ? $"{Meses[Math.Clamp(v.Mes, 1, 12) - 1]}: {motivo}" : motivo, v.Anio));
        }

        // El primer año (el de la convocatoria) es compromiso firme y se carga por mes: debe estar completo en los campos principales.
        if (anioFirme is { } firme)
        {
            if (!p.Valores.Any(v => v.Anio == firme && v.Valor.HasValue))
                obs.Add(new(true, $"Falta cargar el año {firme}, que se considera compromiso firme de cumplimiento."));
            else
                foreach (var cod in new[] { "pozos_perforados", "prod_petroleo", "prod_gas" })
                {
                    var faltan = Enumerable.Range(1, 12).Where(m => !p.Valores.Any(v => v.Anio == firme && v.Mes == m && v.Campo == cod && v.Valor.HasValue)).ToList();
                    if (faltan.Count > 0)
                        obs.Add(new(false, $"{campos.First(c => c.Codigo == cod).Nombre}: faltan {faltan.Count} mes(es) ({string.Join(", ", faltan.Select(m => Meses[m - 1]))}). Si es cero, cargá 0.", firme, EsRequisito: true));
                }
        }

        decimal previo = 0;
        foreach (var (anio, fila) in calc)
        {
            if (fila.TryGetValue("venta_petroleo", out var vp) && fila.TryGetValue("prod_petroleo", out var pp) && vp > pp * 1.001m)
                obs.Add(new(false, "La venta de petróleo supera la producción.", anio));
            if (fila.TryGetValue("venta_gas", out var vg) && fila.TryGetValue("prod_gas", out var pg) && vg > pg * 1.2m)
                obs.Add(new(false, "La venta de gas supera la producción por más de lo esperable (el gas se vende a 9300 kcal).", anio));
            if (fila.TryGetValue("pozos_conectados", out var pc) && fila.TryGetValue("pozos_perforados", out var pf) && pc > 0 && pf == 0 && anio == p.Ciclo?.Anio)
                obs.Add(new(false, "Hay pozos conectados y ninguno perforado en el año (válido si vienen de años anteriores).", anio));
            if (fila.TryGetValue("desarrollo_recursos", out var dr))
            {
                if (dr < previo) obs.Add(new(false, "El desarrollo de recursos es acumulado y no debería bajar.", anio));
                previo = dr;
            }
        }
        return obs;
    }
}

public sealed record AnexoTipoPresentado(string Tipo);
