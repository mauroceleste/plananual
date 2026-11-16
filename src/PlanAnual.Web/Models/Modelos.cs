using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Web.Models;

public sealed record FilaArea(BloqueInfo Bloque, TipoPlan? Tipo, Presentacion? Ultima, SolicitudRectificacion? Solicitud = null);

public sealed class MisAreasVm
{
    public Ciclo? Ciclo { get; set; }
    public List<FilaArea> Filas { get; set; } = [];
    /// <summary>Qué le falta a cada borrador (por id de presentación) para entrar en la presentación final.</summary>
    public Dictionary<long, List<string>> Pendientes { get; set; } = [];
    public string? ErrorServicio { get; set; }
    /// <summary>Si Administración está asistiendo a una empresa: su CUIT y razón social.</summary>
    public string? CuitAsistido { get; set; }
    public string? RazonAsistida { get; set; }
    /// <summary>Cantidad de observaciones internas por área (solo se carga para Administración).</summary>
    public Dictionary<int, int> Observaciones { get; set; } = [];
}

public enum TipoPaso { Grilla, Anexos, Texto, Revision }

/// <summary>Una fila de la grilla: un mes del primer año, el subtotal anual de ese año (Mes 0, EsSubtotal) o un año posterior.</summary>
public sealed record FilaPeriodo(int Anio, int Mes, bool EsSubtotal = false)
{
    public int Clave => Calculadora.Clave(Anio, Mes);
}

public sealed record PasoVm(int Indice, string Titulo, TipoPaso Tipo, List<CampoDef> Campos);

public sealed class EditarVm
{
    public required Presentacion P { get; init; }
    public required List<PasoVm> Pasos { get; init; }
    public required bool SoloLectura { get; init; }
    /// <summary>Filas calculadas por clave (año*100+mes; mes 0 = anual o subtotal anual).</summary>
    public required SortedDictionary<int, Dictionary<string, decimal>> Calc { get; init; }
    public required Dictionary<(int Anio, int Mes, string Campo), decimal> Ingresados { get; init; }
    public int PasoActual { get; set; }
    public List<FilaPeriodo> Filas { get; set; } = [];
    public List<Observacion> Observaciones { get; set; } = [];
    public PasoVm Actual => Pasos[PasoActual];

    public decimal? Valor(FilaPeriodo f, CampoDef c) =>
        c.EsCalculado || f.EsSubtotal
            ? (Calc.TryGetValue(f.Clave, out var fila) && fila.TryGetValue(c.Codigo, out var v) ? v : null)
            : (Ingresados.TryGetValue((f.Anio, f.Mes, c.Codigo), out var i) ? i : null);

    /// <summary>Solo se suman por columna los campos que son flujos del año (no capacidades, precios ni acumulados).</summary>
    public static bool Aditivo(CampoDef c) =>
        c.Unidad is "MMUSD" or "MMbbl" or "MMm3" ||
        c.Codigo is "pozos_perforados" or "pozos_conectados" or "fracturas" or "pozos_abandonar" or "act_pulling" or "act_workover";
}

public sealed class ComprobanteVm
{
    public required Presentacion P { get; init; }
    public required SortedDictionary<int, Dictionary<string, decimal>> Calc { get; init; }
    public EnvioMail? Mail { get; init; }
}

public sealed record EmpresaSeguimiento(string Cuit, string Razon, int Bloques, int Enviadas, int Borradores)
{
    public string Estado => Enviadas >= Bloques && Bloques > 0 ? "Completa" : Enviadas > 0 ? "Parcial" : Borradores > 0 ? "En carga" : "Sin presentar";
}

public sealed class SeguimientoVm
{
    public Ciclo? Ciclo { get; set; }
    public List<Ciclo> Convocatorias { get; set; } = [];
    /// <summary>Si la convocatoria que se mira es la abierta (solo en esa se puede asistir a una empresa).</summary>
    public bool EsVigente { get; set; }
    public List<EmpresaSeguimiento> Empresas { get; set; } = [];
    public List<Presentacion> Recientes { get; set; } = [];
    public string? ErrorServicio { get; set; }
}

/// <summary>Tipo fijado a mano (Tipo), el que informa Legajos (SegunLegajos) y el que rige (Vigente).</summary>
public sealed record FilaTipo(BloqueInfo Bloque, TipoPlan? Tipo, TipoPlan? SegunLegajos)
{
    public TipoPlan? Vigente => Tipo ?? SegunLegajos;
}

public sealed class TableroVm
{
    public Ciclo? Ciclo { get; set; }
    public List<Ciclo> Convocatorias { get; set; } = [];
    public DatosTablero? Datos { get; set; }
    public TipoPlan? Tipo { get; set; }
    public string? Cuit { get; set; }
    public int Anios { get; set; } = 10;
    public List<(string Cuit, string Razon)> Empresas { get; set; } = [];
    public string? ErrorServicio { get; set; }
}

public sealed class ImportacionVm
{
    public List<ErrorImportacion> Errores { get; set; } = [];
    public bool Truncado { get; set; }
    public List<string> Areas { get; set; } = [];
    public string? CuitAsistido { get; set; }
    public string? NombreArchivo { get; set; }
}

public sealed class CorreoVm
{
    public bool Habilitado { get; set; }
    public bool ModoPrueba { get; set; }
    public string? Host { get; set; }
    public int Puerto { get; set; }
    public bool Ssl { get; set; }
    public string? Desde { get; set; }
    public bool TieneUsuario { get; set; }
    public bool TieneClave { get; set; }
    public List<string> Subsecretaria { get; set; } = [];
    public List<string> ListaPrueba { get; set; } = [];
    public string? PublicOrigin { get; set; }
    public string MiMail { get; set; } = "";
}
