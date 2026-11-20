using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Mail;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

// La empresa responde desde el sistema: contesta las notas de la Subsecretaría y pide prórroga.
public sealed class ComunicacionEmpresaTests : IDisposable
{
    sealed class MailFalso : IMailSender
    {
        public List<(string[] Para, string Asunto, string Html)> Enviados { get; } = [];
        public string? Error { get; set; }
        public Task<ResultadoMail> EnviarAsync(IEnumerable<string> para, string asunto, string html)
        { var d = para.Distinct().ToArray(); if (Error is null) Enviados.Add((d, asunto, html)); return Task.FromResult(new ResultadoMail(Error, d)); }
    }
    sealed class Cfg(bool conSubsecretaria = true) : IConfigurationLike { public IEnumerable<string> Lista(string clave) => clave == "Mail:Subsecretaria" && conSubsecretaria ? ["subsecretaria@neuquen.gov.ar"] : []; }

    static readonly UsuarioSesion Ana = new(1, "Ana (Alfa)", "ana@alfa.com", ["111"], Rol.Empresa);
    static readonly UsuarioSesion Beto = new(2, "Beto (Beta)", "beto@beta.com", ["222"], Rol.Empresa);
    static readonly UsuarioSesion Admin = new(3, "Admin", "admin@neuquen.gov.ar", [], Rol.Administrador);
    const string Url = "http://x/Comunicaciones?cuit=111";

    readonly SqliteConnection cn = new("DataSource=:memory:");
    readonly MailFalso mail = new();
    readonly PlanAnualDbContext db;
    readonly ComunicacionService svc;
    readonly Ciclo ciclo;

    public ComunicacionEmpresaTests()
    {
        cn.Open();
        db = new PlanAnualDbContext(new DbContextOptionsBuilder<PlanAnualDbContext>().UseSqlite(cn).Options);
        db.Database.EnsureCreated();
        db.Ciclos.Add(new Ciclo { Anio = 2026, Nombre = "Plan 2026", FechaLimite = new(2099, 1, 1) });
        db.SaveChanges();
        ciclo = db.Ciclos.First();
        svc = new ComunicacionService(db, mail, new Cfg());
    }
    public void Dispose() { db.Dispose(); cn.Dispose(); }

    static DateOnly En(int dias) => DateOnly.FromDateTime(DateTime.Today.AddDays(dias));
    Task<Comunicacion> Nota(string asunto = "Pedido de información", DateOnly? plazo = null, bool visible = true, UsuarioSesion? u = null) =>
        svc.RegistrarAsync(u ?? Admin, ciclo, "111", "Alfa S.A.", new(TipoComunicacion.NotaEnviada, asunto, "Detalle", null, plazo ?? En(10), null, null, null, visible, false, null), Url);

    [Fact]
    public async Task La_empresa_responde_una_nota_y_se_le_avisa_a_la_Subsecretaria()
    {
        var nota = await Nota("Fundamentación del flujo de fondos");
        var r = await svc.ResponderAsync(Ana, ciclo, "111", nota.Id, "Adjuntamos la fundamentación ampliada.", "RE-2026-77", Url, "fundamentacion.pdf", "1/abc.pdf", 100, "sha");

        Assert.Equal(TipoComunicacion.RespuestaEmpresa, r.Tipo); Assert.True(r.PorLaEmpresa); Assert.True(r.VisibleParaEmpresa);
        Assert.Equal(nota.Id, r.RespondeA); Assert.Equal("Ana (Alfa)", r.Usuario); Assert.Equal("Respuesta: Fundamentación del flujo de fondos", r.Asunto);
        Assert.Equal("fundamentacion.pdf", r.AdjuntoNombre);
        var l = await svc.LineaDeTiempoAsync(Ana, ciclo, "111");
        Assert.Equal("Respondida", l.Movimientos.Single(m => m.Id == nota.Id).Estado);
        Assert.Equal(0, l.NotasPendientes);
        Assert.True(l.Movimientos.Single(m => m.Id == r.Id).PorLaEmpresa);

        var aviso = Assert.Single(mail.Enviados);
        Assert.Equal(["subsecretaria@neuquen.gov.ar"], aviso.Para);                                   // el aviso va a la Subsecretaría, no a la empresa
        Assert.Contains("Alfa S.A.", aviso.Asunto); Assert.Contains(System.Net.WebUtility.HtmlEncode("Adjuntamos la fundamentación ampliada."), aviso.Html); Assert.Contains(Url, aviso.Html);
        Assert.True(r.MailEnviado);
    }

