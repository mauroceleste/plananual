namespace PlanAnual.Domain.Entities;

public enum TipoPlan { Convencional = 1, Cench = 2 }

public enum EstadoPresentacion { Borrador = 1, Enviada = 2, Reemplazada = 3 }

/// <summary>Convocatoria anual (ej. Plan de Inversiones 2026) con su plazo y prórroga.</summary>
public class Ciclo
{
    public int Id { get; set; }
    public int Anio { get; set; }
    public string Nombre { get; set; } = "";
    public DateOnly FechaLimite { get; set; }
    public DateOnly? FechaProrroga { get; set; }
    public bool Abierto { get; set; } = true;
    public string? NotaGde { get; set; }

    public DateOnly LimiteVigente => FechaProrroga ?? FechaLimite;
}

/// <summary>
/// Tipo de actividad por bloque. Tabla local provisoria hasta que Legajos de Áreas
/// exponga el dato (se reemplaza por un proveedor del WS sin tocar el resto).
/// </summary>
public class BloqueTipoActividad
{
    public int IdBloque { get; set; }
    public string NombreArea { get; set; } = "";
    public string NombreBloque { get; set; } = "";
    public TipoPlan TipoPlan { get; set; }
}

/// <summary>Planilla de un bloque para un ciclo. Cada envío es una versión nueva.</summary>
public class Presentacion
{
    public long Id { get; set; }
    public int CicloId { get; set; }
    public Ciclo? Ciclo { get; set; }

    public string Cuit { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public int IdBloque { get; set; }
    public string NombreArea { get; set; } = "";
    public string NombreBloque { get; set; } = "";
    public TipoPlan TipoPlan { get; set; }

    /// <summary>Años que cubre la planilla (de 2026 hasta el vencimiento de la concesión).</summary>
    public int AnioDesde { get; set; }
    public int AnioHasta { get; set; }

    public int Version { get; set; } = 1;
    public EstadoPresentacion Estado { get; set; } = EstadoPresentacion.Borrador;

    public int UsuarioId { get; set; }
    public string UsuarioNombre { get; set; } = "";
    public string UsuarioEmail { get; set; } = "";
    /// <summary>Quién hizo el envío (puede ser Administración en nombre de la empresa).</summary>
    public string? EnviadaPor { get; set; }
    /// <summary>Nombre de la persona de Administración que cargó o modificó en nombre de la empresa; null si la hizo solo la empresa.</summary>
    public string? AsistidaPor { get; set; }
    public DateTime CreadaEn { get; set; }
    public DateTime ModificadaEn { get; set; }
    public DateTime? EnviadaEn { get; set; }

    /// <summary>Comprobante visible para la empresa, ej. PAI-2026-000123-v1.</summary>
    public string? NumeroComprobante { get; set; }
    /// <summary>Huella SHA-256 de los valores al enviar: prueba que no cambió después.</summary>
    public string? HuellaEnvio { get; set; }
    public string? Observaciones { get; set; }

    // Bloque 4 de la nota: restricciones a la actividad (texto, hasta que se defina una planilla modelo).
    public string? ConflictosSuperficiarios { get; set; }
    public string? EquiposContratosFirmes { get; set; }
    public string? EquiposPlazos { get; set; }
    public string? EquiposCostos { get; set; }
    /// <summary>Número de expediente de GDE, lo carga la Subsecretaría (dato interno: no lo ve la empresa).</summary>
    public string? ExpedienteGde { get; set; }

    public List<PlanValor> Valores { get; set; } = new();
    public List<Anexo> Anexos { get; set; } = new();
}

/// <summary>
/// Una celda de la planilla: año + mes + código de campo del catálogo + valor. El año de la convocatoria se carga por mes
/// (Mes 1 a 12); los años siguientes son anuales (Mes 0).
/// </summary>
public class PlanValor
{
    public long PresentacionId { get; set; }
    public int Anio { get; set; }
    /// <summary>0 = valor anual; 1 a 12 = mes (solo en el primer año de la planilla).</summary>
    public int Mes { get; set; }
    public string Campo { get; set; } = "";
    public decimal? Valor { get; set; }
}

/// <summary>Anexos que pide la nota (flujograma, paradas de planta, abandono, etc.).</summary>
public class Anexo
{
    public long Id { get; set; }
    public long PresentacionId { get; set; }
    public string Tipo { get; set; } = "";
    public string NombreArchivo { get; set; } = "";
    public string RutaAlmacenada { get; set; } = "";
    public long Tamano { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime SubidoEn { get; set; }
}

public enum EstadoSolicitud { Pendiente = 1, Habilitada = 2, Rechazada = 3, Usada = 4 }

/// <summary>
/// Pedido de una empresa para rectificar un área ya enviada. Administración lo habilita por un plazo
/// (7 días) o lo rechaza; la rectificatoria solo se puede crear, editar y enviar mientras la habilitación esté vigente.
/// </summary>
public class SolicitudRectificacion
{
    public const int DiasDeHabilitacion = 7;

    public long Id { get; set; }
    public int CicloId { get; set; }
    public string Cuit { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public int IdBloque { get; set; }
    public string NombreArea { get; set; } = "";
    /// <summary>Versión enviada que la empresa quiere corregir.</summary>
    public long PresentacionId { get; set; }
    public string Motivo { get; set; } = "";
    public string SolicitadaPor { get; set; } = "";
    public string SolicitadaEmail { get; set; } = "";
    public DateTime SolicitadaEn { get; set; }
    public EstadoSolicitud Estado { get; set; } = EstadoSolicitud.Pendiente;
    public string? ResueltaPor { get; set; }
    public DateTime? ResueltaEn { get; set; }
    public DateTime? HabilitadaHasta { get; set; }

    public bool Vigente(DateTime ahora) => Estado == EstadoSolicitud.Habilitada && HabilitadaHasta > ahora;
}

/// <summary>
/// Observación interna de la Subsecretaría sobre un área (no la ve la empresa). Se liga al área y a la convocatoria, no a una
/// versión: sigue ahí cuando la empresa rectifica.
/// </summary>
public class ObservacionInterna
{
    public const int MaximoTexto = 2000;

    public long Id { get; set; }
    public int CicloId { get; set; }
    public string Cuit { get; set; } = "";
    public int IdBloque { get; set; }
    public string Texto { get; set; } = "";
    public int UsuarioId { get; set; }
    public string UsuarioNombre { get; set; } = "";
    public DateTime CreadaEn { get; set; }
}

public class EnvioMail
{
    public long Id { get; set; }
    public long PresentacionId { get; set; }
    public string Destinatarios { get; set; } = "";
    public string Asunto { get; set; } = "";
    public bool Enviado { get; set; }
    public string? Error { get; set; }
    public DateTime Fecha { get; set; }
}
