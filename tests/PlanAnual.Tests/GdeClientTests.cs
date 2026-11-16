using System.Net;
using System.Text;
using PlanAnual.Infrastructure.Gde;

namespace PlanAnual.Tests;

public class GdeClientTests
{
    sealed class Falso(Func<HttpRequestMessage, HttpResponseMessage> responde) : HttpMessageHandler
    {
        public HttpRequestMessage? Ultimo;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Ultimo = r; return Task.FromResult(responde(r)); }
    }

    static HttpResponseMessage Json(HttpStatusCode s, string json) => new(s) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    static (GdeClient, Falso) Cliente(Func<HttpRequestMessage, HttpResponseMessage> f, string? clave = "clave-de-prueba-1234")
    {
        var h = new Falso(f);
        return (new GdeClient(new HttpClient(h) { BaseAddress = new Uri("http://gde.test:8090/") }, clave), h);
    }

    [Theory]
    [InlineData("EX-2026-00297840-NEU-SEMH#MERN", true)]
    [InlineData("  ex-2026-00297840- -neu-semh#mern ", true)]      // se limpia: mayúsculas y segmento en blanco
    [InlineData("EX-2026-0029784-NEU-SEMH", false)]               // 7 dígitos
    [InlineData("hola", false)]
    [InlineData("", false)]
    [InlineData("EX-2026-00297840-NEU-SEMH/../x", false)]
    public void Valida_el_formato_del_numero_de_GDE(string numero, bool valido) => Assert.Equal(valido, GdeClient.EsNumeroValido(numero));

    [Fact]
    public void Limpia_el_numero()
    {
        Assert.Equal("EX-2026-00297840-NEU-SEMH#MERN", GdeClient.LimpiarNumero(" ex-2026-00297840- -neu-semh#mern "));
    }

    [Fact]
    public async Task Consulta_con_la_clave_en_el_header_y_el_numero_codificado()
    {
        var (c, h) = Cliente(_ => Json(HttpStatusCode.OK, """
            {"ok":true,"codigo":"Ok","mensaje":"OK","desdeCache":true,"datos":{"numero":"EX-2026-00297840-NEU-SEMH#MERN","caratula":"Plan","estadoActual":"En trámite",
            "movimientos":[{"fecha":"09-09-2026 10:15","fechaIso":"2026-09-09T10:15:00","origen":"A","destino":"B","estado":"x","motivo":"m","usuario":"u"}],
            "documentos":[{"numero":"IF-1","tipo":"Informe","referencia":"r","fecha":"f","ordenEnExpediente":"3"}],"obtenidoEn":"2026-10-28T14:00:00"}}
            """));
        var r = await c.HistorialAsync("EX-2026-00297840-NEU-SEMH#MERN");
        Assert.True(r.Ok); Assert.True(r.DesdeCache);
        Assert.Equal("En trámite", r.Datos!.EstadoActual);
        Assert.Single(r.Datos.Movimientos); Assert.Equal(new DateTime(2026, 9, 9, 10, 15, 0), r.Datos.Movimientos[0].FechaIso);
        Assert.Equal("IF-1", r.Datos.Documentos[0].Numero);
        Assert.Equal("clave-de-prueba-1234", h.Ultimo!.Headers.GetValues("X-Api-Key").Single());
        Assert.EndsWith("api/gde/expediente/EX-2026-00297840-NEU-SEMH%23MERN/historial", h.Ultimo.RequestUri!.AbsoluteUri);   // el # va como %23
    }

    [Fact]
    public async Task No_llama_a_GDE_si_no_esta_configurado_o_el_numero_es_invalido()
    {
        var llamadas = 0;
        var (sinClave, _) = Cliente(_ => { llamadas++; return Json(HttpStatusCode.OK, "{}"); }, clave: "");
        Assert.False(sinClave.Configurado);
        Assert.Contains("configurada", (await sinClave.HistorialAsync("EX-2026-00297840-NEU-SEMH")).Mensaje);
        var (c, _) = Cliente(_ => { llamadas++; return Json(HttpStatusCode.OK, "{}"); });
        Assert.Contains("formato", (await c.HistorialAsync("cualquier cosa")).Mensaje);
        Assert.Equal(0, llamadas);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"ok":false,"codigo":"NoEncontrado","mensaje":"El expediente no existe."}""", "El expediente no existe.")]
    [InlineData(HttpStatusCode.Unauthorized, "", "rechazó la clave")]
    [InlineData(HttpStatusCode.TooManyRequests, "no es json", "demasiadas")]
    [InlineData(HttpStatusCode.BadGateway, "", "No se pudo consultar GDE")]
    public async Task Los_errores_del_servicio_se_muestran_en_castellano(HttpStatusCode estado, string cuerpo, string esperado)
    {
        var (c, _) = Cliente(_ => Json(estado, cuerpo));
        var r = await c.HistorialAsync("EX-2026-00297840-NEU-SEMH");
        Assert.False(r.Ok); Assert.Contains(esperado, r.Mensaje);
    }

    [Fact]
    public async Task Si_el_servicio_no_responde_avisa_sin_romper()
    {
        var (c, _) = Cliente(_ => throw new HttpRequestException("sin red"));
        var r = await c.HistorialAsync("EX-2026-00297840-NEU-SEMH");
        Assert.False(r.Ok); Assert.Contains("No se pudo conectar", r.Mensaje);
    }
}
