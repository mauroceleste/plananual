using PlanAnual.Domain.Entities;

namespace PlanAnual.Domain.Catalogo;

public sealed record Observacion(bool EsError, string Mensaje, int? Anio = null);

/// <summary>
/// Controles previos al envío. Los errores bloquean el envío; las advertencias se muestran
/// pero la empresa puede enviar igual (la Subsecretaría las verá en el tablero).
/// </summary>
public static class Validador
{
    public static List<Observacion> Validar(Presentacion p, IReadOnlyCollection<AnexoTipoPresentado> anexos)
    {
        var obs = new List<Observacion>();
        var campos = CatalogoCampos.Para(p.TipoPlan);
        var calc = Calculadora.CalcularPlan(p.TipoPlan, p.Valores);

        foreach (var v in p.Valores.Where(v => v.Valor.HasValue))
        {
            var c = campos.FirstOrDefault(x => x.Codigo == v.Campo);
            if (c is null) { obs.Add(new(true, $"El campo '{v.Campo}' no pertenece a este tipo de planilla.", v.Anio)); continue; }
            var motivo = ReglasDeDatos.Motivo(c, v.Valor!.Value);
            if (motivo is not null) obs.Add(new(true, motivo, v.Anio));
        }

        if (!p.Valores.Any(v => v.Valor.HasValue))
            obs.Add(new(true, "La planilla no tiene ningún dato cargado."));

        // El año del ciclo es compromiso firme: debe estar completo en los bloques principales.
        if (p.Ciclo is not null)
        {
            var anioFirme = p.Ciclo.Anio;
            if (!p.Valores.Any(v => v.Anio == anioFirme && v.Valor.HasValue))
                obs.Add(new(true, $"Falta cargar el año {anioFirme}, que se considera compromiso firme de cumplimiento."));
            else
                foreach (var cod in new[] { "pozos_perforados", "prod_petroleo", "prod_gas" })
                    if (!p.Valores.Any(v => v.Anio == anioFirme && v.Campo == cod && v.Valor.HasValue))
                        obs.Add(new(false, $"{anioFirme}: {campos.First(c => c.Codigo == cod).Nombre} está vacío. Si es cero, cargá 0.", anioFirme));
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

        foreach (var req in AnexosRequeridos.Todos.Where(a => a.Obligatorio))
            if (!anexos.Any(a => a.Tipo == req.Codigo))
                obs.Add(new(false, $"Falta adjuntar: {req.Nombre}."));

        return obs;
    }
}

public sealed record AnexoTipoPresentado(string Tipo);
