using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using PlanAnual.Infrastructure.Legajos;

namespace PlanAnual.Tests;

public class LegajosClientTests
{
    sealed class Falso(string cuerpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "text/xml") });
    }

    // Así responde el ASMX real: la fila modificada se repite en <diffgr:before> sin el campo ID_NQN.
    const string DiffGram = """
        <?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
        <GetAllAreasBloquesActivasResponse xmlns="http://tempuri.org/"><GetAllAreasBloquesActivasResult>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="dt"/></xs:schema>
        <diffgr:diffgram xmlns:diffgr="urn:schemas-microsoft-com:xml-diffgram-v1" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata"><DocumentElement xmlns="">
          <dt diffgr:id="dt1"><idbloque>56</idbloque><nombrearea>Aguada Baguales</nombrearea><nombrebloque>Aguada Baguales</nombrebloque><cuit>30711732752</cuit><razon_social>Oilstone Energia S.A.</razon_social><ID_NQN>A002</ID_NQN></dt>
        </DocumentElement>
        <diffgr:before><dt diffgr:id="dt1"><idbloque>56</idbloque><nombrearea>Aguada Baguales</nombrearea><nombrebloque>Aguada Baguales</nombrebloque><cuit>30711732752</cuit><razon_social>Oilstone Energia S.A.</razon_social></dt></diffgr:before>
        </diffgr:diffgram></GetAllAreasBloquesActivasResult></GetAllAreasBloquesActivasResponse></soap:Body></soap:Envelope>
        """;

    [Fact]
    public async Task Ignora_las_filas_originales_del_diffgram()
    {
        var http = new HttpClient(new Falso(DiffGram)) { BaseAddress = new Uri("http://localhost/ws") };
        var c = new LegajosClient(http, new MemoryCache(new MemoryCacheOptions()));

        var bloques = await c.TodosLosBloquesAsync();

        var b = Assert.Single(bloques);
        Assert.Equal(56, b.IdBloque);
        Assert.Equal("A002", b.IdNqn);
        Assert.Equal("Oilstone Energia S.A.", b.RazonSocial);
    }

    [Fact]
    public async Task Rechaza_un_cuit_que_no_es_numerico()
    {
        var c = new LegajosClient(new HttpClient(new Falso("")) { BaseAddress = new Uri("http://localhost/ws") }, new MemoryCache(new MemoryCacheOptions()));
        await Assert.ThrowsAsync<ArgumentException>(() => c.BloquesPorOperadorAsync("1;DROP"));
    }
}
