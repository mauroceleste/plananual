namespace PlanAnual.Domain.Catalogo;

/// <summary>Documentación de respaldo que pide la nota (apartado "Alcance de la información solicitada").</summary>
public sealed record AnexoTipo(string Codigo, string Nombre, bool Obligatorio);

public static class AnexosRequeridos
{
    public static readonly IReadOnlyList<AnexoTipo> Todos = new List<AnexoTipo>
    {
        new("resumen_2025", "Resumen de actividades 2025 y principales resultados", true),
        new("flujograma", "Flujograma de instalaciones y transporte (capacidades, almacenamiento, medición fiscal)", true),
        new("factor_recuperacion", "Factor de recuperación y declinación por reservorio convencional", false),
        new("esfuerzo_exploratorio", "Conclusiones del esfuerzo exploratorio y estado de compromisos", false),
        new("tramites_vigentes", "Estado de los trámites vigentes (solicitudes)", false),
        new("paradas_planta", "Paradas programadas de planta 2026 y pérdidas de producción asociadas", true),
        new("mantenimiento", "Plan de mantenimiento de instalaciones de superficie y ductos", false),
        new("obras_nuevas", "Cronograma de obra de nuevas instalaciones", false),
        new("cronograma_pozos", "Cronograma mensual 2026 de pozos a perforar, terminar y conectar (con coordenadas)", true),
        new("intervenciones", "Cronograma de intervenciones 2026 (pozos seleccionados)", false),
        new("abandonos", "Programa de abandono de pozos", true),
        new("pronostico_mensual", "Pronóstico mensual de producción de gas y petróleo 2026, 2027 y 2028", true),
        new("empleo", "Empleo propio y tercerizado: situación y perspectivas", false),
        new("incentivos_plan_gas", "Programas de incentivo y estado en el Plan Gas.Ar", false),
        new("comercio_exterior", "Comercio exterior: volúmenes, precios y restricciones", false),
        new("restricciones", "Restricciones a la actividad y conflictos con superficiarios", false),
        new("equipos", "Disponibilidad de equipos: contratos firmes, plazos y costos", false),
        new("otro", "Otra documentación de respaldo", false),
    };

    public static AnexoTipo? Buscar(string codigo) => Todos.FirstOrDefault(a => a.Codigo == codigo);
}
