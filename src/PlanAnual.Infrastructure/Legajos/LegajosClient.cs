using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;

namespace PlanAnual.Infrastructure.Legajos;

public sealed record BloqueInfo(int IdBloque, string NombreArea, string NombreBloque, string Cuit, string RazonSocial, string? IdNqn, string? TipoActividad = null)
{
    public string Nombre => NombreArea == NombreBloque ? NombreArea : $"{NombreArea} - {NombreBloque}";
}

public interface ILegajosClient
{
    Task<IReadOnlyList<BloqueInfo>> BloquesPorOperadorAsync(string cuit, CancellationToken ct = default);
    Task<IReadOnlyList<BloqueInfo>> TodosLosBloquesAsync(CancellationToken ct = default);
}

/// <summary>
/// Cliente del WS de Legajos de Áreas (ASMX). Se habla SOAP por HTTP directo: no hace falta un proxy WCF
/// porque solo se usan dos operaciones de lectura.
/// </summary>
public sealed partial class LegajosClient(HttpClient http, IMemoryCache cache) : ILegajosClient
{
    static readonly TimeSpan Vida = TimeSpan.FromMinutes(10);

    public Task<IReadOnlyList<BloqueInfo>> BloquesPorOperadorAsync(string cuit, CancellationToken ct = default)
    {
        if (!SoloDigitos().IsMatch(cuit)) throw new ArgumentException("CUIT inválido", nameof(cuit));
        return Cacheado($"bloques:{cuit}", () => Llamar("GetAllAreasBloquesActivasByOperador", $"<cuit>{cuit}</cuit>", ct));
    }

    public Task<IReadOnlyList<BloqueInfo>> TodosLosBloquesAsync(CancellationToken ct = default) =>
        Cacheado("bloques:todos", () => Llamar("GetAllAreasBloquesActivas", "", ct));

    async Task<IReadOnlyList<BloqueInfo>> Cacheado(string clave, Func<Task<IReadOnlyList<BloqueInfo>>> carga) =>
        (await cache.GetOrCreateAsync(clave, async e => { e.AbsoluteExpirationRelativeToNow = Vida; return await carga(); }))!;

    async Task<IReadOnlyList<BloqueInfo>> Llamar(string operacion, string parametros, CancellationToken ct)
    {
        var sobre = $"""<?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><{operacion} xmlns="http://tempuri.org/">{parametros}</{operacion}></soap:Body></soap:Envelope>""";
        using var req = new HttpRequestMessage(HttpMethod.Post, "") { Content = new StringContent(sobre, Encoding.UTF8, "text/xml") };
        req.Headers.Add("SOAPAction", $"\"http://tempuri.org/{operacion}\"");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var xml = XDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        // El DataSet viaja como DiffGram: cada fila modificada se repite dentro de <diffgr:before> (valores originales).
        return xml.Descendants().Where(e => e.Name.LocalName == "dt" && !e.Ancestors().Any(a => a.Name.LocalName == "before"))
            .Select(dt => new
            {
                Id = Texto(dt, "idbloque"), Area = Texto(dt, "nombrearea"), Bloque = Texto(dt, "nombrebloque"),
                Cuit = Texto(dt, "cuit"), Razon = Texto(dt, "razon_social"), Nqn = Texto(dt, "ID_NQN"),
                // Campo que todavía no existe en el servicio: cuando lo agreguen, se lee solo (ver TipoActividadProvider).
                Tipo = Texto(dt, "tipo_actividad")
            })
            .Where(x => int.TryParse(x.Id, out _))
            .Select(x => new BloqueInfo(int.Parse(x.Id!), x.Area ?? "", x.Bloque ?? "", x.Cuit ?? "", x.Razon ?? "", x.Nqn, x.Tipo))
            .GroupBy(b => b.IdBloque).Select(g => g.First())
            .ToList();
    }

    static string? Texto(XElement dt, string nombre) =>
        dt.Elements().FirstOrDefault(e => e.Name.LocalName == nombre)?.Value.Trim();

    [GeneratedRegex(@"^\d{1,11}$")] private static partial Regex SoloDigitos();
}
