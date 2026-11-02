using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Data;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Mail;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

public sealed class PresentacionServiceTests : IDisposable
{
    sealed class MailFalso : IMailSender
    {
        public List<(string[] Para, string Asunto)> Enviados { get; } = [];
        public Task<ResultadoMail> EnviarAsync(IEnumerable<string> para, string asunto, string html) { var d = para.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); Enviados.Add((para.ToArray(), asunto)); return Task.FromResult(new ResultadoMail(null, d, Desviado)); }
        public bool Desviado { get; set; }
    }
    sealed class CfgFalsa : IConfigurationLike { public IEnumerable<string> Lista(string clave) => ["subsecretaria@neuquen.gov.ar"]; }

    readonly SqliteConnection cn = new("DataSource=:memory:");
    readonly MailFalso mail = new();
    readonly PlanAnualDbContext db;
    readonly PresentacionService svc;

    // Dos empresas, un administrador y un usuario de empresa sin ninguna empresa asignada.
    static readonly UsuarioSesion Ana = new(1, "Ana (Alfa)", "ana@alfa.com", ["111"], Rol.Empresa);
    static readonly UsuarioSesion Beto = new(2, "Beto (Beta)", "beto@beta.com", ["222"], Rol.Empresa);
    static readonly UsuarioSesion Admin = new(3, "Admin", "admin@neuquen.gov.ar", [], Rol.Administrador);
    static readonly UsuarioSesion SinEmpresa = new(4, "Sin empresa", "x@x.com", [], Rol.Empresa);
    static readonly BloqueInfo BloqueAlfa = new(10, "Área Alfa", "Área Alfa", "111", "Alfa S.A.", "A010");

    public PresentacionServiceTests()
    {
        cn.Open();
        db = new PlanAnualDbContext(new DbContextOptionsBuilder<PlanAnualDbContext>().UseSqlite(cn).Options);
        db.Database.EnsureCreated();
        db.Ciclos.Add(new Ciclo { Anio = 2026, Nombre = "Plan 2026", FechaLimite = new(2099, 1, 1) });
        db.SaveChanges();
        svc = new PresentacionService(db, mail, new CfgFalsa());
    }
    public void Dispose() { db.Dispose(); cn.Dispose(); }

    async Task<Presentacion> BorradorDeAna() => await svc.IniciarAsync(Ana, BloqueAlfa, TipoPlan.Convencional);

    async Task CargarLoMinimo(long id) =>
        await svc.GuardarCeldasAsync(id, Ana, [new(2026, "pozos_perforados", 4), new(2026, "prod_petroleo", 0.8m), new(2026, "prod_gas", 12), new(2026, "inv_perforacion", 3)]);


    // ---------- Acceso entre empresas ----------

    [Fact]
    public async Task Otra_empresa_no_puede_ver_ni_modificar_una_presentacion_ajena()
    {
        var p = await BorradorDeAna();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ObtenerAsync(p.Id, Beto));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.GuardarCeldasAsync(p.Id, Beto, [new(2026, "pozos_perforados", 99)]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.EnviarAsync(p.Id, Beto, "http://x"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.CambiarRangoAsync(p.Id, Beto, 2026, 2030));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.AgregarAnexoAsync(p.Id, Beto, "flujograma", "a.pdf", "x", 1, "h"));
        Assert.Empty((await db.PlanValores.ToListAsync()));
    }

    [Fact]
    public async Task Usuario_de_empresa_sin_empresas_asignadas_no_ve_nada()
    {
        var p = await BorradorDeAna();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ObtenerAsync(p.Id, SinEmpresa));
        Assert.False(SinEmpresa.VeTodo);
        Assert.False(SinEmpresa.PuedeVer("111"));
    }

    [Fact]
    public async Task No_se_puede_iniciar_una_planilla_de_un_area_ajena()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.IniciarAsync(Beto, BloqueAlfa, TipoPlan.Convencional));
    }

    [Fact]
    public async Task El_rol_de_consulta_ve_todo_pero_no_modifica_nada()
    {
        var consulta = new UsuarioSesion(9, "Consulta", "c@x.com", [], Rol.Subsecretaria);
        var p = await BorradorDeAna();
        Assert.Equal(p.Id, (await svc.ObtenerAsync(p.Id, consulta)).Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.GuardarCeldasAsync(p.Id, consulta, [new(2026, "pozos_perforados", 1)]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.EnviarAsync(p.Id, consulta, "http://x"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.IniciarAsync(consulta, BloqueAlfa, TipoPlan.Convencional));
    }

    // ---------- Administración asiste a una empresa ----------

    [Fact]
    public async Task Administracion_puede_iniciar_cargar_adjuntar_y_enviar_en_nombre_de_la_empresa_y_queda_registrado()
    {
        var p = await svc.IniciarAsync(Admin, BloqueAlfa, TipoPlan.Convencional);
        Assert.Equal("Admin", p.AsistidaPor);
        Assert.Equal("111", p.Cuit);                                   // la planilla es de la empresa, no de quien la cargó

        var filas = await svc.GuardarCeldasAsync(p.Id, Admin, [new(2026, "pozos_perforados", 4), new(2026, "prod_petroleo", 0.8m), new(2026, "prod_gas", 12), new(2026, "inv_perforacion", 3)]);
        Assert.Equal(3m, filas[2026]["inv_total"]);
        await svc.AgregarAnexoAsync(p.Id, Admin, "flujograma", "f.pdf", "1/f.pdf", 10, "abc");

        var enviada = await svc.EnviarAsync(p.Id, Admin, "http://x");
        Assert.Equal(EstadoPresentacion.Enviada, enviada.Estado);
        Assert.Equal("Admin", enviada.EnviadaPor);
        Assert.Equal("Admin", enviada.AsistidaPor);
        var (para, _) = Assert.Single(mail.Enviados);
        Assert.Contains("admin@neuquen.gov.ar", para);                 // le llega a quien envió
        Assert.Contains("subsecretaria@neuquen.gov.ar", para);
    }

    [Fact]
    public async Task Cuando_asiste_Administracion_la_empresa_sigue_pudiendo_ver_y_corregir_su_borrador()
    {
        var p = await svc.IniciarAsync(Admin, BloqueAlfa, TipoPlan.Convencional);
        await svc.GuardarCeldasAsync(p.Id, Admin, [new(2026, "pozos_perforados", 4)]);

        var filas = await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "pozos_perforados", 6)]);   // Ana pisa el valor del admin
        Assert.Equal(6m, (await svc.ObtenerAsync(p.Id, Ana)).Valores.Single().Valor);
        Assert.NotNull(filas);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ObtenerAsync(p.Id, Beto));
    }

    [Fact]
    public async Task Si_solo_trabaja_la_empresa_no_figura_asistencia()
    {
        var p = await BorradorDeAna();
        await CargarLoMinimo(p.Id);
        var enviada = await svc.EnviarAsync(p.Id, Ana, "http://x");
        Assert.Null(enviada.AsistidaPor);
        Assert.Equal("Ana (Alfa)", enviada.EnviadaPor);
    }

    // ---------- Reglas de la carga ----------

    [Fact]
    public async Task Rechaza_campos_calculados_anios_fuera_de_rango_y_campos_de_otro_tipo()
    {
        var p = await BorradorDeAna();
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "inv_total", 5)]));
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(p.Id, Ana, [new(2040, "pozos_perforados", 5)]));
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "gto_fijo", 5)]));   // gasto fijo es de CENCH
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "no_existe", 5)]));
    }

    [Fact]
    public async Task Guardar_devuelve_la_fila_recalculada_y_borrar_una_celda_la_elimina()
    {
        var p = await BorradorDeAna();
        var filas = await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "inv_perforacion", 3), new(2026, "inv_terminacion", 2)]);
        Assert.Equal(5m, filas[2026]["inv_desarrollo"]);
        Assert.Equal(5m, filas[2026]["inv_total"]);

        filas = await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "inv_terminacion", null)]);
        Assert.Equal(3m, filas[2026]["inv_total"]);
        Assert.Single(await db.PlanValores.Where(v => v.PresentacionId == p.Id).ToListAsync());
    }

    [Fact]
    public async Task No_se_puede_enviar_una_planilla_vacia()
    {
        var p = await BorradorDeAna();
        var ex = await Assert.ThrowsAsync<OperacionInvalida>(() => svc.EnviarAsync(p.Id, Ana, "http://x"));
        Assert.Contains("errores", ex.Message);
    }

    [Theory]
    [InlineData("pozos_perforados", "2.5")]
    [InlineData("pozos_perforados", "-3")]
    [InlineData("cap_proceso_petroleo", "6000.5")]
    [InlineData("equipos_perforacion", "1.145")]
    [InlineData("set_fracturas", "0.286")]
    [InlineData("precio_gas", "3.455")]
    [InlineData("desarrollo_recursos", "100.5")]
    [InlineData("inv_perforacion", "3.1234567")]
    public async Task El_servidor_rechaza_al_guardar_lo_que_no_corresponde_al_tipo_del_campo(string campo, string valor)
    {
        var p = await BorradorDeAna();
        var ex = await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, campo, decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture))]));
        Assert.Contains(":", ex.Message);
        Assert.Empty(await db.PlanValores.Where(v => v.PresentacionId == p.Id).ToListAsync());   // no quedó nada a medias
    }

    [Fact]
    public async Task Si_una_celda_del_lote_es_invalida_no_se_guarda_ninguna()
    {
        var p = await BorradorDeAna();
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 12m), new(2026, "pozos_perforados", 2.5m)]));
        Assert.Empty(await db.PlanValores.Where(v => v.PresentacionId == p.Id).ToListAsync());
    }

    [Fact]
    public async Task El_servidor_acepta_los_valores_correctos_de_cada_tipo()
    {
        var p = await BorradorDeAna();
        var filas = await svc.GuardarCeldasAsync(p.Id, Ana, [
            new(2026, "pozos_perforados", 4), new(2026, "equipos_perforacion", 1.14m), new(2026, "cap_proceso_petroleo", 6000),
            new(2026, "desarrollo_recursos", 100), new(2026, "inv_perforacion", 3.123457m), new(2026, "prod_gas", 12.345678m)]);
        Assert.Equal(3.123457m, filas[2026]["inv_total"]);
        Assert.Equal(6, (await db.PlanValores.Where(v => v.PresentacionId == p.Id).ToListAsync()).Count);
    }

    // ---------- Envío, comprobante y rectificatoria ----------

    [Fact]
    public async Task Enviar_registra_comprobante_huella_y_manda_el_mail_a_la_empresa_y_a_la_subsecretaria()
    {
        var p = await BorradorDeAna();
        await CargarLoMinimo(p.Id);

        var enviada = await svc.EnviarAsync(p.Id, Ana, "http://localhost/comprobante");

        Assert.Equal(EstadoPresentacion.Enviada, enviada.Estado);
        Assert.Equal($"PAI-2026-{p.Id:000000}-v1", enviada.NumeroComprobante);
        Assert.Matches("^[0-9a-f]{64}$", enviada.HuellaEnvio!);
        var (para, asunto) = Assert.Single(mail.Enviados);
        Assert.Contains("ana@alfa.com", para);
        Assert.Contains("subsecretaria@neuquen.gov.ar", para);
        Assert.Contains(enviada.NumeroComprobante!, asunto);
        var registro = await db.EnviosMail.SingleAsync();
        Assert.True(registro.Enviado);
        Assert.Equal("ana@alfa.com; subsecretaria@neuquen.gov.ar", registro.Destinatarios);
    }

    [Fact]
    public async Task El_mail_se_registra_a_quien_fue_de_verdad_sin_repetidos_y_avisa_si_el_modo_de_prueba_lo_desvio()
    {
        mail.Desviado = true;
        var p = await svc.IniciarAsync(Admin, BloqueAlfa, TipoPlan.Convencional);   // quien envía y quien inició es la misma persona
        await svc.GuardarCeldasAsync(p.Id, Admin, [new(2026, "pozos_perforados", 4), new(2026, "prod_petroleo", 1), new(2026, "prod_gas", 1)]);
        await svc.EnviarAsync(p.Id, Admin, "http://x");

        var registro = await db.EnviosMail.SingleAsync();
        Assert.Equal(1, registro.Destinatarios.Split(';').Count(x => x.Contains("admin@neuquen.gov.ar")));   // no repetido
        Assert.Contains("modo de prueba", registro.Destinatarios);
    }

    [Fact]
    public async Task Una_presentacion_enviada_no_se_modifica_y_se_corrige_con_una_rectificatoria()
    {
        var p = await BorradorDeAna();
        await CargarLoMinimo(p.Id);
        var v1 = await svc.EnviarAsync(p.Id, Ana, "http://x");

        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(v1.Id, Ana, [new(2026, "pozos_perforados", 9)]));
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.IniciarAsync(Ana, BloqueAlfa, TipoPlan.Convencional));   // ya enviada: hay que rectificar

        var v2 = await svc.RectificarAsync(v1.Id, Ana);
        Assert.Equal(2, v2.Version);
        Assert.Equal(EstadoPresentacion.Borrador, v2.Estado);
        Assert.Equal(4, v2.Valores.Count);                                         // copió lo de la v1
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.RectificarAsync(v1.Id, Ana));   // ya hay una en borrador

        await svc.GuardarCeldasAsync(v2.Id, Ana, [new(2026, "pozos_perforados", 6)]);
        var v2Enviada = await svc.EnviarAsync(v2.Id, Ana, "http://x");

        Assert.EndsWith("-v2", v2Enviada.NumeroComprobante);
        Assert.NotEqual(v1.HuellaEnvio, v2Enviada.HuellaEnvio);
        Assert.Equal(EstadoPresentacion.Reemplazada, (await db.Presentaciones.AsNoTracking().SingleAsync(x => x.Id == v1.Id)).Estado);
        Assert.Equal(4m, (await db.PlanValores.AsNoTracking().SingleAsync(x => x.PresentacionId == v1.Id && x.Campo == "pozos_perforados")).Valor);   // la v1 quedó intacta
    }

    [Fact]
    public async Task Rectificar_lo_pueden_la_empresa_duena_y_Administracion_pero_no_otra_empresa()
    {
        var p = await BorradorDeAna();
        await CargarLoMinimo(p.Id);
        var v1 = await svc.EnviarAsync(p.Id, Ana, "http://x");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.RectificarAsync(v1.Id, Beto));
        var porAdmin = await svc.RectificarAsync(v1.Id, Admin);        // Administración sí puede asistir
        Assert.Equal(2, porAdmin.Version);
        Assert.Equal("Admin", porAdmin.AsistidaPor);
    }

    [Fact]
    public async Task La_huella_cambia_si_cambia_cualquier_valor()
    {
        var p = await BorradorDeAna();
        await CargarLoMinimo(p.Id);
        var antes = PresentacionService.Huella(await svc.ObtenerAsync(p.Id, Ana));
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 12.0001m)]);
        Assert.NotEqual(antes, PresentacionService.Huella(await svc.ObtenerAsync(p.Id, Ana)));
    }
}
