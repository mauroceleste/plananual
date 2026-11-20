using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlanAnual.Infrastructure.Acceso;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Gde;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Mail;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Database:Provider = Postgres (por defecto) o Sqlite (desarrollo local sin servidor).</summary>
    public static IServiceCollection AddPlanAnual(this IServiceCollection s, IConfiguration config)
    {
        var cs = config.GetConnectionString("Default") ?? throw new InvalidOperationException("Falta ConnectionStrings:Default.");
        s.AddDbContext<PlanAnualDbContext>(o =>
        {
            if (string.Equals(config["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase)) o.UseSqlite(cs);
            else o.UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history")).UseSnakeCaseNamingConvention();
        });

        s.AddMemoryCache();
        s.AddHttpClient<ILegajosClient, LegajosClient>(c =>
        {
            c.BaseAddress = new Uri(config["Legajos:Url"] ?? throw new InvalidOperationException("Falta Legajos:Url."));
            c.Timeout = TimeSpan.FromSeconds(30);
        })
        // El WS es interno (10.x): va directo, el proxy corporativo responde 403.
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false });
        s.AddHttpClient<IUsersAccessClient, UsersAccessClient>(c =>
        {
            c.BaseAddress = new Uri(config["Acceso:UsersAccessUrl"] ?? throw new InvalidOperationException("Falta Acceso:UsersAccessUrl."));
            c.Timeout = TimeSpan.FromSeconds(20);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false });
        // GDE (solo lectura): dirección y clave del servicio interno. Sin ellas la pantalla avisa que no está configurado.
        s.AddHttpClient<IGdeClient, GdeClient>((sp, c) =>
        {
            if (Uri.TryCreate((config["Gde:BaseUrl"] ?? "").TrimEnd('/') + "/", UriKind.Absolute, out var u) && !string.IsNullOrWhiteSpace(config["Gde:BaseUrl"])) c.BaseAddress = u;
            c.Timeout = TimeSpan.FromSeconds(100);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false })
        .AddTypedClient<IGdeClient>((h, sp) => new GdeClient(h, config["Gde:ApiKey"]));
        s.AddScoped<AutenticadorUsersAccess>();
        s.AddSingleton<IConfigurationLike>(new ConfigAdapter(config));
        s.AddSingleton<IMailSender, SmtpMailSender>();
        s.AddScoped<PresentacionService>();
        s.AddScoped<ComunicacionService>();
        s.AddScoped<TableroService>();
        s.AddScoped<ITipoActividadProvider, TipoActividadProvider>();
        return s;
    }

    sealed class ConfigAdapter(IConfiguration c) : IConfigurationLike
    {
        public IEnumerable<string> Lista(string clave) =>
            (c[clave] ?? "").Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
