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

    /// <summary>Los dos planes de actividad que puede tener un área, en el orden en que se muestran.</summary>
    public static readonly TipoPlan[] Actividades = [TipoPlan.Convencional, TipoPlan.Cench];

    /// <summary>Tipos de actividad que tienen al menos un valor cargado (un plan en 0 también cuenta: es una declaración).</summary>
    public IReadOnlyList<TipoPlan> TiposCargados() => Actividades.Where(t => Valores.Any(v => ActividadDe(v) == t && v.Valor.HasValue)).ToList();

    /// <summary>Plan de un valor; los que no lo traen (0) se toman del tipo principal de la presentación.</summary>
    public TipoPlan ActividadDe(PlanValor v) => v.Actividad == 0 ? TipoPlan : v.Actividad;

    /// <summary>
    /// Copia de solo lectura con únicamente los valores de un tipo de actividad y ese tipo como TipoPlan. Sirve para calcular, validar y
    /// mostrar cada plan por separado con la misma lógica de siempre. Nunca se guarda.
    /// </summary>
    public Presentacion ParaTipo(TipoPlan tipo)
    {
        var copia = (Presentacion)MemberwiseClone();
        copia.TipoPlan = tipo;
        copia.Valores = Valores.Where(v => ActividadDe(v) == tipo).ToList();
        return copia;
    }
}

/// <summary>
/// Una celda de la planilla: año + mes + código de campo del catálogo + valor. El año de la convocatoria se carga por mes
/// (Mes 1 a 12); los años siguientes son anuales (Mes 0).
/// </summary>
public class PlanValor
{
    public long PresentacionId { get; set; }
    /// <summary>Plan de actividad al que pertenece el valor: un área puede tener Convencional y No Convencional (CENCH) a la vez.</summary>
    public TipoPlan Actividad { get; set; }
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

/// <summary>
/// Un renglón del historial de modificaciones de un área (solo lo ve Administración). Se liga al área y a la convocatoria, no a una versión.
/// Guarda el valor anterior y el nuevo cuando es un dato puntual (expediente, textos, rango de años) y un resumen cuando son muchas celdas.
/// </summary>
public class HistorialArea
{
    public long Id { get; set; }
    public int CicloId { get; set; }
    public string Cuit { get; set; } = "";
    public int IdBloque { get; set; }
    public DateTime Fecha { get; set; }
    public string Usuario { get; set; } = "";
    /// <summary>Qué cambió: «Expediente GDE», «Valores · Plan Convencional», «Adjunto - …», «Presentación», etc.</summary>
    public string Campo { get; set; } = "";
    public string? Anterior { get; set; }
    public string? Nuevo { get; set; }
    /// <summary>Versión de la presentación a la que corresponde el cambio (si aplica).</summary>
    public int? Version { get; set; }
}

/// <summary>Tipo de movimiento en el ida y vuelta entre la Subsecretaría y una empresa.</summary>
public enum TipoComunicacion
{
    NotaEnviada = 1,         // la Subsecretaría le manda una nota o mail (pide información, avisa algo); puede tener plazo de respuesta
    RespuestaEmpresa = 2,    // la empresa contesta (recibida por GDE o mail y registrada acá)
    ProrrogaSolicitada = 3,  // la empresa pide más plazo
    ProrrogaOtorgada = 4,    // la Subsecretaría la concede: PlazoHasta es el nuevo límite de esa empresa
    Observado = 5,           // el plan tiene observaciones que hay que subsanar
    Rechazado = 6,           // el plan se rechaza; PlazoHasta es hasta cuándo puede volver a presentarlo
    Aprobado = 7,            // el plan se da por aprobado
}

/// <summary>
/// Un movimiento registrado entre la Subsecretaría y una empresa: nota o mail, respuesta, prórroga o decisión. Se ve en la línea de tiempo de la empresa.
/// Los envíos del plan y los pedidos de rectificación no se guardan acá: el sistema ya los tiene y se suman al mostrar la línea.
/// </summary>
public class Comunicacion
{
    public long Id { get; set; }
    public int CicloId { get; set; }
    public string Cuit { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    /// <summary>Si se refiere a un área en particular (opcional; por defecto es de toda la empresa).</summary>
    public int? IdBloque { get; set; }
    public string? NombreArea { get; set; }
    public DateTime Fecha { get; set; }
    public TipoComunicacion Tipo { get; set; }
    public string Asunto { get; set; } = "";
    public string? Detalle { get; set; }
    /// <summary>Número de nota, expediente o documento de GDE (referencia, sin consultarlo).</summary>
    public string? NumeroGde { get; set; }
    /// <summary>Plazo de respuesta (nota), nuevo límite (prórroga otorgada) o hasta cuándo puede volver a presentar (rechazo).</summary>
    public DateOnly? PlazoHasta { get; set; }
    /// <summary>Si es una respuesta o una prórroga otorgada: la nota o el pedido al que contesta.</summary>
    public long? RespondeA { get; set; }
    /// <summary>La empresa ve este movimiento en su pantalla (por defecto sí).</summary>
    public bool VisibleParaEmpresa { get; set; } = true;
    public int UsuarioId { get; set; }
    public string Usuario { get; set; } = "";

    public string? AdjuntoNombre { get; set; }
    public string? AdjuntoRuta { get; set; }
    public long? AdjuntoTamano { get; set; }
    public string? AdjuntoSha256 { get; set; }

    public bool MailEnviado { get; set; }
    public string? MailDestinatarios { get; set; }
    public string? MailError { get; set; }
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
