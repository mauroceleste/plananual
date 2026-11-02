using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PlanAnual.Infrastructure.Acceso;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

public class AccesoTests
{
    sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
    static readonly DateTimeOffset Hoy = new(2026, 10, 29, 15, 0, 0, TimeSpan.Zero);
    static string MariaCon(string inicio, string fin = "0001-01-01T00:00:00") => $"<IdSesion>77</IdSesion><IdUsuario>1234</IdUsuario><Usuario>mlopez</Usuario><Nombre>María</Nombre><Apellido>López</Apellido><Email>mlopez@empresa.com</Email><FechaInicio>{inicio}</FechaInicio><FechaFin>{fin}</FechaFin>";

    // Responde según la operación SOAP pedida, con el formato real del ASMX.
    sealed class FalsoUsersAccess(string usuario, string permisos, string empresas) : HttpMessageHandler
    {
        public List<string> Operaciones { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var op = r.Headers.GetValues("SOAPAction").Single().Trim('"').Split('/').Last();
            Operaciones.Add(op);
            var cuerpo = op switch
            {
                "obtainUserDataBySession" => $"<obtainUserDataBySessionResponse xmlns=\"http://tempuri.org/\"><obtainUserDataBySessionResult>{usuario}</obtainUserDataBySessionResult></obtainUserDataBySessionResponse>",
                "obtainModulosPermisosBySession" => $"<obtainModulosPermisosBySessionResponse xmlns=\"http://tempuri.org/\"><obtainModulosPermisosBySessionResult>{permisos}</obtainModulosPermisosBySessionResult></obtainModulosPermisosBySessionResponse>",
                _ => $"<ExchangeDataCompanyGetByIdUsuarioResponse xmlns=\"http://tempuri.org/\"><ExchangeDataCompanyGetByIdUsuarioResult>{empresas}</ExchangeDataCompanyGetByIdUsuarioResult></ExchangeDataCompanyGetByIdUsuarioResponse>",
            };
            var sobre = $"<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>{cuerpo}</soap:Body></soap:Envelope>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sobre, Encoding.UTF8, "text/xml") });
        }
    }

    const string Maria = "<IdSesion>77</IdSesion><IdUsuario>1234</IdUsuario><Usuario>mlopez</Usuario><Nombre>María</Nombre><Apellido>López</Apellido><Password>NO-DEBE-USARSE</Password><Email>mlopez@empresa.com</Email>";
    static string Permiso(int modulo, bool baja = false) => $"<Permisos><IdPermiso>1</IdPermiso><IdModulo>{modulo}</IdModulo><IdTipoPermiso>1</IdTipoPermiso><Baja>{baja.ToString().ToLower()}</Baja></Permisos>";
    static string Empresa(string cuit) => $"<ExchangeDataCompany><IdUsuario>1234</IdUsuario><IdEmpresa>{cuit}</IdEmpresa><Empresa>X</Empresa></ExchangeDataCompany>";

