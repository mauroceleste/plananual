using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlanAnual.Infrastructure.Data;

/// <summary>
/// Solo para `dotnet ef`: arma el contexto con el proveedor de producción (PostgreSQL, nombres en snake_case)
/// sin necesitar una base ni credenciales. La cadena de conexión es ficticia: generar migraciones no se conecta.
/// </summary>
public sealed class FabricaDeContextoEnDiseno : IDesignTimeDbContextFactory<PlanAnualDbContext>
{
    public PlanAnualDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<PlanAnualDbContext>()
            .UseNpgsql("Host=solo-diseno;Database=plananual;Username=diseno;Password=diseno",
                       n => n.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PlanAnualDbContext(o);
    }
}
