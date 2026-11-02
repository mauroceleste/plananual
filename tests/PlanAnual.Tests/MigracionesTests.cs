using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;

namespace PlanAnual.Tests;

/// <summary>Guardas del modelo de producción (PostgreSQL). No se conectan a ninguna base.</summary>
public class MigracionesTests
{
    static PlanAnualDbContext Contexto() => new FabricaDeContextoEnDiseno().CreateDbContext([]);

    [Fact]
    public void El_modelo_no_tiene_cambios_sin_migracion()
    {
        // Si falla: cambiaste una entidad o la configuración y falta `dotnet ef migrations add <Nombre>` (ver DESPLIEGUE.md).
        using var db = Contexto();
        Assert.False(db.Database.HasPendingModelChanges(), "Hay cambios del modelo sin migración.");
    }

    [Fact]
    public void Las_fechas_van_sin_zona_horaria_porque_Npgsql_rechaza_hora_local_en_timestamptz()
    {
        using var db = Contexto();
        var fechas = db.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)).ToList();
        Assert.NotEmpty(fechas);
        Assert.All(fechas, p => Assert.Equal("timestamp without time zone", p.GetColumnType()));
    }

    [Fact]
    public void Los_nombres_son_snake_case_y_la_tabla_de_historial_es_la_esperada()
    {
        using var db = Contexto();
        var tablas = db.Model.GetEntityTypes().Select(t => t.GetTableName()).ToHashSet();
        Assert.Superset(new HashSet<string?> { "presentaciones", "plan_valores", "anexos", "ciclos", "envios_mail", "bloques_tipo_actividad" }, tablas);
        Assert.Equal("numero_comprobante", db.Model.FindEntityType(typeof(Presentacion))!.FindProperty(nameof(Presentacion.NumeroComprobante))!.GetColumnName());
    }

    [Fact]
    public void Borrar_una_convocatoria_no_borra_las_presentaciones()
    {
        using var db = Contexto();
        var fk = db.Model.FindEntityType(typeof(Presentacion))!.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(Ciclo));
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void Un_numero_de_comprobante_y_una_version_por_area_no_se_pueden_repetir()
    {
        using var db = Contexto();
        var indices = db.Model.FindEntityType(typeof(Presentacion))!.GetIndexes().Where(i => i.IsUnique).Select(i => string.Join(",", i.Properties.Select(p => p.Name))).ToList();
        Assert.Contains("NumeroComprobante", indices);
        Assert.Contains("CicloId,Cuit,IdBloque,Version", indices);
    }
}
