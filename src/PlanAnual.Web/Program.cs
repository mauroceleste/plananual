using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure;
using PlanAnual.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// En producción el sistema no arranca con una configuración que dejaría la puerta abierta o sin base de datos.
if (!builder.Environment.IsDevelopment())
{
    var cfg = builder.Configuration;
    var problemas = new List<string>();
    if (cfg.GetValue<bool>("Acceso:AccesoDeDesarrollo")) problemas.Add("Acceso:AccesoDeDesarrollo está activo (permite entrar sin UsersAccess).");
    if ((cfg.GetConnectionString("Default") ?? "").Contains("CAMBIAR", StringComparison.OrdinalIgnoreCase)) problemas.Add("ConnectionStrings:Default sigue con valores de ejemplo.");
    if (string.Equals(cfg["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase)) problemas.Add("Database:Provider=Sqlite es solo para desarrollo.");
    if (cfg.GetValue<bool>("Mail:Habilitado") && string.IsNullOrWhiteSpace(cfg["Mail:Host"])) problemas.Add("Mail:Habilitado está activo pero falta Mail:Host.");
    if (problemas.Count > 0) throw new InvalidOperationException("Configuración inválida para producción:" + Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", problemas));
}

builder.Services.AddControllersWithViews(o => o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
builder.Services.AddPlanAnual(builder.Configuration);

// Las claves que firman la cookie de sesión deben sobrevivir a reinicios y reciclados del sitio (si no, todos pierden la sesión).
var carpetaClaves = builder.Configuration["DataProtection:Carpeta"];
var dp = builder.Services.AddDataProtection().SetApplicationName("PlanAnual");
if (!string.IsNullOrWhiteSpace(carpetaClaves)) dp.PersistKeysToFileSystem(new DirectoryInfo(carpetaClaves));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/sin-permiso";
        o.Cookie.Name = "plananual.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        o.SlidingExpiration = true;
        // Los fetch del formulario necesitan un 401 (no una redirección) para avisar que venció la sesión.
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Headers.XRequestedWith == "XMLHttpRequest") { ctx.Response.StatusCode = 401; return Task.CompletedTask; }
            ctx.Response.Redirect(ctx.RedirectUri); return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Subsecretaria", p => p.RequireRole("Subsecretaria", "Administrador"))
    .AddPolicy("Administrador", p => p.RequireRole("Administrador"));

var app = builder.Build();

var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrEmpty(pathBase)) app.UsePathBase(pathBase);

if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/error"); app.UseHsts(); }

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PlanAnualDbContext>();
    // En producción el esquema lo aplica la persona que administra la base con db/001_creacion_inicial.sql;
    // con Database:AplicarMigraciones=true también puede aplicarlo la propia aplicación al iniciar.
    if (!app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:AplicarMigraciones"))
        await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
    {
        Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
        db.Database.EnsureCreated();   // En producción se usan migraciones de EF.
        if (!await db.Ciclos.AnyAsync())
        {
            db.Ciclos.Add(new Ciclo { Anio = 2026, Nombre = "Plan de Inversiones 2026", FechaLimite = new(2026, 2, 20), FechaProrroga = new(2026, 3, 6) });
            await db.SaveChangesAsync();
        }
    }
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();

public partial class Program;
