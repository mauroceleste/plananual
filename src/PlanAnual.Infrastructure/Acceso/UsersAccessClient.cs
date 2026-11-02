using System.Text;
using System.Xml.Linq;

namespace PlanAnual.Infrastructure.Acceso;

/// <summary>Datos de la persona que ingresó. No se lee el campo Password que trae ExchangeData.</summary>
public sealed record DatosUsuario(int IdUsuario, string Usuario, string Nombre, string Apellido, string Email, DateTime? Inicio = null, DateTime? Fin = null);

public sealed record PermisoModulo(int IdModulo, int IdTipoPermiso, bool Baja);

public interface IUsersAccessClient
{
    /// <returns>null si la sesión no existe o venció.</returns>
    Task<DatosUsuario?> UsuarioPorSesionAsync(int idSesion, CancellationToken ct = default);
    Task<IReadOnlyList<PermisoModulo>> PermisosAsync(int idSesion, int idSistema, CancellationToken ct = default);
    /// <summary>CUIT de las empresas asignadas al usuario (IdEmpresa de ExchangeDataCompany).</summary>
    Task<IReadOnlyList<string>> CuitsDeEmpresasAsync(int idUsuario, CancellationToken ct = default);
}

/// <summary>Cliente SOAP (ASMX) de WSUserAccessV2, mismo criterio que el de Legajos: HTTP directo, solo lectura.</summary>
public sealed class UsersAccessClient(HttpClient http) : IUsersAccessClient
{
    public async Task<DatosUsuario?> UsuarioPorSesionAsync(int idSesion, CancellationToken ct = default)
    {
        var res = await Llamar("obtainUserDataBySession", $"<idSesion>{idSesion}</idSesion>", ct);
        var r = res.Descendants().FirstOrDefault(e => e.Name.LocalName == "obtainUserDataBySessionResult");
        if (r is null) return null;
        int.TryParse(Texto(r, "IdUsuario"), out var id);
        if (id <= 0) return null;
        return new DatosUsuario(id, Texto(r, "Usuario") ?? "", Texto(r, "Nombre") ?? "", Texto(r, "Apellido") ?? "", Texto(r, "Email") ?? "",
            Fecha(Texto(r, "FechaInicio")), Fecha(Texto(r, "FechaFin")));
    }

    public async Task<IReadOnlyList<PermisoModulo>> PermisosAsync(int idSesion, int idSistema, CancellationToken ct = default)
    {
        var res = await Llamar("obtainModulosPermisosBySession", $"<idSesion>{idSesion}</idSesion><idSistema>{idSistema}</idSistema>", ct);
        return res.Descendants().Where(e => e.Name.LocalName == "Permisos")
            .Select(p => new PermisoModulo(
                int.TryParse(Texto(p, "IdModulo"), out var m) ? m : 0,
                int.TryParse(Texto(p, "IdTipoPermiso"), out var t) ? t : 0,
                string.Equals(Texto(p, "Baja"), "true", StringComparison.OrdinalIgnoreCase)))
            .Where(p => p.IdModulo > 0).ToList();
    }

    public async Task<IReadOnlyList<string>> CuitsDeEmpresasAsync(int idUsuario, CancellationToken ct = default)
    {
        var res = await Llamar("ExchangeDataCompanyGetByIdUsuario", $"<idUsuario>{idUsuario}</idUsuario>", ct);
        return res.Descendants().Where(e => e.Name.LocalName == "ExchangeDataCompany")
            .Select(e => new string((Texto(e, "IdEmpresa") ?? "").Where(char.IsDigit).ToArray()))
            .Where(c => c.Length > 0).Distinct().ToList();
    }

    async Task<XDocument> Llamar(string operacion, string parametros, CancellationToken ct)
    {
        var sobre = $"""<?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><{operacion} xmlns="http://tempuri.org/">{parametros}</{operacion}></soap:Body></soap:Envelope>""";
        using var req = new HttpRequestMessage(HttpMethod.Post, "") { Content = new StringContent(sobre, Encoding.UTF8, "text/xml") };
        req.Headers.Add("SOAPAction", $"\"http://tempuri.org/{operacion}\"");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return XDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    /// <summary>El servicio manda 0001-01-01 cuando no hay fecha: se toma como "sin dato".</summary>
    static DateTime? Fecha(string? texto) =>
        DateTime.TryParse(texto, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) && d.Year > 1900 ? d : null;

    static string? Texto(XElement padre, string nombre) =>
        padre.Elements().FirstOrDefault(e => e.Name.LocalName == nombre)?.Value.Trim();
}
