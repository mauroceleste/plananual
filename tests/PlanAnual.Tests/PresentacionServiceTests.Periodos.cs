using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

// Primer año mensual, planilla Excel de carga e importación.
public sealed partial class PresentacionServiceTests
{
    // ---------- Primer año mensual, años siguientes anuales ----------

    [Fact]
    public async Task El_anio_de_la_convocatoria_se_carga_por_mes_y_los_siguientes_por_anio()
    {
        var p = await BorradorDeAna();
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "pozos_perforados", 4)]));          // 2026 sin mes
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "pozos_perforados", 4, 13)]));     // mes inexistente
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(p.Id, Ana, [new(2027, "pozos_perforados", 4, 3)]));      // 2027 con mes
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "pozos_perforados", 4, 3), new(2027, "pozos_perforados", 9)]);
        Assert.Equal(2, (await db.PlanValores.Where(v => v.PresentacionId == p.Id).ToListAsync()).Count);
    }

    [Fact]
    public async Task Guardar_un_mes_devuelve_la_fila_del_mes_y_el_subtotal_anual()
    {
        var p = await BorradorDeAna();
        var filas = await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "inv_perforacion", 3, 1), new(2026, "inv_perforacion", 2, 2), new(2026, "precio_gas", 2, 1), new(2026, "precio_gas", 4, 2)]);

        Assert.Equal(3m, filas[202601]["inv_total"]);
        Assert.Equal(2m, filas[202602]["inv_total"]);
        Assert.Equal(5m, filas[202600]["inv_total"]);       // subtotal: suma de los meses
        Assert.Equal(3m, filas[202600]["precio_gas"]);      // subtotal: promedio de los precios
    }

    [Fact]
    public async Task El_rango_de_anios_empieza_siempre_en_el_anio_de_la_convocatoria()
    {
        var p = await BorradorDeAna();
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.CambiarRangoAsync(p.Id, Ana, 2025, 2030));
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.CambiarRangoAsync(p.Id, Ana, 2027, 2030));
        await svc.CambiarRangoAsync(p.Id, Ana, 2026, 2032);
        Assert.Equal(2032, (await svc.ObtenerAsync(p.Id, Ana)).AnioHasta);
    }

    [Fact]
    public async Task La_rectificatoria_copia_los_meses_y_los_textos_de_restricciones()
    {
        var p = await AreaCompleta(BloqueAlfa);
        await svc.GuardarRestriccionesAsync(p.Id, Ana, "Conflicto en el lote 4", "Dos equipos", "Hasta 2027", "USD 500 por día");
        var v1 = await svc.EnviarAsync(p.Id, Ana, "http://x");
        await Habilitar(v1.Id);

        var v2 = await svc.RectificarAsync(v1.Id, Ana);

        Assert.Equal(v1.Valores.Count, v2.Valores.Count);
        Assert.Contains(v2.Valores, x => x.Anio == 2026 && x.Mes == 12 && x.Campo == "prod_gas");
        Assert.Equal("Conflicto en el lote 4", v2.ConflictosSuperficiarios);
        Assert.Equal("USD 500 por día", v2.EquiposCostos);
    }

    [Fact]
    public async Task Los_textos_del_bloque_4_cuentan_en_la_huella_y_no_pasan_de_4000_caracteres()
    {
        var p = await BorradorDeAna();
        await CargarLoMinimo(p.Id);
        var antes = PresentacionService.Huella(await svc.ObtenerAsync(p.Id, Ana));
        await svc.GuardarRestriccionesAsync(p.Id, Ana, "Ninguno", null, null, null);
        Assert.NotEqual(antes, PresentacionService.Huella(await svc.ObtenerAsync(p.Id, Ana)));
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarRestriccionesAsync(p.Id, Ana, new string('x', 4001), null, null, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.GuardarRestriccionesAsync(p.Id, Beto, "x", null, null, null));
    }

    // ---------- Planilla Excel de carga e importación ----------

    static readonly Dictionary<int, TipoPlan> Tipos = new() { [10] = TipoPlan.Convencional, [11] = TipoPlan.Convencional };

    static MemoryStream Guardar(XLWorkbook wb) { var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0; return ms; }

    /// <summary>Baja la planilla de la empresa y completa todas las celdas de entrada con un valor.</summary>
    async Task<XLWorkbook> PlanillaCompleta(decimal valor = 1)
    {
        var bytes = await svc.PlantillaAsync(Ana, [BloqueAlfa, BloqueAlfa2], Tipos);
        var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(PlanillaExcel.NombreHoja(TipoPlan.Convencional));
        var entradas = ExportadorExcel.Columnas(TipoPlan.Convencional).Select((c, i) => (c, col: PlanillaExcel.PrimeraColCampo + i)).Where(x => !x.c.EsCalculado).ToList();
        for (int r = PlanillaExcel.PrimeraFila; r <= ws.LastRowUsed()!.RowNumber(); r++)
            foreach (var (c, col) in entradas)
                ws.Cell(r, col).Value = c.DecimalesMax == 0 ? Math.Round(valor) : valor;
        return wb;
    }

    static int ColDe(string codigo) => PlanillaExcel.PrimeraColCampo + ExportadorExcel.Columnas(TipoPlan.Convencional).ToList().FindIndex(c => c.Codigo == codigo);

    [Fact]
    public async Task La_planilla_trae_todas_las_areas_con_codigo_oculto_el_primer_anio_mensual_y_los_demas_anuales()
    {
        var bytes = await svc.PlantillaAsync(Ana, [BloqueAlfa, BloqueAlfa2], Tipos);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(PlanillaExcel.NombreHoja(TipoPlan.Convencional));

        Assert.True(ws.Column(PlanillaExcel.ColCodigo).IsHidden);
        Assert.True(ws.Row(PlanillaExcel.FilaCodigos).IsHidden);
        Assert.Equal("pozos_perforados", ws.Cell(PlanillaExcel.FilaCodigos, PlanillaExcel.PrimeraColCampo).GetString());
        Assert.Equal("Área Alfa", ws.Cell(5, PlanillaExcel.ColArea).GetString());
        Assert.Equal(10, ws.Cell(5, PlanillaExcel.ColCodigo).GetValue<int>());
        Assert.Equal(2026, ws.Cell(5, PlanillaExcel.ColAnio).GetValue<int>());
        Assert.Equal(1, ws.Cell(5, PlanillaExcel.ColMes).GetValue<int>());
        Assert.Equal(12, ws.Cell(16, PlanillaExcel.ColMes).GetValue<int>());
        Assert.Equal(2027, ws.Cell(17, PlanillaExcel.ColAnio).GetValue<int>());
        Assert.Equal("Anual", ws.Cell(17, PlanillaExcel.ColMes).GetString());
        Assert.Equal("Área Alfa Dos", ws.Cell(21, PlanillaExcel.ColArea).GetString());          // 12 meses + 4 años = 16 filas por área
        Assert.Equal(11, ws.Cell(21, PlanillaExcel.ColCodigo).GetValue<int>());
        Assert.True(ws.Cell(5, PlanillaExcel.PrimeraColCampo).IsEmpty());                         // vacía: la completa la empresa
        Assert.NotNull(wb.Worksheets.FirstOrDefault(w => w.Name == "Instrucciones"));
    }

    [Fact]
    public async Task La_planilla_deja_afuera_las_areas_sin_tipo_y_avisa_en_las_instrucciones()
    {
        var bytes = await svc.PlantillaAsync(Ana, [BloqueAlfa, BloqueAlfa2], new Dictionary<int, TipoPlan> { [10] = TipoPlan.Convencional });
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var texto = string.Join("\n", wb.Worksheet("Instrucciones").CellsUsed().Select(c => c.GetString()));
        Assert.Contains("ÁREAS SIN INCLUIR", texto);
        Assert.Contains("Área Alfa Dos", texto);
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.PlantillaAsync(Ana, [BloqueAlfa], new Dictionary<int, TipoPlan>()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.PlantillaAsync(Beto, [BloqueAlfa], Tipos));
    }

    [Fact]
    public async Task Importar_una_planilla_completa_crea_los_borradores_con_los_valores_y_el_rango()
    {
        using var wb = await PlanillaCompleta(2);

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Tipos, Guardar(wb));

        Assert.Empty(r.Errores);
        Assert.Equal(new[] { "Área Alfa", "Área Alfa Dos" }, r.Areas);
        var ps = await db.Presentaciones.Include(p => p.Valores).ToListAsync();
        Assert.Equal(2, ps.Count);
        Assert.All(ps, p => { Assert.Equal(EstadoPresentacion.Borrador, p.Estado); Assert.Equal(2026, p.AnioDesde); Assert.Equal(2030, p.AnioHasta); });
        var alfa = ps.Single(p => p.IdBloque == 10);
        Assert.Equal(12, alfa.Valores.Count(v => v.Anio == 2026 && v.Campo == "prod_gas" && v.Mes > 0));
        Assert.Single(alfa.Valores, v => v.Anio == 2028 && v.Campo == "prod_gas" && v.Mes == 0);
        Assert.Equal(24m, Calculadora.CalcularPlan(TipoPlan.Convencional, alfa.Valores)[2026]["prod_gas"]);   // 12 meses de 2, sumados
    }

    [Fact]
    public async Task Importar_otra_vez_reemplaza_lo_cargado_y_se_puede_seguir_en_el_formulario()
    {
        using (var w1 = await PlanillaCompleta(1)) await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Tipos, Guardar(w1));
        using var w2 = await PlanillaCompleta(5);

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Tipos, Guardar(w2));

        Assert.Empty(r.Errores);
        Assert.Equal(2, await db.Presentaciones.CountAsync());                                    // no duplicó las presentaciones
        Assert.All(await db.PlanValores.Where(v => v.Campo == "prod_gas").ToListAsync(), v => Assert.Equal(5m, v.Valor));
        var p = await db.Presentaciones.FirstAsync(x => x.IdBloque == 10);
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 7, 4)]);                   // el borrador importado se edita normalmente
    }

    [Fact]
    public async Task Una_celda_vacia_un_texto_o_un_negativo_bloquean_toda_la_importacion_y_se_informa_hoja_fila_y_columna()
    {
        using var wb = await PlanillaCompleta(1);
        var ws = wb.Worksheet(PlanillaExcel.NombreHoja(TipoPlan.Convencional));
        ws.Cell(6, ColDe("prod_gas")).Clear();                       // vacía
        ws.Cell(7, ColDe("prod_petroleo")).Value = "mucho";          // no es un número
        ws.Cell(8, ColDe("inv_perforacion")).Value = -3;             // negativo
        ws.Cell(9, ColDe("pozos_perforados")).Value = 2.5;           // entero con decimales

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Tipos, Guardar(wb));

        Assert.Equal(4, r.Errores.Count);
        Assert.All(r.Errores, e => Assert.Equal("Plan Convencional", e.Hoja));
        Assert.Contains(r.Errores, e => e.Fila == 6 && e.Columna!.StartsWith("Producción de gas") && e.Mensaje.Contains("celda vacía") && e.Mensaje.Contains("0"));
        Assert.Contains(r.Errores, e => e.Fila == 7 && e.Mensaje.Contains("no es un número"));
        Assert.Contains(r.Errores, e => e.Fila == 8 && e.Mensaje.Contains("negativos"));
        Assert.Contains(r.Errores, e => e.Fila == 9 && e.Mensaje.Contains("entero"));
        Assert.Empty(await db.Presentaciones.ToListAsync());       // todo o nada: no se creó ni un borrador
        Assert.Empty(await db.PlanValores.ToListAsync());
    }

    [Fact]
    public async Task Acepta_numeros_escritos_como_texto_con_coma_decimal()
    {
        using var wb = await PlanillaCompleta(1);
        var ws = wb.Worksheet(PlanillaExcel.NombreHoja(TipoPlan.Convencional));
        ws.Cell(5, ColDe("prod_gas")).Value = "12,5";
        ws.Cell(6, ColDe("prod_gas")).Value = "1.234,5";

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Tipos, Guardar(wb));

        Assert.Empty(r.Errores);
        var alfa = await db.Presentaciones.SingleAsync(p => p.IdBloque == 10);
        Assert.Equal(12.5m, (await db.PlanValores.SingleAsync(v => v.PresentacionId == alfa.Id && v.Campo == "prod_gas" && v.Anio == 2026 && v.Mes == 1)).Valor);
        Assert.Equal(1234.5m, (await db.PlanValores.SingleAsync(v => v.PresentacionId == alfa.Id && v.Campo == "prod_gas" && v.Anio == 2026 && v.Mes == 2)).Valor);
    }

    [Fact]
    public async Task Detecta_codigos_de_area_ajenos_meses_o_anios_faltantes_y_filas_repetidas()
    {
        using var wb = await PlanillaCompleta(1);
        var ws = wb.Worksheet(PlanillaExcel.NombreHoja(TipoPlan.Convencional));
        ws.Cell(5, PlanillaExcel.ColCodigo).Value = 999;                                  // área que no es de la empresa
        ws.Row(6).Delete();                                                               // falta un mes de la primera área
        ws.Cell(19, PlanillaExcel.ColAnio).Value = 2029;                                  // (tras borrar la fila 6) el 2030 del área pasa a 2029: repetido

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Tipos, Guardar(wb));

        Assert.Contains(r.Errores, e => e.Mensaje.Contains("999") && e.Mensaje.Contains("ninguna de las áreas"));
        Assert.Contains(r.Errores, e => e.Mensaje.Contains("faltan los meses"));
        Assert.Contains(r.Errores, e => e.Mensaje.Contains("repetido") || e.Mensaje.Contains("faltan los años"));
        Assert.Empty(await db.Presentaciones.ToListAsync());
    }

    [Fact]
    public async Task El_primer_anio_no_puede_ser_anual_ni_los_siguientes_mensuales()
    {
        using var wb = await PlanillaCompleta(1);
        var ws = wb.Worksheet(PlanillaExcel.NombreHoja(TipoPlan.Convencional));
        ws.Cell(5, PlanillaExcel.ColMes).Value = "Anual";   // enero de 2026 sin mes
        ws.Cell(17, PlanillaExcel.ColMes).Value = 3;        // 2027 con mes

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Tipos, Guardar(wb));

        Assert.Contains(r.Errores, e => e.Fila == 5 && e.Mensaje.Contains("mes por mes"));
        Assert.Contains(r.Errores, e => e.Fila == 17 && e.Mensaje.Contains("anuales"));
    }

    [Fact]
    public async Task No_se_importa_sobre_un_area_ya_enviada_ni_un_archivo_que_no_es_la_planilla()
    {
        var a = await AreaCompleta(BloqueAlfa);
        await svc.EnviarAsync(a.Id, Ana, "http://x");
        using var wb = await PlanillaCompleta(1);

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Tipos, Guardar(wb));
        Assert.Contains(r.Errores, e => e.Mensaje.Contains("Área Alfa") && e.Mensaje.Contains("ya fue enviada"));
        Assert.Equal(1, await db.Presentaciones.CountAsync());                                    // y tampoco se creó la otra

        var basura = await svc.ImportarAsync(Ana, [BloqueAlfa], Tipos, new MemoryStream([1, 2, 3, 4]));
        Assert.Contains(basura.Errores, e => e.Mensaje.Contains("No se pudo abrir"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ImportarAsync(Beto, [BloqueAlfa], Tipos, new MemoryStream()));
    }

    [Fact]
    public async Task Una_planilla_importada_completa_cumple_los_numeros_pero_igual_exige_anexos_y_restricciones_para_la_final()
    {
        using var wb = await PlanillaCompleta(1);
        await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Tipos, Guardar(wb));

        var r = await svc.EnviarFinalAsync(Ana, [BloqueAlfa, BloqueAlfa2], id => "http://x/" + id);

        Assert.Equal(0, r.Enviadas);
        Assert.Equal(2, r.Problemas.Count);
        Assert.All(r.Problemas, x => { Assert.Contains("Falta adjuntar", x); Assert.Contains("Restricciones a la actividad", x); });
    }
}
