namespace PlanAnual.Domain.Catalogo;

/// <summary>
/// Documentación de respaldo que se adjunta a cada área. En el nombre, {A} es el año de la convocatoria (el primer año del plan).
/// </summary>
public sealed record AnexoTipo(string Codigo, string Nombre, bool Obligatorio)
{
    /// <summary>El nombre con el año de la convocatoria puesto.</summary>
    public string Titulo(int anio) => Nombre.Replace("{A}", anio.ToString());
}

public static class AnexosRequeridos
{
    public static readonly IReadOnlyList<AnexoTipo> Todos = new List<AnexoTipo>
    {
        new("fundamento_flujo_fondos", "Fundamentación del flujo de fondos", true),
        new("flujograma", "Instalaciones: flujograma de instalaciones y transporte de hidrocarburos (capacidad de tratamiento y de almacenamiento, puntos de medición fiscal)", true),
        new("paradas_planta", "Paradas programadas de planta proyectadas para {A} por área (y pérdidas de producción asociadas)", true),
        new("presentacion_plan", "Presentación del Plan Anual de Inversiones {A}", true),
        new("otro", "Otros", false),
    };

    public static AnexoTipo? Buscar(string codigo) => Todos.FirstOrDefault(a => a.Codigo == codigo);
}
