using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PlanAnual.Infrastructure.Mail;

/// <summary>Qué pasó con un envío: el error (null si salió bien), a quién fue de verdad y si el modo de prueba lo desvió.</summary>
public sealed record ResultadoMail(string? Error, IReadOnlyList<string> Destinatarios, bool Desviado = false)
{
    public bool Ok => Error is null;
}

public interface IMailSender
{
    /// <summary>Un fallo de mail nunca debe romper el envío del plan: se informa en el resultado, no se lanza.</summary>
    Task<ResultadoMail> EnviarAsync(IEnumerable<string> para, string asunto, string html);
}

/// <summary>
/// Config "Mail": Habilitado, Host, Puerto, Usuario, Clave, Desde, ModoPrueba, ListaPrueba.
/// Con ModoPrueba=true todo se desvía a ListaPrueba con asunto [PRUEBA] (igual que Registro de Empresas).
/// </summary>
public sealed class SmtpMailSender(IConfiguration config, ILogger<SmtpMailSender> log) : IMailSender
{
    public async Task<ResultadoMail> EnviarAsync(IEnumerable<string> para, string asunto, string html)
    {
        var c = config.GetSection("Mail");
        if (!c.GetValue<bool>("Habilitado"))
        {
            log.LogInformation("Mail deshabilitado en este entorno. No se envió: {Asunto}", asunto);
            return new("El envío de mails está deshabilitado en este entorno.", []);
        }
        var destinos = para.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var desviado = c.GetValue<bool>("ModoPrueba");
        if (desviado)
        {
            asunto = "[PRUEBA] " + asunto;
            destinos = (c["ListaPrueba"] ?? "").Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        if (destinos.Count == 0) return new("Sin destinatarios.", destinos, desviado);

        try
        {
            using var smtp = new SmtpClient(c["Host"], c.GetValue("Puerto", 25))
            {
                EnableSsl = c.GetValue<bool>("Ssl"),
                Credentials = string.IsNullOrEmpty(c["Usuario"]) ? null : new NetworkCredential(c["Usuario"], c["Clave"])
            };
            using var msg = new MailMessage { From = new MailAddress(c["Desde"] ?? "plananual@neuquen.gov.ar"), Subject = asunto, Body = html, IsBodyHtml = true,
                SubjectEncoding = System.Text.Encoding.UTF8, BodyEncoding = System.Text.Encoding.UTF8 };
            foreach (var d in destinos) msg.To.Add(d);
            await smtp.SendMailAsync(msg);
            return new(null, destinos, desviado);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudo enviar el mail '{Asunto}'", asunto);
            return new(ex.Message, destinos, desviado);
        }
    }
}
