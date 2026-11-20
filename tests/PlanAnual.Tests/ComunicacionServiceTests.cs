using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Mail;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

public sealed class ComunicacionServiceTests : IDisposable
{
    sealed class MailFalso : IMailSender
    {
        public List<(string[] Para, string Asunto, string Html)> Enviados { get; } = [];
        public string? Error { get; set; }
        public bool Desviado { get; set; }
        public Task<ResultadoMail> EnviarAsync(IEnumerable<string> para, string asunto, string html)
        { var d = para.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray(); if (Error is null) Enviados.Add((d, asunto, html)); return Task.FromResult(new ResultadoMail(Error, d, Desviado)); }
    }
    sealed class Cfg : IConfigurationLike { public IEnumerable<string> Lista(string clave) => clave == "Mail:Subsecretaria" ? ["subsecretaria@neuquen.gov.ar"] : []; }

    static readonly UsuarioSesion Ana = new(1, "Ana (Alfa)", "ana@alfa.com", ["111"], Rol.Empresa);
    static readonly UsuarioSesion Beto = new(2, "Beto (Beta)", "beto@beta.com", ["222"], Rol.Empresa);
    static readonly UsuarioSesion Admin = new(3, "Admin", "admin@neuquen.gov.ar", [], Rol.Administrador);
    static readonly UsuarioSesion Consulta = new(4, "Consulta", "consulta@neuquen.gov.ar", [], Rol.Subsecretaria);
    static readonly BloqueInfo BloqueAlfa = new(10, "Área Alfa", "Área Alfa", "111", "Alfa S.A.", "A010");
    static readonly BloqueInfo BloqueAlfa2 = new(11, "Área Alfa Dos", "Área Alfa Dos", "111", "Alfa S.A.", "A011");
    const string Url = "http://x/Comunicaciones?cuit=111";

    readonly SqliteConnection cn = new("DataSource=:memory:");
    readonly MailFalso mail = new();
    readonly PlanAnualDbContext db;
    readonly PresentacionService pres;
    readonly ComunicacionService svc;
    Ciclo ciclo;

    public ComunicacionServiceTests()
    {
        cn.Open();
        db = new PlanAnualDbContext(new DbContextOptionsBuilder<PlanAnualDbContext>().UseSqlite(cn).Options);
        db.Database.EnsureCreated();
        db.Ciclos.Add(new Ciclo { Anio = 2026, Nombre = "Plan 2026", FechaLimite = new(2099, 1, 1) });
        db.SaveChanges();
        ciclo = db.Ciclos.First();
        pres = new PresentacionService(db, mail, new Cfg());
        svc = new ComunicacionService(db, mail, new Cfg());
    }
    public void Dispose() { db.Dispose(); cn.Dispose(); }

    static DateOnly En(int dias) => DateOnly.FromDateTime(DateTime.Today.AddDays(dias));
    static RegistroComunicacion Nota(string asunto = "Pedido de información", DateOnly? plazo = null, bool mailea = false, bool visible = true, string? extra = null) =>
        new(TipoComunicacion.NotaEnviada, asunto, "Detalle de la nota", "NO-2026-0001-NEU", plazo, null, null, null, visible, mailea, extra);
    Task<Comunicacion> Registrar(RegistroComunicacion r, UsuarioSesion? u = null) => svc.RegistrarAsync(u ?? Admin, ciclo, "111", "Alfa S.A.", r, Url);

    async Task<Presentacion> AreaConDatos(BloqueInfo b)
    {
        var p = await pres.IniciarAsync(Ana, b, TipoPlan.Convencional);
        await pres.GuardarCeldasAsync(p.Id, Ana, [new(2026, "pozos_perforados", 4, 1), new(2026, "prod_petroleo", 0.8m, 1), new(2026, "prod_gas", 12, 1), new(2026, "inv_perforacion", 3, 1)]);
        return p;
    }

    // ---------- Permisos y validaciones ----------

