using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Catalogo;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Legajos;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

// Planilla «con lo cargado»: se baja lo que ya está en el sistema, se corrige en Excel y se vuelve a importar (rectificaciones).
public sealed partial class PresentacionServiceTests
{
    [Fact]
    public async Task La_planilla_con_lo_cargado_trae_Si_con_valores_No_para_sin_actividad_y_sin_contestar_lo_no_cargado()
    {
        using (var wb0 = await PlanillaCompleta(2)) await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb0));   // Convencional con 2 en todo; No Convencional: «No»
        var p = await db.Presentaciones.SingleAsync(x => x.IdBloque == 10);
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 7.5m, 3)]);                               // una corrección hecha a mano en el formulario

        var tres = new BloqueInfo(12, "Área Alfa Tres", "Área Alfa Tres", "111", "Alfa S.A.", "A012");
        var bytes = await svc.PlantillaAsync(Ana, [BloqueAlfa, BloqueAlfa2, tres], conLoCargado: true);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(Hoja);

        Assert.Equal("Sí", ws.Cell(FilaInicio(0, TipoPlan.Convencional), PlanillaExcel.ColActividad).GetString());
        Assert.Equal("No", ws.Cell(FilaInicio(0, TipoPlan.Cench), PlanillaExcel.ColActividad).GetString());
        Assert.Equal(7.5, ws.Cell(FilaInicio(0, TipoPlan.Convencional) + 2, ColDe("prod_gas")).GetDouble());      // marzo: lo corregido
        Assert.Equal(2, ws.Cell(FilaInicio(0, TipoPlan.Convencional) + 3, ColDe("prod_gas")).GetDouble());        // abril: lo importado
        Assert.True(ws.Cell(FilaInicio(0, TipoPlan.Cench) + 1, ColDe("prod_gas")).IsEmpty());                     // «No»: el bloque queda sin valores
        // Un área que no se cargó nunca (la tercera): sin contestar ni valores.
        Assert.True(ws.Cell(FilaInicio(2, TipoPlan.Convencional), PlanillaExcel.ColActividad).IsEmpty());
        Assert.True(ws.Cell(FilaInicio(2, TipoPlan.Convencional), ColDe("prod_gas")).IsEmpty());
        // Años: hasta el último cargado (2030); los de más quedan en blanco, como siempre.
        var fila2030 = Enumerable.Range(FilaInicio(0, TipoPlan.Convencional), FilasPorBloque).First(r => ws.Cell(r, PlanillaExcel.ColAnio).GetValue<int>() == 2030);
        Assert.Equal(2, ws.Cell(fila2030, ColDe("prod_gas")).GetDouble());
        Assert.True(ws.Cell(fila2030 + 1, ColDe("prod_gas")).IsEmpty());
    }

    [Fact]
    public async Task Bajar_lo_cargado_y_volver_a_importarlo_sin_tocar_nada_deja_las_cosas_igual()
    {
        using (var wb0 = await PlanillaCompleta(valor: 1, convencional: true, noConvencional: true, valorNc: 3)) await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb0));
        List<(int, TipoPlan, int, int, string, decimal?)> Foto() => db.PlanValores.AsNoTracking().OrderBy(v => v.PresentacionId).ThenBy(v => v.Actividad).ThenBy(v => v.Anio).ThenBy(v => v.Mes).ThenBy(v => v.Campo)
            .AsEnumerable().Select(v => ((int)v.PresentacionId, v.Actividad, v.Anio, v.Mes, v.Campo, v.Valor)).ToList();
        var antes = Foto();

        var bytes = await svc.PlantillaAsync(Ana, [BloqueAlfa, BloqueAlfa2], conLoCargado: true);
        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], new MemoryStream(bytes));

        Assert.Empty(r.Errores);
        Assert.Equal(antes, Foto());                                                    // mismos valores, mismas claves: nada se perdió ni se duplicó
        Assert.Equal(2, await db.Presentaciones.CountAsync());
    }

    [Fact]
    public async Task Para_rectificar_se_baja_lo_cargado_se_corrige_y_se_importa_en_la_rectificatoria()
    {
        using (var wb0 = await PlanillaCompleta(1)) await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb0));
        var a = await db.Presentaciones.SingleAsync(x => x.IdBloque == 10);
        await AdjuntarRequeridos(a.Id);
        await svc.GuardarRestriccionesAsync(a.Id, Ana, "Ninguno", "Ninguno", "Ninguno", "Ninguno");
        var v1 = await svc.EnviarAsync(a.Id, Ana, "http://x");
        await Habilitar(v1.Id);
        var v2 = await svc.RectificarAsync(v1.Id, Ana);                                    // borrador v2, copia de la v1

        var bytes = await svc.PlantillaAsync(Ana, [BloqueAlfa, BloqueAlfa2], conLoCargado: true);   // trae la última versión de cada área (la v2 de Alfa)
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(Hoja);
        Assert.Equal(1, ws.Cell(FilaInicio(0, TipoPlan.Convencional) + 1, ColDe("prod_gas")).GetDouble());
        ws.Cell(FilaInicio(0, TipoPlan.Convencional) + 1, ColDe("prod_gas")).Value = 99;   // corrige febrero

        var r = await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));

        Assert.Empty(r.Errores);
        Assert.Equal(99m, (await db.PlanValores.SingleAsync(x => x.PresentacionId == v2.Id && x.Actividad == TipoPlan.Convencional && x.Campo == "prod_gas" && x.Anio == 2026 && x.Mes == 2)).Valor);
        Assert.Equal(1m, (await db.PlanValores.SingleAsync(x => x.PresentacionId == v1.Id && x.Actividad == TipoPlan.Convencional && x.Campo == "prod_gas" && x.Anio == 2026 && x.Mes == 2)).Valor);   // la v1 enviada no se toca
        Assert.Equal(EstadoPresentacion.Enviada, (await db.Presentaciones.AsNoTracking().SingleAsync(x => x.Id == v1.Id)).Estado);
    }

    [Fact]
    public async Task No_hay_planilla_con_lo_cargado_si_todavia_no_se_cargo_nada()
    {
        await Assert.ThrowsAsync<OperacionInvalida>(() => svc.PlantillaAsync(Ana, [BloqueAlfa], conLoCargado: true));
        Assert.NotEmpty(await svc.PlantillaAsync(Ana, [BloqueAlfa]));                    // la planilla en blanco sigue andando
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.PlantillaAsync(Beto, [BloqueAlfa], conLoCargado: true));
    }
}
