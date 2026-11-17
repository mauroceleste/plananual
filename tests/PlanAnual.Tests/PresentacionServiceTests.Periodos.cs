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

    // ---------- Planilla Excel unificada (Convencional y No Convencional) e importación ----------

    static MemoryStream Guardar(XLWorkbook wb) { var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0; return ms; }

    const string Hoja = PlanillaExcel.NombreHoja;
    static readonly int FilasPorBloque = 12 + PlanillaExcel.AniosAnualesPorDefecto;
    static readonly IReadOnlyList<CampoDef> Unificadas = ExportadorExcel.ColumnasUnificadas();

    static int ColDe(string codigo) => PlanillaExcel.PrimeraColCampo + Unificadas.ToList().FindIndex(c => c.Codigo == codigo);
    /// <summary>Primera fila de un bloque: el área (0 = la primera) y su tipo de actividad.</summary>
    static int FilaInicio(int area, TipoPlan tipo) => PlanillaExcel.PrimeraFila + area * 2 * FilasPorBloque + (tipo == TipoPlan.Cench ? FilasPorBloque : 0);

    /// <summary>Baja la planilla de la empresa y la completa: Sí/No en cada bloque y, en los que tienen actividad, todas las celdas de carga con un valor (hasta 2030).</summary>
    async Task<XLWorkbook> PlanillaCompleta(decimal valor = 1, bool convencional = true, bool noConvencional = false, decimal? valorNc = null)
    {
        var bytes = await svc.PlantillaAsync(Ana, [BloqueAlfa, BloqueAlfa2]);
        var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(Hoja);
        for (int area = 0; area < 2; area++)
            foreach (var (tipo, tiene) in new[] { (TipoPlan.Convencional, convencional), (TipoPlan.Cench, noConvencional) })
            {
                var inicio = FilaInicio(area, tipo);
                ws.Cell(inicio, PlanillaExcel.ColActividad).Value = tiene ? "Sí" : "No";
                if (!tiene) continue;
                var v = tipo == TipoPlan.Cench ? valorNc ?? valor : valor;
                var entradas = CatalogoCampos.Para(tipo).Where(c => !c.EsCalculado).ToList();
                for (int r = inicio; r < inicio + FilasPorBloque; r++)
                {
                    if (ws.Cell(r, PlanillaExcel.ColAnio).GetValue<int>() > 2030) continue;    // la planilla trae años de más: se usan hasta 2030 y el resto queda en blanco
                    foreach (var c in entradas) ws.Cell(r, ColDe(c.Codigo)).Value = c.DecimalesMax == 0 ? Math.Round(v) : v;
                }
            }
        return wb;
    }

    [Fact]
    public async Task La_planilla_trae_una_hoja_con_dos_bloques_por_area_el_tipo_fijo_y_el_primer_anio_mensual()
    {
        var bytes = await svc.PlantillaAsync(Ana, [BloqueAlfa, BloqueAlfa2]);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(Hoja);

        Assert.Equal(new[] { "Instrucciones", Hoja }, wb.Worksheets.Select(w => w.Name));
        Assert.True(ws.Column(PlanillaExcel.ColCodigo).IsHidden);
        Assert.True(ws.Row(PlanillaExcel.FilaCodigos).IsHidden);
        Assert.Equal("tipo_actividad", ws.Cell(PlanillaExcel.FilaCodigos, PlanillaExcel.ColTipo).GetString());
        Assert.Equal("Tipo de Actividad", ws.Cell(2, PlanillaExcel.ColTipo).GetString());
        Assert.Equal("pozos_perforados", ws.Cell(PlanillaExcel.FilaCodigos, PlanillaExcel.PrimeraColCampo).GetString());

        // Primer bloque: Convencional de la primera área; el segundo, su No Convencional; después, la segunda área.
        Assert.Equal("Área Alfa", ws.Cell(5, PlanillaExcel.ColArea).GetString());
        Assert.Equal(10, ws.Cell(5, PlanillaExcel.ColCodigo).GetValue<int>());
        Assert.Equal("Convencional", ws.Cell(5, PlanillaExcel.ColTipo).GetString());
        Assert.Equal(1, ws.Cell(5, PlanillaExcel.ColMes).GetValue<int>());
        Assert.Equal(12, ws.Cell(16, PlanillaExcel.ColMes).GetValue<int>());
        Assert.Equal(2027, ws.Cell(17, PlanillaExcel.ColAnio).GetValue<int>());
        Assert.Equal("Anual", ws.Cell(17, PlanillaExcel.ColMes).GetString());
        Assert.Equal("No Convencional", ws.Cell(FilaInicio(0, TipoPlan.Cench), PlanillaExcel.ColTipo).GetString());
        Assert.Equal("Área Alfa", ws.Cell(FilaInicio(0, TipoPlan.Cench), PlanillaExcel.ColArea).GetString());
        Assert.Equal("Área Alfa Dos", ws.Cell(FilaInicio(1, TipoPlan.Convencional), PlanillaExcel.ColArea).GetString());
        Assert.Equal(11, ws.Cell(FilaInicio(1, TipoPlan.Convencional), PlanillaExcel.ColCodigo).GetValue<int>());
        Assert.Equal(2026 + PlanillaExcel.AniosAnualesPorDefecto, ws.Cell(FilaInicio(0, TipoPlan.Cench) - 1, PlanillaExcel.ColAnio).GetValue<int>());
        Assert.True(ws.Cell(5, PlanillaExcel.PrimeraColCampo).IsEmpty());                         // vacía: la completa la empresa
    }

    [Fact]
    public async Task La_planilla_esta_protegida_el_tipo_es_fijo_y_las_columnas_que_no_corresponden_estan_en_gris_y_bloqueadas()
    {
        var bytes = await svc.PlantillaAsync(Ana, [BloqueAlfa, BloqueAlfa2]);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(Hoja);
        Assert.True(ws.IsProtected);
        Assert.True(wb.Worksheet("Instrucciones").IsProtected);

        foreach (var tipo in Presentacion.Actividades)
        {
            var inicio = FilaInicio(0, tipo);
            foreach (var r in new[] { inicio, inicio + 11, inicio + 12, inicio + FilasPorBloque - 1 })
            {
                foreach (var c in new[] { PlanillaExcel.ColArea, PlanillaExcel.ColCodigo, PlanillaExcel.ColAnio, PlanillaExcel.ColMes, PlanillaExcel.ColTipo })
                    Assert.True(ws.Cell(r, c).Style.Protection.Locked, $"fila {r}, columna {c}: tiene que estar bloqueada");
                for (int i = 0; i < Unificadas.Count; i++)
                {
                    var def = CatalogoCampos.Buscar(tipo, Unificadas[i].Codigo);
                    var celda = ws.Cell(r, PlanillaExcel.PrimeraColCampo + i);
                    // De carga: libre. Calculada o que no corresponde a ese tipo: bloqueada.
                    Assert.Equal(def is null || def.EsCalculado, celda.Style.Protection.Locked);
                    if (def is null) Assert.Equal(XLColor.FromHtml("#9AA5B1"), celda.Style.Fill.BackgroundColor);
                }
            }
            // ¿Tiene actividad? solo se contesta en la primera fila del bloque.
            Assert.False(ws.Cell(inicio, PlanillaExcel.ColActividad).Style.Protection.Locked);
            Assert.True(ws.Cell(inicio + 1, PlanillaExcel.ColActividad).Style.Protection.Locked);
        }
        // Lo que es de un solo tipo: Fijo/Variables son de No Convencional; Salarios y Abandono, de Convencional. Y Desarrollo cambia según el tipo.
        Assert.True(ws.Cell(5, ColDe("gto_fijo")).Style.Protection.Locked);
        Assert.False(ws.Cell(FilaInicio(0, TipoPlan.Cench), ColDe("gto_fijo")).Style.Protection.Locked);
        Assert.False(ws.Cell(5, ColDe("gto_salarios")).Style.Protection.Locked);
        Assert.True(ws.Cell(FilaInicio(0, TipoPlan.Cench), ColDe("gto_salarios")).Style.Protection.Locked);
        Assert.True(ws.Cell(5, ColDe("inv_desarrollo")).HasFormula);                                // Convencional: suma de sus cuatro componentes
        Assert.False(ws.Cell(FilaInicio(0, TipoPlan.Cench), ColDe("inv_desarrollo")).HasFormula);   // No Convencional: se carga
        // Canon y Renta extraordinarios son de carga en los dos tipos.
        foreach (var cod in new[] { "gto_canon_extra", "gto_renta_extra" })
            foreach (var tipo in Presentacion.Actividades)
                Assert.False(ws.Cell(FilaInicio(0, tipo), ColDe(cod)).Style.Protection.Locked);
    }

    [Fact]
    public async Task Los_totales_de_la_planilla_restan_el_canon_y_la_renta_extraordinarios_del_flujo_de_fondos()
    {
        using var wb = await PlanillaCompleta(1, convencional: true, noConvencional: true);
        var ws = wb.Worksheet(Hoja);
        foreach (var tipo in Presentacion.Actividades)
        {
            var calc = CatalogoCampos.Buscar(tipo, "gto_total")!;
            Assert.Contains("gto_canon_extra", calc.Suma!); Assert.Contains("gto_renta_extra", calc.Suma!);
            Assert.Contains("gto_total", CatalogoCampos.Buscar(tipo, "flujo_fondos")!.Resta!);   // el total de gastos (con canon y renta) se resta del flujo
        }
        // Y en el Excel: la fórmula del flujo de fondos de una fila Convencional resta las dos columnas nuevas.
        var formula = ws.Cell(5, ColDe("flujo_fondos")).FormulaA1;
        Assert.Contains(XLHelper.GetColumnLetterFromNumber(ColDe("gto_canon_extra")) + "5", formula);
        Assert.Contains(XLHelper.GetColumnLetterFromNumber(ColDe("gto_renta_extra")) + "5", formula);
    }

    [Fact]
    public async Task Importar_con_actividad_convencional_y_sin_no_convencional_carga_ceros_en_el_otro_plan()
    {
        using var wb = await PlanillaCompleta(2);                              // Convencional Sí (2 en todo); No Convencional: No

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        Assert.Empty(r.Errores);
        Assert.Equal(new[] { "Área Alfa", "Área Alfa Dos" }, r.Areas);
        var ps = await db.Presentaciones.Include(p => p.Valores).ToListAsync();
        Assert.Equal(2, ps.Count);
        Assert.All(ps, p => { Assert.Equal(EstadoPresentacion.Borrador, p.Estado); Assert.Equal(2026, p.AnioDesde); Assert.Equal(2030, p.AnioHasta); Assert.Equal(TipoPlan.Convencional, p.TipoPlan); });
        var alfa = ps.Single(p => p.IdBloque == 10);
        Assert.Equal(new[] { TipoPlan.Convencional, TipoPlan.Cench }, alfa.TiposCargados());

        var conv = alfa.Valores.Where(v => v.Actividad == TipoPlan.Convencional).ToList();
        Assert.Equal(12, conv.Count(v => v.Anio == 2026 && v.Campo == "prod_gas" && v.Mes > 0));
        Assert.Equal(24m, Calculadora.CalcularPlan(TipoPlan.Convencional, conv)[2026]["prod_gas"]);   // 12 meses de 2, sumados

        // «No»: todo en 0 en el plan No Convencional, con los mismos años que el Convencional.
        var nc = alfa.Valores.Where(v => v.Actividad == TipoPlan.Cench).ToList();
        Assert.All(nc, v => Assert.Equal(0m, v.Valor));
        Assert.Equal(12, nc.Count(v => v.Anio == 2026 && v.Campo == "prod_gas" && v.Mes > 0));
        Assert.Single(nc, v => v.Anio == 2030 && v.Campo == "prod_gas" && v.Mes == 0);
        Assert.Equal(0m, Calculadora.CalcularPlan(TipoPlan.Cench, nc)[2026]["prod_gas"]);
        Assert.DoesNotContain(nc, v => v.Campo is "gto_salarios" or "abandono_usd");                   // lo que no existe en No Convencional no se carga
    }

    [Fact]
    public async Task Importar_con_los_dos_tipos_con_actividad_guarda_cada_plan_por_separado()
    {
        using var wb = await PlanillaCompleta(valor: 1, convencional: true, noConvencional: true, valorNc: 3);

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        Assert.Empty(r.Errores);
        var alfa = await db.Presentaciones.Include(p => p.Valores).SingleAsync(p => p.IdBloque == 10);
        var conv = alfa.Valores.Where(v => v.Actividad == TipoPlan.Convencional).ToList();
        var nc = alfa.Valores.Where(v => v.Actividad == TipoPlan.Cench).ToList();
        Assert.Equal(12m, Calculadora.CalcularPlan(TipoPlan.Convencional, conv)[2026]["prod_gas"]);
        Assert.Equal(36m, Calculadora.CalcularPlan(TipoPlan.Cench, nc)[2026]["prod_gas"]);
        Assert.Contains(nc, v => v.Campo == "gto_fijo"); Assert.DoesNotContain(conv, v => v.Campo == "gto_fijo");
        Assert.Contains(conv, v => v.Campo == "gto_salarios"); Assert.DoesNotContain(nc, v => v.Campo == "gto_salarios");
        Assert.Contains(conv, v => v.Campo == "gto_canon_extra"); Assert.Contains(nc, v => v.Campo == "gto_renta_extra");
        // Al validar, cada plan se controla por separado y no falta declarar ninguno.
        Assert.DoesNotContain(Validador.Validar(alfa, []), o => o.Mensaje.Contains("sin actividad"));
    }

    [Fact]
    public async Task Importar_otra_vez_reemplaza_lo_cargado_y_se_puede_seguir_en_el_formulario()
    {
        using (var w1 = await PlanillaCompleta(1)) await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(w1));
        using var w2 = await PlanillaCompleta(5);

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(w2));

        Assert.Empty(r.Errores);
        Assert.Equal(2, await db.Presentaciones.CountAsync());                                    // no duplicó las presentaciones
        Assert.All(await db.PlanValores.Where(v => v.Campo == "prod_gas" && v.Actividad == TipoPlan.Convencional).ToListAsync(), v => Assert.Equal(5m, v.Valor));
        Assert.All(await db.PlanValores.Where(v => v.Actividad == TipoPlan.Cench).ToListAsync(), v => Assert.Equal(0m, v.Valor));
        var p = await db.Presentaciones.FirstAsync(x => x.IdBloque == 10);
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 7, 4)]);                   // el borrador importado se edita normalmente
    }

    [Fact]
    public async Task Una_celda_vacia_un_texto_o_un_negativo_bloquean_toda_la_importacion_y_se_informa_hoja_fila_y_columna()
    {
        using var wb = await PlanillaCompleta(1);
        var ws = wb.Worksheet(Hoja);
        ws.Cell(6, ColDe("prod_gas")).Clear();                       // vacía
        ws.Cell(7, ColDe("prod_petroleo")).Value = "mucho";          // no es un número
        ws.Cell(8, ColDe("inv_perforacion")).Value = -3;             // negativo
        ws.Cell(9, ColDe("pozos_perforados")).Value = 2.5;           // entero con decimales

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        Assert.Equal(4, r.Errores.Count);
        Assert.All(r.Errores, e => Assert.Equal(Hoja, e.Hoja));
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
        var ws = wb.Worksheet(Hoja);
        ws.Cell(5, ColDe("prod_gas")).Value = "12,5";
        ws.Cell(6, ColDe("prod_gas")).Value = "1.234,5";

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        Assert.Empty(r.Errores);
        var alfa = await db.Presentaciones.SingleAsync(p => p.IdBloque == 10);
        Assert.Equal(12.5m, (await db.PlanValores.SingleAsync(v => v.PresentacionId == alfa.Id && v.Actividad == TipoPlan.Convencional && v.Campo == "prod_gas" && v.Anio == 2026 && v.Mes == 1)).Valor);
        Assert.Equal(1234.5m, (await db.PlanValores.SingleAsync(v => v.PresentacionId == alfa.Id && v.Actividad == TipoPlan.Convencional && v.Campo == "prod_gas" && v.Anio == 2026 && v.Mes == 2)).Valor);
    }

    [Fact]
    public async Task Hay_que_contestar_Si_o_No_y_con_No_no_puede_haber_valores_cargados()
    {
        using var wb = await PlanillaCompleta(1);
        var ws = wb.Worksheet(Hoja);
        ws.Cell(FilaInicio(0, TipoPlan.Convencional), PlanillaExcel.ColActividad).Clear();                   // sin contestar
        ws.Cell(FilaInicio(1, TipoPlan.Cench), PlanillaExcel.ColActividad).Value = "No";                      // ya decía No...
        ws.Cell(FilaInicio(1, TipoPlan.Cench) + 2, ColDe("prod_gas")).Value = 4;                              // ...pero hay un valor
        ws.Cell(FilaInicio(0, TipoPlan.Cench), PlanillaExcel.ColActividad).Value = "quizás";                  // ni Sí ni No

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        Assert.Contains(r.Errores, e => e.Fila == FilaInicio(0, TipoPlan.Convencional) && e.Mensaje.Contains("elegí Sí o No"));
        Assert.Contains(r.Errores, e => e.Fila == FilaInicio(0, TipoPlan.Cench) && e.Mensaje.Contains("elegí Sí o No"));
        Assert.Contains(r.Errores, e => e.Fila == FilaInicio(1, TipoPlan.Cench) && e.Mensaje.Contains("elegiste No pero cargaste valores"));
        Assert.Empty(await db.Presentaciones.ToListAsync());
    }

    [Fact]
    public async Task Un_area_puede_declarar_No_en_los_dos_tipos_y_queda_todo_en_cero()
    {
        using var wb = await PlanillaCompleta(1);
        wb.Worksheet(Hoja).Cell(FilaInicio(1, TipoPlan.Convencional), PlanillaExcel.ColActividad).Value = "No";   // la segunda área sin actividad en ninguno
        var vaciar = wb.Worksheet(Hoja);
        for (int r = FilaInicio(1, TipoPlan.Convencional); r < FilaInicio(1, TipoPlan.Convencional) + FilasPorBloque; r++)
            foreach (var c in CatalogoCampos.Para(TipoPlan.Convencional).Where(c => !c.EsCalculado)) vaciar.Cell(r, ColDe(c.Codigo)).Clear();

        var res = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        Assert.Empty(res.Errores);
        var dos = await db.Presentaciones.Include(p => p.Valores).SingleAsync(p => p.IdBloque == 11);
        Assert.Equal(2026, dos.AnioHasta);                                                          // solo el año firme
        Assert.Equal(new[] { TipoPlan.Convencional, TipoPlan.Cench }, dos.TiposCargados());
        Assert.All(dos.Valores, v => Assert.Equal(0m, v.Valor));
    }

    [Fact]
    public async Task No_se_acepta_una_planilla_anterior_ni_cambiar_el_tipo_de_actividad()
    {
        using var wb = await PlanillaCompleta(1);
        wb.Worksheet(Hoja).Cell(5, PlanillaExcel.ColTipo).Value = "Otro tipo";
        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));
        Assert.Contains(r.Errores, e => e.Fila == 5 && e.Columna == "Tipo de Actividad");

        // La planilla vieja: dos hojas (Plan Convencional / Plan CENCH) y sin la columna de tipo.
        using var vieja = new XLWorkbook();
        vieja.AddWorksheet("Plan Convencional").Cell(1, 1).Value = "area";
        var sinHoja = await svc.ImportarAsync(Ana, [BloqueAlfa], Guardar(vieja));
        Assert.Contains(sinHoja.Errores, e => e.Mensaje.Contains($"«{Hoja}»") && e.Mensaje.Contains("volvé a bajarla"));
        Assert.Empty(await db.Presentaciones.ToListAsync());
    }

    [Fact]
    public async Task Detecta_codigos_de_area_ajenos_meses_o_anios_faltantes_y_filas_repetidas()
    {
        using var wb = await PlanillaCompleta(1);
        var ws = wb.Worksheet(Hoja);
        ws.Cell(5, PlanillaExcel.ColCodigo).Value = 999;                                  // área que no es de la empresa
        ws.Row(6).Delete();                                                               // falta un mes de la primera área
        ws.Cell(19, PlanillaExcel.ColAnio).Value = 2029;                                  // (tras borrar la fila 6) el 2030 del área pasa a 2029: repetido

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        Assert.Contains(r.Errores, e => e.Mensaje.Contains("999") && e.Mensaje.Contains("ninguna de las áreas"));
        Assert.Contains(r.Errores, e => e.Mensaje.Contains("faltan los meses") || e.Mensaje.Contains("falta la fila de enero"));
        Assert.Contains(r.Errores, e => e.Mensaje.Contains("repetido") || e.Mensaje.Contains("faltan los años"));
        Assert.Empty(await db.Presentaciones.ToListAsync());
    }

    [Fact]
    public async Task El_primer_anio_no_puede_ser_anual_ni_los_siguientes_mensuales()
    {
        using var wb = await PlanillaCompleta(1);
        var ws = wb.Worksheet(Hoja);
        ws.Cell(5, PlanillaExcel.ColMes).Value = "Anual";   // enero de 2026 sin mes
        ws.Cell(17, PlanillaExcel.ColMes).Value = 3;        // 2027 con mes

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        Assert.Contains(r.Errores, e => e.Fila == 5 && e.Mensaje.Contains("mes por mes"));
        Assert.Contains(r.Errores, e => e.Fila == 17 && e.Mensaje.Contains("anuales"));
    }

    [Fact]
    public async Task No_se_importa_sobre_un_area_ya_enviada_ni_un_archivo_que_no_es_la_planilla()
    {
        var a = await AreaCompleta(BloqueAlfa);
        await svc.EnviarAsync(a.Id, Ana, "http://x");
        using var wb = await PlanillaCompleta(1);

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));
        Assert.Contains(r.Errores, e => e.Mensaje.Contains("Área Alfa") && e.Mensaje.Contains("ya fue enviada"));
        Assert.Equal(1, await db.Presentaciones.CountAsync());                                    // y tampoco se creó la otra

        var basura = await svc.ImportarAsync(Ana, [BloqueAlfa], new MemoryStream([1, 2, 3, 4]));
        Assert.Contains(basura.Errores, e => e.Mensaje.Contains("No se pudo abrir"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ImportarAsync(Beto, [BloqueAlfa], new MemoryStream()));
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.PlantillaAsync(Ana, []));
    }

    [Fact]
    public async Task Una_planilla_importada_completa_cumple_los_numeros_pero_igual_exige_anexos_y_restricciones_para_la_final()
    {
        using var wb = await PlanillaCompleta(1);
        await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        var r = await svc.EnviarFinalAsync(Ana, [BloqueAlfa, BloqueAlfa2], id => "http://x/" + id);

        Assert.Equal(0, r.Enviadas);
        Assert.Equal(2, r.Problemas.Count);
        Assert.All(r.Problemas, x => { Assert.Contains("Falta adjuntar", x); Assert.Contains("Restricciones a la actividad", x); Assert.DoesNotContain("sin actividad", x); });
    }

    [Fact]
    public async Task Los_anios_finales_en_blanco_no_se_importan_y_el_rango_llega_hasta_el_ultimo_usado()
    {
        using var wb = await PlanillaCompleta(1);    // completa hasta 2030; de 2031 en adelante queda en blanco
        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        Assert.Empty(r.Errores);
        Assert.All(await db.Presentaciones.ToListAsync(), p => Assert.Equal(2030, p.AnioHasta));
        Assert.DoesNotContain(await db.PlanValores.ToListAsync(), v => v.Anio > 2030);
    }

    [Fact]
    public async Task Un_anio_posterior_a_medio_completar_o_con_un_hueco_se_rechaza()
    {
        using var wb = await PlanillaCompleta(1);
        var ws = wb.Worksheet(Hoja);
        ws.Cell(18, ColDe("prod_gas")).Clear();                                   // 2028 de Alfa con una celda vacía: error de celda
        for (int i = 0; i < 20; i++) ws.Cell(19, PlanillaExcel.PrimeraColCampo + i).Clear();   // 2029 casi en blanco...
        ws.Cell(19, ColDe("pozos_perforados")).Value = 3;                         // ...con una sola celda: sigue siendo una fila usada

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        Assert.Contains(r.Errores, e => e.Fila == 18 && e.Mensaje.Contains("celda vacía"));
        Assert.Contains(r.Errores, e => e.Fila == 19 && e.Mensaje.Contains("celda vacía"));
        Assert.Empty(await db.Presentaciones.ToListAsync());

        using var wb2 = await PlanillaCompleta(1);
        var ws2 = wb2.Worksheet(Hoja);
        for (int i = 0; i < Unificadas.Count; i++) ws2.Cell(18, PlanillaExcel.PrimeraColCampo + i).Clear();   // 2028 en blanco pero 2029 y 2030 completos: hueco
        var hueco = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb2));
        Assert.Contains(hueco.Errores, e => e.Mensaje.Contains("faltan los años") && e.Mensaje.Contains("2028"));
    }

    // ---------- Un área con dos planes de actividad (formulario y validación) ----------

    [Fact]
    public async Task Un_area_tiene_que_declarar_sus_dos_planes_y_el_que_no_tiene_se_marca_sin_actividad()
    {
        var p = await BorradorDeAna();
        await CargarFirmeMensual(p.Id);                                                    // solo Convencional

        var obs = (await svc.RevisarAsync(p.Id, Ana)).Where(o => o.Mensaje.Contains("sin actividad")).ToList();
        Assert.Single(obs);
        Assert.Contains("No Convencional", obs[0].Mensaje); Assert.True(obs[0].EsRequisito);   // bloquea la final, no el envío suelto

        await svc.MarcarSinActividadAsync(p.Id, Ana, TipoPlan.Cench);
        Assert.DoesNotContain(await svc.RevisarAsync(p.Id, Ana), o => o.Mensaje.Contains("sin actividad"));
        var guardado = await svc.ObtenerAsync(p.Id, Ana);
        Assert.Equal(new[] { TipoPlan.Convencional, TipoPlan.Cench }, guardado.TiposCargados());
        Assert.All(guardado.Valores.Where(v => v.Actividad == TipoPlan.Cench), v => Assert.Equal(0m, v.Valor));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.MarcarSinActividadAsync(p.Id, Beto, TipoPlan.Cench));
    }

    [Fact]
    public async Task Los_dos_planes_se_guardan_calculan_y_validan_por_separado()
    {
        var p = await BorradorDeAna();
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 5, 1), new(2026, "inv_perforacion", 2, 1)]);                                  // Convencional (el principal)
        var filas = await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 7, 1, TipoPlan.Cench), new(2026, "inv_desarrollo", 4, 1, TipoPlan.Cench)]);

        Assert.Equal(7m, filas[Calculadora.Clave(2026, 1)]["prod_gas"]);
        Assert.Equal(4m, filas[Calculadora.Clave(2026, 1)]["inv_total"]);                 // en No Convencional "Desarrollo" se carga: es el total
        var g = await svc.ObtenerAsync(p.Id, Ana);
        Assert.Equal(2m, Calculadora.CalcularPeriodos(TipoPlan.Convencional, g.Valores.Where(v => v.Actividad == TipoPlan.Convencional))[Calculadora.Clave(2026, 1)]["inv_total"]);
        // Un campo de un tipo no se puede cargar en el otro, ni se mezclan tipos en un mismo guardado.
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "gto_salarios", 1, 1, TipoPlan.Cench)]));
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 1, 1), new(2026, "prod_gas", 1, 1, TipoPlan.Cench)]));
    }

    [Fact]
    public async Task La_rectificatoria_conserva_los_dos_planes_y_la_huella_los_distingue()
    {
        var p = await AreaCompleta(BloqueAlfa);
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 9, 1, TipoPlan.Cench)]);
        var antes = PresentacionService.Huella(await svc.ObtenerAsync(p.Id, Ana));
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 9, 1)]);                                   // mismo valor, otro plan
        Assert.NotEqual(antes, PresentacionService.Huella(await svc.ObtenerAsync(p.Id, Ana)));

        var v1 = await svc.EnviarAsync(p.Id, Ana, "http://x");
        await Habilitar(v1.Id);
        var v2 = await svc.RectificarAsync(v1.Id, Ana);
        Assert.Equal(new[] { TipoPlan.Convencional, TipoPlan.Cench }, v2.TiposCargados());
        Assert.Equal(v1.Valores.Count, v2.Valores.Count);
    }

    [Fact]
    public async Task El_tablero_cuenta_cada_plan_por_separado_y_el_filtro_por_tipo_elige_uno()
    {
        var p = await AreaCompleta(BloqueAlfa);
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "inv_desarrollo", 4, 1, TipoPlan.Cench)]);   // No Convencional: inversión propia
        await svc.EnviarAsync(p.Id, Ana, "http://x");
        var enviada = await db.Presentaciones.AsNoTracking().Include(x => x.Valores).Include(x => x.Anexos).Include(x => x.Ciclo).SingleAsync();

        var ambos = Agregador.Calcular(enviada.TiposCargados().Select(enviada.ParaTipo).ToList(), 2026, 5);
        var soloNc = Agregador.Calcular([enviada.ParaTipo(TipoPlan.Cench)], 2026, 5);
        Assert.Equal(1m + 4m, ambos.InversionFirme);     // 1 (Convencional) + 4 (No Convencional)
        Assert.Equal(4m, soloNc.InversionFirme);
    }

    [Fact]
    public void Los_adjuntos_son_solo_los_pedidos_y_Otros_es_opcional()
    {
        Assert.Equal(new[] { "fundamento_flujo_fondos", "flujograma", "paradas_planta", "presentacion_plan", "otro" }, AnexosRequeridos.Todos.Select(a => a.Codigo));
        Assert.Equal(new[] { "fundamento_flujo_fondos", "flujograma", "paradas_planta", "presentacion_plan" }, AnexosRequeridos.Todos.Where(a => a.Obligatorio).Select(a => a.Codigo));
        Assert.Equal("Paradas programadas de planta proyectadas para 2027 por área (y pérdidas de producción asociadas)", AnexosRequeridos.Buscar("paradas_planta")!.Titulo(2027));
        Assert.Equal("Presentación del Plan Anual de Inversiones 2027", AnexosRequeridos.Buscar("presentacion_plan")!.Titulo(2027));
    }
}
