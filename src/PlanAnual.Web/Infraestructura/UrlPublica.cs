using Microsoft.AspNetCore.Mvc;

namespace PlanAnual.Web.Infraestructura;

public static class UrlPublica
{
    /// <summary>
    /// Dirección absoluta para poner en un mail. Detrás del proxy el host y el esquema internos no sirven:
    /// se usa el origen público configurado (PublicOrigin); sin él, el de la solicitud.
    /// </summary>
    public static string Absoluta(this Controller c, IConfiguration config, string accion, string controlador, object? valores = null)
    {
        var origen = config["PublicOrigin"];
        return string.IsNullOrWhiteSpace(origen)
            ? c.Url.Action(accion, controlador, valores, c.Request.Scheme)!
            : origen.TrimEnd('/') + c.Url.Action(accion, controlador, valores);
    }
}
