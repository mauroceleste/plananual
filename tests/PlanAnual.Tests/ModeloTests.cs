using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;

namespace PlanAnual.Tests;

public class CatalogoTests
{
    [Fact]
    public void Codigos_son_unicos_por_tipo_de_plan()
    {
        foreach (var plan in new[] { TipoPlan.Cench, TipoPlan.Convencional })
        {
            var repetidos = CatalogoCampos.Para(plan).GroupBy(c => c.Codigo).Where(g => g.Count() > 1);
            Assert.Empty(repetidos);
        }
    }

    [Fact]
    public void Columnas_coinciden_con_las_planillas_oficiales()
    {
        // Planilla CENCH: 35 columnas de datos (C..AJ) con Total de inversión y Desarrollo.
        // Se cuentan los campos cargables + calculados del modelo oficial, sin los totales propios de gastos.
        Assert.Equal(34, CatalogoCampos.Para(TipoPlan.Cench).Count(c => c.Codigo != "gto_total"));
        // Planilla Convencionales: 51 columnas (C..BA) sin el total de gastos que el modelo no trae.
        Assert.Equal(51, CatalogoCampos.Para(TipoPlan.Convencional).Count(c => c.Codigo != "gto_total"));
    }

    [Fact]
    public void Los_calculados_solo_dependen_de_campos_anteriores()
    {
        foreach (var plan in new[] { TipoPlan.Cench, TipoPlan.Convencional })
        {
            var vistos = new HashSet<string>();
            foreach (var c in CatalogoCampos.Para(plan))
            {
                foreach (var dep in (c.Suma ?? []).Concat(c.Resta ?? []))
                    Assert.Contains(dep, vistos);
                vistos.Add(c.Codigo);
            }
        }
    }
}

public class CalculadoraTests
{
    static Dictionary<string, decimal?> D(params (string, decimal)[] v) =>
        v.ToDictionary(x => x.Item1, x => (decimal?)x.Item2);

    [Fact]
    public void Convencional_calcula_desarrollo_total_y_flujo()
    {
        var r = Calculadora.CalcularAnio(TipoPlan.Convencional, D(
            ("inv_perforacion", 3), ("inv_terminacion", 2), ("inv_workover", 1), ("inv_pulling", 0.5m),
            ("inv_instal_produccion", 1.5m),
            ("abandono_usd", 0.5m), ("gto_salarios", 2), ("gto_energia", 1),
            ("ingresos", 20), ("regalias", 2), ("iibb_otros", 0.6m), ("iigg", 1)));

        Assert.Equal(6.5m, r["inv_desarrollo"]);
        Assert.Equal(8m, r["inv_total"]);
        Assert.Equal(3m, r["gto_total"]);
        Assert.Equal(20 - 8 - 0.5m - 3 - 2 - 0.6m - 1, r["flujo_fondos"]);
    }

    [Fact]
    public void Cench_usa_desarrollo_ingresado_y_gastos_fijo_variable()
    {
        var r = Calculadora.CalcularAnio(TipoPlan.Cench, D(
            ("inv_desarrollo", 100), ("inv_plantas_nuevas", 20), ("gto_fijo", 5), ("gto_variable", 7),
            ("ingresos", 300), ("regalias", 30)));

        Assert.Equal(120m, r["inv_total"]);
        Assert.Equal(12m, r["gto_total"]);
        Assert.Equal(300 - 120 - 12 - 30, r["flujo_fondos"]);
    }

    [Fact]
    public void Un_calculado_ingresado_a_mano_se_pisa()
    {
        var r = Calculadora.CalcularAnio(TipoPlan.Convencional, D(("inv_desarrollo", 999), ("inv_perforacion", 1)));
        Assert.Equal(1m, r["inv_desarrollo"]);
    }
}

public class PersistenciaTests
{
    [Fact]
    public async Task Guarda_y_lee_una_presentacion_con_valores()
    {
        using var cn = new SqliteConnection("DataSource=:memory:");
        cn.Open();
        var opts = new DbContextOptionsBuilder<PlanAnualDbContext>().UseSqlite(cn).Options;

        await using (var db = new PlanAnualDbContext(opts))
        {
            db.Database.EnsureCreated();
            db.Ciclos.Add(new Ciclo { Id = 1, Anio = 2026, Nombre = "Plan de Inversiones 2026", FechaLimite = new(2026, 2, 20) });
            db.Presentaciones.Add(new Presentacion
            {
                CicloId = 1, Cuit = "30711732752", IdBloque = 56, TipoPlan = TipoPlan.Convencional,
                Valores = { new PlanValor { Anio = 2026, Campo = "pozos_perforados", Valor = 4 } }
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new PlanAnualDbContext(opts))
        {
            var p = await db.Presentaciones.Include(x => x.Valores).SingleAsync();
            Assert.Equal(4m, p.Valores.Single().Valor);
        }
    }
}
