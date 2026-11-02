using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Infrastructure.Acceso;

public sealed record ResultadoIngreso(UsuarioSesion? Usuario, string? Motivo)
{
    public bool Ok => Usuario is not null;
}

/// <summary>
/// Convierte una sesión del portal (IdSesion) en un UsuarioSesion del sistema.
/// Config "Acceso": IdSistema (39), ModuloOperacion (119, empresas), ModuloAdministracion (118, Subsecretaría).
/// </summary>
public sealed class AutenticadorUsersAccess(IUsersAccessClient cliente, IConfiguration config, ILogger<AutenticadorUsersAccess> log, TimeProvider? reloj = null)
{
    public async Task<ResultadoIngreso> IngresarAsync(int idSesion, CancellationToken ct = default)
    {
        var a = config.GetSection("Acceso");
        int idSistema = a.GetValue<int>("IdSistema"), modOperacion = a.GetValue<int>("ModuloOperacion"), modAdmin = a.GetValue<int>("ModuloAdministracion");

        var datos = await cliente.UsuarioPorSesionAsync(idSesion, ct);
        if (datos is null) return new(null, "La sesión del portal no existe o ya venció. Volvé a ingresar desde el portal.");

        // UsersAccess no vence las sesiones (los números son chicos y consecutivos, y siguen resolviendo años después),
        // así que este sistema pone su propio límite: no se acepta una sesión cerrada ni una abierta hace más de N horas (por defecto 30 días: las sesiones reales del portal duran días).
        var ahora = (reloj ?? TimeProvider.System).GetLocalNow().DateTime;
        var vigenciaHoras = a.GetValue("VigenciaSesionHoras", 720);
        if (datos.Fin is { } fin && fin <= ahora)
        {
            log.LogWarning("Sesión {IdSesion} rechazada: ya estaba cerrada ({Fin}).", idSesion, fin);
            return new(null, "La sesión del portal ya fue cerrada. Volvé a ingresar desde el portal.");
        }
        if (datos.Inicio is { } inicio && vigenciaHoras > 0 && ahora - inicio > TimeSpan.FromHours(vigenciaHoras))
        {
            log.LogWarning("Sesión {IdSesion} rechazada: abierta hace más de {Horas} horas ({Inicio}).", idSesion, vigenciaHoras, inicio);
            return new(null, "La sesión del portal es muy antigua. Volvé a ingresar desde el portal para continuar.");
        }
        if (datos.Inicio is null) log.LogWarning("La sesión {IdSesion} no informa fecha de inicio: no se puede controlar su antigüedad.", idSesion);

        var permisos = await cliente.PermisosAsync(idSesion, idSistema, ct);
        var vigentes = permisos.Where(p => !p.Baja).Select(p => p.IdModulo).ToHashSet();
        var nombre = $"{datos.Nombre} {datos.Apellido}".Trim();
        if (nombre.Length == 0) nombre = datos.Usuario;

        // Mismo criterio que Registro de Empresas: quien tiene Operación ve solo sus empresas, aunque también tenga Administración.
        if (vigentes.Contains(modOperacion))
        {
            var cuits = await cliente.CuitsDeEmpresasAsync(datos.IdUsuario, ct);
            if (cuits.Count == 0)
            {
                log.LogWarning("El usuario {IdUsuario} tiene permiso de operación pero ninguna empresa asignada.", datos.IdUsuario);
                return new(null, "Tu usuario no tiene una empresa asignada. Pedile a la Subsecretaría que la asocie.");
            }
            return new(new UsuarioSesion(datos.IdUsuario, nombre, datos.Email, cuits, Rol.Empresa), null);
        }

        if (vigentes.Contains(modAdmin))
            return new(new UsuarioSesion(datos.IdUsuario, nombre, datos.Email, [], Rol.Administrador), null);

        log.LogWarning("El usuario {IdUsuario} ingresó sin permisos en el sistema {IdSistema}.", datos.IdUsuario, idSistema);
        return new(null, "Tu usuario no tiene permisos para este sistema.");
    }
}
