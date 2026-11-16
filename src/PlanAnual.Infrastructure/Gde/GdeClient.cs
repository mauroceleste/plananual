using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PlanAnual.Infrastructure.Gde;

public sealed record GdeMovimiento(string? Fecha, DateTime? FechaIso, string? Origen, string? Destino, string? Estado, string? Motivo, string? Usuario);
public sealed record GdeDocumento(string? Numero, string? Tipo, string? Referencia, string? Fecha, string? OrdenEnExpediente);
public sealed record GdeHistorial(string? Numero, string? Caratula, string? EstadoActual, List<GdeMovimiento> Movimientos, List<GdeDocumento> Documentos, DateTime? ObtenidoEn);

/// <summary>Resultado de consultar GDE: o hay datos, o hay un mensaje en español para mostrar.</summary>
public sealed record GdeResultado(bool Ok, string Mensaje, GdeHistorial? Datos = null, bool DesdeCache = false);

public interface IGdeClient
{
    /// <summary>False si falta la dirección o la clave del servicio (la pantalla lo avisa en lugar de fallar).</summary>
    bool Configurado { get; }
    Task<GdeResultado> HistorialAsync(string numero, CancellationToken ct = default);
}

/// <summary>
/// Consulta de solo lectura a la API interna de GDE (la misma de Registro de Empresas): historial de un expediente.
/// Es lenta (5 a 10 s la primera vez; después queda 30 min en caché del servicio) y atiende de a una consulta. La clave nunca llega al navegador.
/// Decidir quién puede consultar es responsabilidad de quien llama: la API no sabe quién es el usuario final.
/// </summary>
public sealed partial class GdeClient(HttpClient http, string? apiKey) : IGdeClient
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool Configurado => http.BaseAddress is not null && !string.IsNullOrWhiteSpace(apiKey);

    [GeneratedRegex(@"^[A-Z]{2,8}-\d{4}-\d{8}-NEU-[A-Z0-9]{2,15}(#[A-Z0-9]{2,15}){0,3}$")]
    private static partial Regex Patron();
    [GeneratedRegex(@"-\s*-")]
    private static partial Regex SegmentoVacio();

    /// <summary>Mayúsculas, sin espacios de más y sin segmentos en blanco (EX-2026-00297840- -NEU-… queda EX-2026-00297840-NEU-…).</summary>
    public static string LimpiarNumero(string? numero) => SegmentoVacio().Replace((numero ?? "").Trim().ToUpperInvariant(), "-");
    public static bool EsNumeroValido(string? numero) => Patron().IsMatch(LimpiarNumero(numero));

    public async Task<GdeResultado> HistorialAsync(string numero, CancellationToken ct = default)
    {
        if (!Configurado) return new(false, "La consulta a GDE todavía no está configurada en este sistema.");
        var limpio = LimpiarNumero(numero);
        if (!Patron().IsMatch(limpio)) return new(false, "El número de expediente no tiene el formato de GDE (por ejemplo EX-2026-00297840-NEU-SEMH#MERN).");

        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Get, $"api/gde/expediente/{Uri.EscapeDataString(limpio)}/historial");
            pedido.Headers.Add("X-Api-Key", apiKey);
            using var r = await http.SendAsync(pedido, ct);
            Respuesta? cuerpo = null;
            try { cuerpo = await r.Content.ReadFromJsonAsync<Respuesta>(Json, ct); } catch (JsonException) { }

            if (r.IsSuccessStatusCode && cuerpo is { Ok: true, Datos: { } d })
                return new(true, "OK", new GdeHistorial(d.Numero, d.Caratula, d.EstadoActual, d.Movimientos ?? [], d.Documentos ?? [], d.ObtenidoEn), cuerpo.DesdeCache);

            return new(false, Traducir(r.StatusCode, cuerpo?.Mensaje));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new(false, "GDE tardó demasiado en responder. Probá de nuevo en un momento."); }
        catch (HttpRequestException) { return new(false, "No se pudo conectar con el servicio de GDE."); }
    }

    // El servicio devuelve un mensaje en español apto para mostrar; si no vino, uno según el estado.
    static string Traducir(HttpStatusCode estado, string? mensaje) => !string.IsNullOrWhiteSpace(mensaje) ? mensaje : estado switch
    {
        HttpStatusCode.NotFound => "No se encontró ese expediente en GDE.",
        HttpStatusCode.Unauthorized => "El servicio de GDE rechazó la clave de este sistema.",
        HttpStatusCode.Forbidden => "El servicio de GDE no autoriza esta consulta.",
        HttpStatusCode.TooManyRequests => "Se hicieron demasiadas consultas a GDE. Esperá un minuto.",
        _ => "No se pudo consultar GDE en este momento.",
    };

    sealed record Respuesta(bool Ok, string? Codigo, string? Mensaje, bool DesdeCache, Datos? Datos);
    sealed record Datos(string? Numero, string? Caratula, string? EstadoActual, List<GdeMovimiento>? Movimientos, List<GdeDocumento>? Documentos, DateTime? ObtenidoEn);
}