    [Fact]
    public async Task Registrar_y_decidir_es_solo_de_Administracion()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Registrar(Nota(), Ana));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Registrar(Nota(), Consulta));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.RechazarPlanAsync(Ana, ciclo, "111", "Alfa S.A.", "Motivo suficiente del rechazo", En(10), true, null, false, null, Url));
        Assert.Empty(await db.Comunicaciones.ToListAsync());
    }

    [Fact]
    public async Task Valida_el_asunto_el_plazo_de_la_prorroga_y_a_quien_responde()
    {
        await Assert.ThrowsAsync<OperacionInvalida>(() => Registrar(Nota("ab")));
        await Assert.ThrowsAsync<OperacionInvalida>(() => Registrar(Nota(new string('x', 201))));
        await Assert.ThrowsAsync<OperacionInvalida>(() => Registrar(Nota(plazo: En(-1))));                                                                          // plazo pasado
        await Assert.ThrowsAsync<OperacionInvalida>(() => Registrar(new(TipoComunicacion.ProrrogaOtorgada, "Prórroga", null, null, null, null, null, null, true, false, null)));   // sin nuevo plazo
        await Assert.ThrowsAsync<OperacionInvalida>(() => Registrar(new(TipoComunicacion.RespuestaEmpresa, "Respuesta", null, null, null, 9999, null, null, true, false, null)));   // nota inexistente
        Assert.Empty(await db.Comunicaciones.ToListAsync());
    }

    // ---------- Notas con plazo: pendiente, vencida, respondida ----------

    [Fact]
    public async Task Una_nota_con_plazo_queda_pendiente_hasta_que_se_registra_la_respuesta()
    {
        var nota = await Registrar(Nota(plazo: En(10)));
        var l = await svc.LineaDeTiempoAsync(Admin, ciclo, "111");
        Assert.Equal(EstadoEmpresa.InformacionSolicitada, l.Estado);
        Assert.Equal(1, l.NotasPendientes);
        Assert.Equal(En(10), l.ProximoVencimiento);
        Assert.Equal("Pendiente", l.Movimientos.Single(m => m.Id == nota.Id).Estado);
        Assert.Equal(1, (await svc.PendientesPorEmpresaAsync(ciclo.Id))["111"]);

        await Registrar(new(TipoComunicacion.RespuestaEmpresa, "Respuesta a la nota", null, "RE-2026-1", null, nota.Id, null, null, true, false, null));
        l = await svc.LineaDeTiempoAsync(Admin, ciclo, "111");
        Assert.Equal("Respondida", l.Movimientos.Single(m => m.Id == nota.Id).Estado);
        Assert.Equal(0, l.NotasPendientes);
        Assert.Null(l.ProximoVencimiento);
        Assert.NotEqual(EstadoEmpresa.InformacionSolicitada, l.Estado);
        Assert.Empty(await svc.PendientesDeRespuestaAsync(Admin, ciclo.Id, "111"));
    }

    [Fact]
    public async Task Una_nota_sin_respuesta_pasado_el_plazo_figura_vencida()
    {
        db.Comunicaciones.Add(new Comunicacion { CicloId = ciclo.Id, Cuit = "111", RazonSocial = "Alfa S.A.", Fecha = DateTime.Now.AddDays(-20), Tipo = TipoComunicacion.NotaEnviada, Asunto = "Vieja", PlazoHasta = En(-3), Usuario = "Admin" });
        await db.SaveChangesAsync();
        var l = await svc.LineaDeTiempoAsync(Admin, ciclo, "111");
        Assert.Equal("Vencida", l.Movimientos.Single().Estado);
        Assert.Equal(1, l.NotasPendientes);                      // sigue sin contestar
    }

    // ---------- Prórroga ----------

    [Fact]
    public async Task La_prorroga_otorgada_extiende_el_limite_solo_de_esa_empresa_y_contesta_el_pedido()
    {
        var pedido = await Registrar(new(TipoComunicacion.ProrrogaSolicitada, "Pedimos 15 días más", null, null, null, null, null, null, true, false, null));
        Assert.Equal("Pendiente", (await svc.LineaDeTiempoAsync(Admin, ciclo, "111")).Movimientos.Single().Estado);
        var general = ciclo.LimiteVigente;

        await Assert.ThrowsAsync<OperacionInvalida>(() => Registrar(new(TipoComunicacion.ProrrogaOtorgada, "No alcanza", null, null, general, null, null, null, true, false, null)));   // no es posterior
        var otorgada = await Registrar(new(TipoComunicacion.ProrrogaOtorgada, "Prórroga otorgada", null, "NO-2026-9", general.AddDays(15), null, null, null, true, false, null));

        Assert.Equal(general.AddDays(15), await svc.LimiteDeLaEmpresaAsync(ciclo, "111"));
        Assert.Equal(general, await svc.LimiteDeLaEmpresaAsync(ciclo, "222"));                          // otra empresa: el general
        Assert.Equal(pedido.Id, otorgada.RespondeA);                                                    // sin indicar a cuál, contesta el pedido pendiente
        Assert.Equal("Respondida", (await svc.LineaDeTiempoAsync(Admin, ciclo, "111")).Movimientos.Single(m => m.Id == pedido.Id).Estado);
    }

    [Fact]
    public async Task Con_prorroga_presentar_dentro_del_nuevo_limite_no_es_fuera_de_plazo()
    {
        ciclo = await db.Ciclos.FirstAsync(); ciclo.FechaLimite = En(-5); await db.SaveChangesAsync();             // el límite general ya pasó
        var sinProrroga = await AreaConDatos(BloqueAlfa);
        Assert.Contains("fuera de plazo", (await pres.EnviarAsync(sinProrroga.Id, Ana, "http://x")).Observaciones);

        await Registrar(new(TipoComunicacion.ProrrogaOtorgada, "Prórroga otorgada", null, null, En(20), null, null, null, true, false, null));
        var conProrroga = await AreaConDatos(BloqueAlfa2);
        Assert.Null((await pres.EnviarAsync(conProrroga.Id, Ana, "http://x")).Observaciones);
    }

    // ---------- Lo que ve la empresa ----------

    [Fact]
    public async Task La_empresa_ve_solo_lo_suyo_y_lo_marcado_visible_y_no_los_adjuntos_internos()
    {
        var visible = await Registrar(Nota("Para la empresa"));
        var interna = await Registrar(Nota("Nota interna", visible: false));

        Assert.Equal(2, (await svc.LineaDeTiempoAsync(Admin, ciclo, "111")).Movimientos.Count);
        Assert.Equal("Para la empresa", (await svc.LineaDeTiempoAsync(Ana, ciclo, "111")).Movimientos.Single().Titulo);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.LineaDeTiempoAsync(Beto, ciclo, "111"));       // otra empresa
        Assert.NotNull(await svc.ObtenerAsync(Ana, visible.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ObtenerAsync(Ana, interna.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ObtenerAsync(Beto, visible.Id));
        Assert.NotNull(await svc.ObtenerAsync(Consulta, interna.Id));                                                   // la Subsecretaría consulta todo
    }

    // ---------- Mail: siempre se registra; el mail es opcional ----------

    [Fact]
    public async Task Sin_pedir_mail_solo_se_registra_y_con_mail_se_avisa_a_la_empresa_con_copia_a_la_Subsecretaria()
    {
        await AreaConDatos(BloqueAlfa);                                              // la empresa tiene una casilla conocida (quien inició la planilla)
        var sin = await Registrar(Nota("Solo registro"));
        Assert.Empty(mail.Enviados); Assert.False(sin.MailEnviado);

        var con = await Registrar(Nota("Con mail", plazo: En(7), mailea: true, extra: "contacto@alfa.com; otra@alfa.com"));
        var enviado = Assert.Single(mail.Enviados);
        Assert.Contains("ana@alfa.com", enviado.Para); Assert.Contains("contacto@alfa.com", enviado.Para); Assert.Contains("subsecretaria@neuquen.gov.ar", enviado.Para);
        Assert.Equal("[Plan de Inversiones 2026] Con mail", enviado.Asunto);
        Assert.Contains("NO-2026-0001-NEU", enviado.Html); Assert.Contains(Url, enviado.Html); Assert.Contains(En(7).ToString("dd/MM/yyyy"), enviado.Html);
        Assert.True(con.MailEnviado); Assert.Contains("ana@alfa.com", con.MailDestinatarios);
    }

    [Fact]
    public async Task Si_el_mail_falla_el_movimiento_queda_registrado_igual_y_se_ve_el_motivo()
    {
        await AreaConDatos(BloqueAlfa);
        mail.Error = "SMTP caído";
        var c = await Registrar(Nota("Se registra igual", mailea: true));
        Assert.True(await db.Comunicaciones.AnyAsync(x => x.Id == c.Id));
        Assert.False(c.MailEnviado); Assert.Equal("SMTP caído", c.MailError);
    }

    [Fact]
    public async Task Si_no_hay_casilla_de_la_empresa_se_avisa_y_se_pueden_agregar_a_mano()
    {
        var sin = await Registrar(Nota("Sin casillas", mailea: true));               // la empresa todavía no cargó nada: solo la copia de la Subsecretaría
        Assert.Contains("subsecretaria@neuquen.gov.ar", Assert.Single(mail.Enviados).Para);
        Assert.False(sin.MailEnviado && sin.MailDestinatarios!.Contains("ana@alfa.com"));
        mail.Desviado = true;
        var prueba = await Registrar(Nota("Modo prueba", mailea: true));
        Assert.Contains("modo de prueba", prueba.MailDestinatarios);
    }

    // ---------- Rechazo ----------

    [Fact]
    public async Task Rechazar_el_plan_lo_registra_y_habilita_a_la_empresa_a_rectificar_hasta_el_plazo()
    {
        var a = await AreaConDatos(BloqueAlfa); var b = await AreaConDatos(BloqueAlfa2);
        var ea = await pres.EnviarAsync(a.Id, Ana, "http://x"); await pres.EnviarAsync(b.Id, Ana, "http://x");
        await Assert.ThrowsAsync<OperacionInvalida>(() => pres.RectificarAsync(ea.Id, Ana));                          // sin autorización no se puede

        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.RechazarPlanAsync(Admin, ciclo, "111", "Alfa S.A.", "corto", En(10), true, null, false, null, Url));
        var r = await svc.RechazarPlanAsync(Admin, ciclo, "111", "Alfa S.A.", "Faltan datos de producción de los años 2028 a 2030.", En(10), true, "NO-2026-55", true, null, Url);

        Assert.Equal(TipoComunicacion.Rechazado, r.Tipo); Assert.Equal(En(10), r.PlazoHasta);
        var l = await svc.LineaDeTiempoAsync(Admin, ciclo, "111");
        Assert.Equal(EstadoEmpresa.Rechazado, l.Estado);
        Assert.Equal(2, await db.SolicitudesRectificacion.CountAsync(s => s.Estado == EstadoSolicitud.Habilitada && s.HabilitadaHasta > DateTime.Now));
        var v2 = await pres.RectificarAsync(ea.Id, Ana);                                                              // ahora sí puede
        Assert.Equal(2, v2.Version);
        Assert.NotEmpty(mail.Enviados);                                                                               // avisó por mail

        // Cuando la empresa vuelve a presentar, el estado deja de ser «Rechazado».
        await Task.Delay(1100);
        await pres.EnviarAsync(v2.Id, Ana, "http://x");
        Assert.Equal(EstadoEmpresa.Presentado, (await svc.LineaDeTiempoAsync(Admin, ciclo, "111")).Estado);
    }

    [Fact]
    public async Task Rechazar_sin_habilitar_no_toca_las_rectificaciones()
    {
        var a = await AreaConDatos(BloqueAlfa); await pres.EnviarAsync(a.Id, Ana, "http://x");
        await svc.RechazarPlanAsync(Admin, ciclo, "111", "Alfa S.A.", "Motivo suficiente del rechazo.", En(10), false, null, false, null, Url);
        Assert.Empty(await db.SolicitudesRectificacion.ToListAsync());
    }

    // ---------- La línea de tiempo suma lo que el sistema ya sabe ----------

    [Fact]
    public async Task La_linea_de_tiempo_junta_lo_registrado_con_los_envios_y_los_pedidos_de_rectificacion()
    {
        var a = await AreaConDatos(BloqueAlfa); var b = await AreaConDatos(BloqueAlfa2);
        var e1 = await pres.EnviarAsync(a.Id, Ana, "http://x"); await pres.EnviarAsync(b.Id, Ana, "http://x");
        await pres.SolicitarRectificacionAsync(e1.Id, Ana, "Hubo un error en la inversión de 2027.", "http://x");
        await Registrar(Nota("Nota de la Subsecretaría", plazo: En(5)));
        await svc.RechazarPlanAsync(Admin, ciclo, "111", "Alfa S.A.", "Rechazado por falta de fundamentación.", En(9), false, null, false, null, Url);

        var l = await svc.LineaDeTiempoAsync(Admin, ciclo, "111");
        var tipos = l.Movimientos.Select(m => m.Tipo).ToList();
        Assert.Contains("Presentacion", tipos); Assert.Contains("RectificacionSolicitada", tipos); Assert.Contains("NotaEnviada", tipos); Assert.Contains("Rechazado", tipos);
        Assert.Equal(1, tipos.Count(t => t == "Presentacion"));                                                       // las dos áreas enviadas juntas son un movimiento
        Assert.Contains("2 áreas", l.Movimientos.Single(m => m.Tipo == "Presentacion").Detalle);
        Assert.Equal(l.Movimientos.OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id ?? 0).Select(m => m.Titulo), l.Movimientos.Select(m => m.Titulo));   // lo más nuevo primero
        // La empresa ve su presentación y su pedido, pero no lo que se marcó interno.
        var empresa = await svc.LineaDeTiempoAsync(Ana, ciclo, "111");
        Assert.All(empresa.Movimientos, m => Assert.True(m.Visible));
    }
}