    [Fact]
    public async Task La_empresa_no_puede_responder_lo_que_no_es_suyo_ni_dos_veces_ni_sin_texto()
    {
        var propia = await Nota(); var interna = await Nota("Interna", visible: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ResponderAsync(Beto, ciclo, "111", propia.Id, "Respuesta de otra empresa.", null, Url));      // otra empresa
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ResponderAsync(Admin, ciclo, "111", propia.Id, "Respuesta de un administrador.", null, Url));  // Administración usa el registro
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.ResponderAsync(Ana, ciclo, "111", interna.Id, "Es una nota interna.", null, Url));                       // la empresa no la ve
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.ResponderAsync(Ana, ciclo, "111", 9999, "Nota que no existe.", null, Url));
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.ResponderAsync(Ana, ciclo, "111", propia.Id, "corta", null, Url));
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.ResponderAsync(Ana, ciclo, "111", propia.Id, new string('x', 4001), null, Url));
        Assert.Empty(mail.Enviados);

        await svc.ResponderAsync(Ana, ciclo, "111", propia.Id, "Esta sí es una respuesta válida.", null, Url);
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.ResponderAsync(Ana, ciclo, "111", propia.Id, "Una segunda respuesta.", null, Url));                      // ya respondida
    }

    [Fact]
    public async Task Si_falla_el_mail_o_no_hay_casilla_la_respuesta_queda_registrada_igual()
    {
        var nota = await Nota();
        mail.Error = "SMTP caído";
        var r = await svc.ResponderAsync(Ana, ciclo, "111", nota.Id, "Respuesta con el mail caído.", null, Url);
        Assert.True(await db.Comunicaciones.AnyAsync(x => x.Id == r.Id)); Assert.False(r.MailEnviado); Assert.Equal("SMTP caído", r.MailError);

        var otra = await Nota("Otra nota");
        var sinCasilla = new ComunicacionService(db, mail, new Cfg(conSubsecretaria: false));
        var r2 = await sinCasilla.ResponderAsync(Ana, ciclo, "111", otra.Id, "Respuesta sin casilla de la Subsecretaría.", null, Url);
        Assert.True(await db.Comunicaciones.AnyAsync(x => x.Id == r2.Id)); Assert.False(r2.MailEnviado);
    }

    [Fact]
    public async Task El_pedido_de_prorroga_queda_pendiente_uno_a_la_vez_y_se_resuelve_cuando_Administracion_decide()
    {
        var general = ciclo.LimiteVigente;
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.SolicitarProrrogaAsync(Ana, ciclo, "111", "corto", null, Url));
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.SolicitarProrrogaAsync(Ana, ciclo, "111", "Necesitamos más tiempo.", general, Url));          // no es posterior al límite
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.SolicitarProrrogaAsync(Beto, ciclo, "111", "Pedido de otra empresa.", null, Url));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.SolicitarProrrogaAsync(Admin, ciclo, "111", "Pedido de un administrador.", null, Url));

        var pedido = await svc.SolicitarProrrogaAsync(Ana, ciclo, "111", "Necesitamos 15 días más por el cierre del ejercicio.", general.AddDays(15), Url);
        Assert.Equal(TipoComunicacion.ProrrogaSolicitada, pedido.Tipo); Assert.True(pedido.PorLaEmpresa); Assert.Equal(general.AddDays(15), pedido.PlazoHasta);
        Assert.Equal("Pendiente", (await svc.LineaDeTiempoAsync(Ana, ciclo, "111")).Movimientos.Single().Estado);
        Assert.Equal(general, await svc.LimiteDeLaEmpresaAsync(ciclo, "111"));                          // pedir no cambia el límite
        Assert.Contains("Pedido de prórroga", Assert.Single(mail.Enviados).Asunto);
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.SolicitarProrrogaAsync(Ana, ciclo, "111", "Un segundo pedido encima del primero.", null, Url));   // ya hay uno abierto

        await svc.RegistrarAsync(Admin, ciclo, "111", "Alfa S.A.", new(TipoComunicacion.ProrrogaOtorgada, "Prórroga otorgada", null, null, general.AddDays(15), null, null, null, true, false, null), Url);
        var l = await svc.LineaDeTiempoAsync(Ana, ciclo, "111");
        Assert.Equal("Resuelta", l.Movimientos.Single(m => m.Id == pedido.Id).Estado);
        Assert.Equal(general.AddDays(15), l.LimiteDeLaEmpresa);
        await svc.SolicitarProrrogaAsync(Ana, ciclo, "111", "Ahora pedimos una segunda extensión.", general.AddDays(30), Url);                                   // resuelto el primero, puede pedir otra
    }

    [Fact]
    public async Task Administracion_puede_denegar_un_pedido_con_una_nota_que_lo_contesta_pero_no_contestar_una_nota_con_otra()
    {
        var pedido = await svc.SolicitarProrrogaAsync(Ana, ciclo, "111", "Necesitamos más tiempo, por favor.", null, Url);
        var otraNota = await Nota();
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.RegistrarAsync(Admin, ciclo, "111", "Alfa S.A.", new(TipoComunicacion.NotaEnviada, "Contesta una nota", null, null, null, otraNota.Id, null, null, true, false, null), Url));

        var denegada = await svc.RegistrarAsync(Admin, ciclo, "111", "Alfa S.A.", new(TipoComunicacion.NotaEnviada, "No se concede la prórroga", "El plazo es improrrogable.", null, null, pedido.Id, null, null, true, false, null), Url);
        Assert.Equal(pedido.Id, denegada.RespondeA);
        Assert.Equal("Resuelta", (await svc.LineaDeTiempoAsync(Admin, ciclo, "111")).Movimientos.Single(m => m.Id == pedido.Id).Estado);
        Assert.Equal(await svc.LimiteDeLaEmpresaAsync(ciclo, "111"), ciclo.LimiteVigente);                                                                          // denegar no cambia el límite
    }

    [Fact]
    public async Task El_listado_de_la_Subsecretaria_cuenta_notas_sin_respuesta_prorrogas_por_resolver_y_respuestas_recientes()
    {
        var n1 = await Nota("Primera"); await Nota("Segunda");
        await svc.ResponderAsync(Ana, ciclo, "111", n1.Id, "Respuesta de la empresa a la primera.", null, Url);
        await svc.SolicitarProrrogaAsync(Ana, ciclo, "111", "Pedimos una prórroga para presentar.", null, Url);
        db.Comunicaciones.Add(new Comunicacion { CicloId = ciclo.Id, Cuit = "111", RazonSocial = "Alfa S.A.", Fecha = DateTime.Now.AddDays(-30), Tipo = TipoComunicacion.RespuestaEmpresa, Asunto = "Vieja", PorLaEmpresa = true, Usuario = "Ana" });   // fuera de la semana
        await db.SaveChangesAsync();

        var a = (await svc.ActividadPorEmpresaAsync(ciclo.Id))["111"];
        Assert.Equal(1, a.NotasSinRespuesta);        // la segunda
        Assert.Equal(1, a.ProrrogasPorResolver);
        Assert.Equal(1, a.RespuestasRecientes);      // solo la de esta semana
        Assert.False((await svc.ActividadPorEmpresaAsync(ciclo.Id)).ContainsKey("222"));
    }
}
