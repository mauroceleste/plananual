using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

public class TipoActividadTests
{
    [Theory]
    [InlineData("Convencional", TipoPlan.Convencional)]
    [InlineData("CONVENCIONAL", TipoPlan.Convencional)]
    [InlineData("  convencional ", TipoPlan.Convencional)]
    [InlineData("C", TipoPlan.Convencional)]
    [InlineData("No convencional", TipoPlan.Cench)]
    [InlineData("NO_CONVENCIONAL", TipoPlan.Cench)]
    [InlineData("no-convencional", TipoPlan.Cench)]
    [InlineData("CENCH", TipoPlan.Cench)]
    [InlineData("cench (no convencional)", TipoPlan.Cench)]
    [InlineData("NC", TipoPlan.Cench)]
    public void Interpreta_las_formas_habituales_de_escribir_el_tipo(string texto, TipoPlan esperado) =>
        Assert.Equal(esperado, TipoActividadTexto.Interpretar(texto));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mixta")]
    [InlineData("exploración")]
    public void Lo_que_no_se_entiende_queda_sin_definir_y_nunca_se_adivina(string? texto) =>
        Assert.Null(TipoActividadTexto.Interpretar(texto));

    // ---------- Proveedor: lo manual pisa a Legajos ----------

    sealed class LegajosFalso(params BloqueInfo[] bloques) : ILegajosClient
    {
        public Task<IReadOnlyList<BloqueInfo>> BloquesPorOperadorAsync(string cuit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BloqueInfo>>(bloques.Where(b => b.Cuit == cuit).ToList());
        public Task<IReadOnlyList<BloqueInfo>> TodosLosBloquesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BloqueInfo>>(bloques);
    }

    static BloqueInfo B(int id, string? tipo) => new(id, $"Área {id}", $"Área {id}", "111", "Alfa", null, tipo);

    static async Task<(TipoActividadProvider, PlanAnualDbContext, SqliteConnection)> Armar(BloqueInfo[] bloques, params (int Id, TipoPlan T)[] manuales)
    {
        var cn = new SqliteConnection("DataSource=:memory:"); cn.Open();
        var db = new PlanAnualDbContext(new DbContextOptionsBuilder<PlanAnualDbContext>().UseSqlite(cn).Options);
        db.Database.EnsureCreated();
        foreach (var (id, t) in manuales) db.BloquesTipoActividad.Add(new BloqueTipoActividad { IdBloque = id, NombreArea = "x", NombreBloque = "x", TipoPlan = t });
        await db.SaveChangesAsync();
        return (new TipoActividadProvider(db, new LegajosFalso(bloques)), db, cn);
    }

    [Fact]
    public async Task Usa_lo_que_informa_Legajos_cuando_no_hay_nada_fijado_a_mano()
    {
        var (prov, db, cn) = await Armar([B(1, "Convencional"), B(2, "No convencional"), B(3, null), B(4, "Mixta")]);
        using (db) using (cn)
        {
            var t = await prov.TodosAsync();
            Assert.Equal(TipoPlan.Convencional, t[1]);
            Assert.Equal(TipoPlan.Cench, t[2]);
            Assert.False(t.ContainsKey(3));          // Legajos no lo informa
            Assert.False(t.ContainsKey(4));          // valor desconocido: no se adivina
            Assert.Null(await prov.DeAsync(3));
        }
    }

    [Fact]
    public async Task Lo_fijado_a_mano_pisa_a_Legajos_y_completa_lo_que_Legajos_no_sabe()
    {
        var (prov, db, cn) = await Armar([B(1, "Convencional"), B(2, null)], (1, TipoPlan.Cench), (2, TipoPlan.Convencional));
        using (db) using (cn)
        {
            Assert.Equal(TipoPlan.Cench, await prov.DeAsync(1));          // la Subsecretaría corrigió a Legajos
            Assert.Equal(TipoPlan.Convencional, await prov.DeAsync(2));   // Legajos no lo tenía
        }
    }

    // ---------- Lectura del campo nuevo en la respuesta del servicio ----------

    sealed class Falso(string cuerpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "text/xml") });
    }

    static string Respuesta(string filas) => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><R xmlns="http://tempuri.org/"><Result>
        <diffgr:diffgram xmlns:diffgr="urn:schemas-microsoft-com:xml-diffgram-v1"><DocumentElement xmlns="">{filas}</DocumentElement></diffgr:diffgram>
        </Result></R></soap:Body></soap:Envelope>
        """;

    [Fact]
    public async Task Lee_el_campo_tipo_actividad_cuando_el_servicio_lo_agregue()
    {
        var xml = Respuesta("<dt><idbloque>1</idbloque><nombrearea>A</nombrearea><nombrebloque>A</nombrebloque><cuit>111</cuit><razon_social>Alfa</razon_social><ID_NQN>A001</ID_NQN><tipo_actividad>No convencional</tipo_actividad></dt>");
        var c = new LegajosClient(new HttpClient(new Falso(xml)) { BaseAddress = new Uri("http://localhost/ws") }, new MemoryCache(new MemoryCacheOptions()));
        var b = Assert.Single(await c.TodosLosBloquesAsync());
        Assert.Equal("No convencional", b.TipoActividad);
        Assert.Equal(TipoPlan.Cench, TipoActividadTexto.Interpretar(b.TipoActividad));
    }

    [Fact]
    public async Task Hoy_el_servicio_no_trae_el_campo_y_todo_sigue_funcionando()
    {
        var xml = Respuesta("<dt><idbloque>1</idbloque><nombrearea>A</nombrearea><nombrebloque>A</nombrebloque><cuit>111</cuit><razon_social>Alfa</razon_social><ID_NQN>A001</ID_NQN></dt>");
        var c = new LegajosClient(new HttpClient(new Falso(xml)) { BaseAddress = new Uri("http://localhost/ws") }, new MemoryCache(new MemoryCacheOptions()));
        var b = Assert.Single(await c.TodosLosBloquesAsync());
        Assert.Null(b.TipoActividad);
        Assert.Equal("A001", b.IdNqn);
    }
}
