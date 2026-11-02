using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Web.Infraestructura;

public static class Sesion
{
    public static ClaimsPrincipal Crear(UsuarioSesion u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Nombre),
            new(ClaimTypes.Email, u.Email),
            new("cuits", string.Join(",", u.Cuits)),
            new(ClaimTypes.Role, u.Rol.ToString()),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
    }

    public static UsuarioSesion ToSesion(this ClaimsPrincipal p) => new(
        int.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0"),
        p.Identity?.Name ?? "",
        p.FindFirstValue(ClaimTypes.Email) ?? "",
        (p.FindFirstValue("cuits") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries),
        Enum.Parse<Rol>(p.FindFirstValue(ClaimTypes.Role) ?? nameof(Rol.Empresa)));
}

public static class Formato
{
    public static readonly CultureInfo AR = new("es-AR");

    public static string Num(decimal? v, string f = "0.######") => v?.ToString(f, AR) ?? "";
    public static string Num2(decimal? v) => v?.ToString("N2", AR) ?? "—";
    public static string Fecha(DateTime? d) => d?.ToString("dd/MM/yyyy HH:mm", AR) ?? "—";
    public static string Fecha(DateOnly? d) => d?.ToString("dd/MM/yyyy", AR) ?? "—";

    public static string Iniciales(string nombre) =>
        string.Concat(nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpper(x[0])));

    /// <summary>Ícono del sprite compartido (wwwroot/img/iconos.svg).</summary>
    public static IHtmlContent Icono(this IUrlHelper url, string nombre) =>
        new HtmlString($"<svg class=\"icon\" aria-hidden=\"true\" focusable=\"false\"><use href=\"{url.Content("~/img/iconos.svg")}#i-{nombre}\"/></svg>");
}