    static (AutenticadorUsersAccess, FalsoUsersAccess) Armar(string usuario, string permisos, string empresas, DateTimeOffset? ahora = null)
    {
        var falso = new FalsoUsersAccess(usuario, permisos, empresas);
        var cliente = new UsersAccessClient(new HttpClient(falso) { BaseAddress = new Uri("http://localhost/ws") });
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Acceso:IdSistema"] = "39", ["Acceso:ModuloOperacion"] = "119", ["Acceso:ModuloAdministracion"] = "118"
        }).Build();
        return (new AutenticadorUsersAccess(cliente, cfg, NullLogger<AutenticadorUsersAccess>.Instance, ahora is null ? null : new RelojFijo(ahora.Value)), falso);
    }

    [Fact]
    public async Task Modulo_118_es_administrador_y_ve_todo()
    {
        var (a, _) = Armar(Maria, Permiso(118), "");
        var r = await a.IngresarAsync(77);
        Assert.True(r.Ok);
        Assert.Equal(Rol.Administrador, r.Usuario!.Rol);
        Assert.True(r.Usuario.VeTodo);
        Assert.Equal("María López", r.Usuario.Nombre);
        Assert.Equal("mlopez@empresa.com", r.Usuario.Email);
    }

    [Fact]
    public async Task Modulo_119_es_empresa_con_sus_cuit()
    {
        var (a, _) = Armar(Maria, Permiso(119), Empresa("30-71173275-2") + Empresa("30711732752") + Empresa("33515950899"));
        var r = await a.IngresarAsync(77);
        Assert.Equal(Rol.Empresa, r.Usuario!.Rol);
        Assert.Equal(["30711732752", "33515950899"], r.Usuario.Cuits);   // normaliza guiones y descarta repetidos
        Assert.True(r.Usuario.PuedeEditar("33515950899"));
        Assert.False(r.Usuario.PuedeVer("30546689979"));
    }

    [Fact]
    public async Task Si_tiene_los_dos_modulos_gana_operacion_y_ve_solo_sus_empresas()
    {
        // Igual que en Registro de Empresas: ante la duda, el permiso más restrictivo.
        var (a, _) = Armar(Maria, Permiso(119) + Permiso(118), Empresa("30711732752"));
        var u = (await a.IngresarAsync(77)).Usuario!;
        Assert.Equal(Rol.Empresa, u.Rol);
        Assert.False(u.VeTodo);
        Assert.False(u.PuedeVer("30546689979"));
    }

    [Fact]
    public async Task Administracion_sin_operacion_ve_todo_y_puede_asistir_a_cualquier_empresa()
    {
        var (a, _) = Armar(Maria, Permiso(118), "");
        var u = (await a.IngresarAsync(77)).Usuario!;
        Assert.True(u.PuedeVer("30546689979"));
        Assert.True(u.PuedeEditar("30546689979"));
        Assert.True(u.EsAsistencia("30546689979"));
    }

    [Fact]
    public async Task La_empresa_edita_lo_suyo_y_no_es_una_asistencia()
    {
        var (a, _) = Armar(Maria, Permiso(119), Empresa("30711732752"));
        var u = (await a.IngresarAsync(77)).Usuario!;
        Assert.True(u.PuedeEditar("30711732752"));
        Assert.False(u.EsAsistencia("30711732752"));
        Assert.False(u.PuedeEditar("30546689979"));
    }

    [Fact]
    public void El_rol_de_consulta_ve_todo_pero_no_edita_ni_asiste()
    {
        var consulta = new UsuarioSesion(9, "Consulta", "c@x.com", [], Rol.Subsecretaria);
        Assert.True(consulta.PuedeVer("111"));
        Assert.False(consulta.PuedeEditar("111"));
        Assert.False(consulta.EsAsistencia("111"));
    }

    [Fact]
    public async Task Empresa_sin_empresa_asignada_no_ingresa()
    {
        var (a, _) = Armar(Maria, Permiso(119), "");
        var r = await a.IngresarAsync(77);
        Assert.False(r.Ok);
        Assert.Contains("empresa asignada", r.Motivo);
    }

    [Fact]
    public async Task Sin_permisos_en_el_sistema_no_ingresa()
    {
        var (a, _) = Armar(Maria, Permiso(500) + Permiso(66), Empresa("30711732752"));   // módulos de otros sistemas
        var r = await a.IngresarAsync(77);
        Assert.False(r.Ok);
        Assert.Contains("permisos", r.Motivo);
    }

    [Fact]
    public async Task Un_permiso_dado_de_baja_no_cuenta()
    {
        var (a, _) = Armar(Maria, Permiso(118, baja: true), "");
        Assert.False((await a.IngresarAsync(77)).Ok);
    }

    [Fact]
    public async Task Sesion_inexistente_no_ingresa_y_no_pide_permisos()
    {
        var (a, falso) = Armar("<IdUsuario>0</IdUsuario>", Permiso(118), "");
        var r = await a.IngresarAsync(999);
        Assert.False(r.Ok);
        Assert.DoesNotContain("obtainModulosPermisosBySession", falso.Operaciones);
    }

    // ---------- Antigüedad de la sesión (UsersAccess no la vence) ----------

    [Fact]
    public async Task Una_sesion_de_hace_anios_no_se_acepta_aunque_UsersAccess_siga_respondiendo()
    {
        var (a, falso) = Armar(MariaCon("2018-06-01T15:42:34"), Permiso(118), "", Hoy);
        var r = await a.IngresarAsync(227);
        Assert.False(r.Ok);
        Assert.Contains("antigua", r.Motivo);
        Assert.DoesNotContain("obtainModulosPermisosBySession", falso.Operaciones);   // ni siquiera pide permisos
    }

    [Fact]
    public async Task Una_sesion_cerrada_no_se_acepta()
    {
        var (a, _) = Armar(MariaCon("2026-10-29T10:00:00", "2026-10-29T12:00:00"), Permiso(118), "", Hoy);
        var r = await a.IngresarAsync(77);
        Assert.False(r.Ok);
        Assert.Contains("cerrada", r.Motivo);
    }

    [Fact]
    public async Task Una_sesion_reciente_ingresa()
    {
        var (a, _) = Armar(MariaCon("2026-10-29T09:30:00"), Permiso(118), "", Hoy);   // 5,5 horas
        Assert.True((await a.IngresarAsync(77)).Ok);
    }

    [Fact]
    public async Task Una_sesion_de_varios_dias_es_normal_en_el_portal_y_se_acepta()
    {
        // Caso real: la sesión de un administrador abierta 9 días antes seguía siendo la vigente en el portal.
        var (a, _) = Armar(MariaCon("2026-10-20T07:48:40"), Permiso(118), "", Hoy);
        Assert.True((await a.IngresarAsync(77)).Ok);
    }

    [Fact]
    public async Task El_limite_por_defecto_son_30_dias()
    {
        Assert.True((await Armar(MariaCon("2026-09-30T16:00:00"), Permiso(118), "", Hoy).Item1.IngresarAsync(77)).Ok);    // 29 días
        Assert.False((await Armar(MariaCon("2026-09-28T16:00:00"), Permiso(118), "", Hoy).Item1.IngresarAsync(77)).Ok);   // 31 días
    }

    [Fact]
    public async Task Si_el_servicio_no_informa_la_fecha_se_deja_pasar_para_no_trabar_a_todos()
    {
        var (a, _) = Armar(MariaCon("0001-01-01T00:00:00"), Permiso(118), "", Hoy);
        Assert.True((await a.IngresarAsync(77)).Ok);
    }
}
