using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Legajos;

namespace PlanAnual.Web.Models;

public sealed record FilaArea(BloqueInfo Bloque, TipoPlan? Tipo, Presentacion? Ultima);

public sealed class MisAreasVm
{
    public Ciclo? Ciclo { get; set; }
    public List<FilaArea> Filas { get; set; } = [];
    public string? ErrorServicio { get; set; }
    /// <summary>Si Administración está asistiendo a una empresa: su CUIT y razón social.</summary>
    public string? CuitAsistido { get; set; }
    public string? RazonAsistida { get; set; }
}

public enum TipoPaso { Grilla, Anexos, Revision }

public sealed record PasoVm(int Indice, string Titulo, TipoPaso Tipo, List<CampoDef> Campos);

public sealed class EditarVm
{
    public required Presentacion P { get; init; }
    public required List<PasoVm> Pasos { get; init; }
    public required bool SoloLectura { get; init; }
    public required SortedDictionary<int, Dictionary<string, decimal>> Calc { get; init; }
    public required Dictionary<(int Anio, string Campo), decimal> Ingresados { get; init; }
    public int PasoActual { get; set; }
    public List<int> Anios { get; set; } = [];
    public List<Observacion> Observaciones { get; set; } = [];
    public PasoVm Actual => Pasos[PasoActual];

    public decimal? Valor(int anio, CampoDef c) =>
        c.EsCalculado
            ? (Calc.TryGetValue(anio, out var f) && f.TryGetValue(c.Codigo, out var v) ? v : null)
            : (Ingresados.TryGetValue((anio, c.Codigo), out var i) ? i : null);

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
    public DatosTablero? Datos { get; set; }
    public TipoPlan? Tipo { get; set; }
    public string? Cuit { get; set; }
    public int Anios { get; set; } = 10;
    public List<(string Cuit, string Razon)> Empresas { get; set; } = [];
    public string? ErrorServicio { get; set; }
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
