using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PlanAnual.Domain.Entities;

namespace PlanAnual.Infrastructure.Data;

public class PlanAnualDbContext(DbContextOptions<PlanAnualDbContext> options) : DbContext(options)
{
    public DbSet<Ciclo> Ciclos => Set<Ciclo>();
    public DbSet<BloqueTipoActividad> BloquesTipoActividad => Set<BloqueTipoActividad>();
    public DbSet<Presentacion> Presentaciones => Set<Presentacion>();
    public DbSet<PlanValor> PlanValores => Set<PlanValor>();
    public DbSet<Anexo> Anexos => Set<Anexo>();
    public DbSet<EnvioMail> EnviosMail => Set<EnvioMail>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Ciclo>(e =>
        {
            e.HasIndex(x => x.Anio).IsUnique();
            e.Property(x => x.Nombre).HasMaxLength(150);
            e.Ignore(x => x.LimiteVigente);
        });

        b.Entity<BloqueTipoActividad>(e =>
        {
            e.HasKey(x => x.IdBloque);
            e.Property(x => x.IdBloque).ValueGeneratedNever();
            e.Property(x => x.NombreArea).HasMaxLength(150);
            e.Property(x => x.NombreBloque).HasMaxLength(150);
        });

        b.Entity<Presentacion>(e =>
        {
            e.Property(x => x.Cuit).HasMaxLength(11);
            e.Property(x => x.RazonSocial).HasMaxLength(150);
            e.Property(x => x.NombreArea).HasMaxLength(150);
            e.Property(x => x.NombreBloque).HasMaxLength(150);
            e.Property(x => x.NumeroComprobante).HasMaxLength(40);
            e.Property(x => x.HuellaEnvio).HasMaxLength(64);
            e.Property(x => x.UsuarioNombre).HasMaxLength(150);
            e.Property(x => x.EnviadaPor).HasMaxLength(150);
            e.Property(x => x.AsistidaPor).HasMaxLength(150);
            e.HasIndex(x => new { x.CicloId, x.Cuit, x.IdBloque, x.Version }).IsUnique();
            e.HasIndex(x => x.NumeroComprobante).IsUnique();
            e.HasOne(x => x.Ciclo).WithMany().HasForeignKey(x => x.CicloId).OnDelete(DeleteBehavior.Restrict);   // borrar una convocatoria nunca debe llevarse las presentaciones
            e.HasMany(x => x.Valores).WithOne().HasForeignKey(v => v.PresentacionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Anexos).WithOne().HasForeignKey(a => a.PresentacionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PlanValor>(e =>
        {
            e.HasKey(x => new { x.PresentacionId, x.Anio, x.Campo });
            e.Property(x => x.Campo).HasMaxLength(40);
            e.Property(x => x.Valor).HasPrecision(18, 6);
            e.HasIndex(x => x.Campo);
        });

        b.Entity<Anexo>(e =>
        {
            e.Property(x => x.Tipo).HasMaxLength(60);
            e.Property(x => x.NombreArchivo).HasMaxLength(255);
            e.Property(x => x.RutaAlmacenada).HasMaxLength(500);
            e.Property(x => x.Sha256).HasMaxLength(64);
        });

        b.Entity<EnvioMail>(e =>
        {
            e.Property(x => x.Asunto).HasMaxLength(250);
            e.HasOne<Presentacion>().WithMany().HasForeignKey(x => x.PresentacionId);
        });

        // PostgreSQL: las fechas son hora local de la Provincia, sin zona. Npgsql rechaza DateTime local en timestamptz,
        // así que se guardan como "timestamp without time zone" (Kind sin especificar).
        if (Database.IsNpgsql())
        {
            var aSinKind = new ValueConverter<DateTime, DateTime>(v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified), v => v);
            var aSinKindNulo = new ValueConverter<DateTime?, DateTime?>(v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Unspecified) : v, v => v);
            foreach (var prop in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
            {
                if (prop.ClrType == typeof(DateTime)) { prop.SetColumnType("timestamp without time zone"); prop.SetValueConverter(aSinKind); }
                else if (prop.ClrType == typeof(DateTime?)) { prop.SetColumnType("timestamp without time zone"); prop.SetValueConverter(aSinKindNulo); }
            }
        }
    }
}
