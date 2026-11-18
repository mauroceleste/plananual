using Microsoft.EntityFrameworkCore;
using PlanAnual.Domain.Entities;
using PlanAnual.Infrastructure.Servicios;

namespace PlanAnual.Tests;

// Historial de modificaciones por área (quién cambió qué y cuándo): solo lo ve Administración.
public sealed partial class PresentacionServiceTests
{
    async Task<List<HistorialArea>> Historial(int idBloque = 10, string cuit = "111")
    {
        var ciclo = (await svc.CicloAbiertoAsync())!;
        return await svc.HistorialAsync(Admin, ciclo.Id, cuit, idBloque);
    }

    [Fact]
    public async Task El_historial_es_solo_de_Administracion()
    {
        var ciclo = (await svc.CicloAbiertoAsync())!;
        await BorradorDeAna();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.HistorialAsync(Ana, ciclo.Id, "111", 10));
        Assert.Single(await Historial());
    }

    [Fact]
    public async Task Registra_los_datos_puntuales_con_valor_anterior_y_nuevo()
    {
        var p = await BorradorDeAna();
        await svc.GuardarRestriccionesAsync(p.Id, Ana, "Conflicto en el lote 4", null, null, null);
        await svc.GuardarRestriccionesAsync(p.Id, Ana, "Conflicto resuelto", null, null, null);
        await svc.GuardarRestriccionesAsync(p.Id, Ana, "Conflicto resuelto", null, null, null);     // sin cambios: no suma renglón
        await svc.GuardarExpedienteAsync(p.Id, Admin, "EX-1");
        await svc.GuardarExpedienteAsync(p.Id, Admin, "EX-2");
        await svc.CambiarRangoAsync(p.Id, Ana, 2026, 2031);

        var h = await Historial();
        var texto = h.Where(x => x.Campo.StartsWith("Restricciones")).OrderBy(x => x.Id).ToList();
        Assert.Equal(2, texto.Count);
        Assert.Equal((null, "Conflicto en el lote 4"), (texto[0].Anterior, texto[0].Nuevo));
        Assert.Equal(("Conflicto en el lote 4", "Conflicto resuelto"), (texto[1].Anterior, texto[1].Nuevo));
        var exp = h.Where(x => x.Campo == "Expediente GDE").OrderBy(x => x.Id).ToList();
        Assert.Equal(new[] { (null, "EX-1"), ("EX-1", "EX-2") }, exp.Select(x => (x.Anterior, x.Nuevo)).ToArray());
        Assert.All(exp, x => Assert.Equal("Admin", x.Usuario));
        Assert.Contains(h, x => x.Campo == "Años de la planilla" && x.Anterior == "2026–2030" && x.Nuevo == "2026–2031");
    }

    [Fact]
    public async Task Las_celdas_guardadas_seguidas_se_juntan_en_un_solo_renglon_y_otro_evento_lo_corta()
    {
        var p = await BorradorDeAna();
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 1, 1), new(2026, "prod_gas", 2, 2)]);
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 3, 3)]);
        var valores = (await Historial()).Where(x => x.Campo == "Valores · Plan Convencional").ToList();
        Assert.Single(valores);
        Assert.Equal("3 modificaciones", valores[0].Nuevo);

        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 9, 1, TipoPlan.Cench)]);          // otro plan: otro renglón
        await svc.GuardarCeldasAsync(p.Id, Ana, [new(2026, "prod_gas", 4, 4)]);                           // vuelve al Convencional: otro renglón (hubo otro evento en el medio)
        var h = await Historial();
        Assert.Equal(2, h.Count(x => x.Campo == "Valores · Plan Convencional"));
        Assert.Single(h, x => x.Campo == "Valores · Plan No Convencional (CENCH)");
    }

    [Fact]
    public async Task Registra_inicio_sin_actividad_adjuntos_envio_y_rectificatoria_lo_nuevo_primero()
    {
        var p = await AreaCompleta(BloqueAlfa);                                                          // inicia, carga, declara sin actividad, adjunta y escribe restricciones
        var anexo = (await svc.ObtenerAsync(p.Id, Ana)).Anexos.First();
        await svc.QuitarAnexoAsync(p.Id, anexo.Id, Ana);
        var v1 = await svc.EnviarAsync(p.Id, Ana, "http://x");
        await Habilitar(v1.Id);
        await svc.RectificarAsync(v1.Id, Ana);

        var h = await Historial();
        Assert.Equal("Rectificatoria", h[0].Campo);                                                      // lo más nuevo, primero
        Assert.Contains(h, x => x.Campo == "Área" && x.Nuevo == "Se inició la carga");
        Assert.Contains(h, x => x.Campo == "Plan No Convencional (CENCH)" && x.Nuevo!.Contains("sin actividad"));
        Assert.Contains(h, x => x.Campo.StartsWith("Adjunto - ") && x.Nuevo == anexo.NombreArchivo);
        Assert.Contains(h, x => x.Campo.StartsWith("Adjunto - ") && x.Anterior == anexo.NombreArchivo && x.Nuevo == "(eliminado)");
        Assert.Contains(h, x => x.Campo == "Presentación" && x.Nuevo!.StartsWith("Enviada v1"));
        Assert.Contains(h, x => x.Campo == "Pedido de rectificación" && x.Nuevo!.StartsWith("Habilitado hasta"));
        Assert.Equal(h.OrderByDescending(x => x.Fecha).ThenByDescending(x => x.Id).Select(x => x.Id), h.Select(x => x.Id));
        Assert.Empty(await Historial(idBloque: 11));                                                     // otra área: sin movimientos
    }

    [Fact]
    public async Task Importar_deja_constancia_de_los_planes_que_se_cargaron()
    {
        using var wb = await PlanillaCompleta(1);
        await svc.ImportarAsync(Ana, [BloqueAlfa, BloqueAlfa2], Guardar(wb));
        var imp = (await Historial()).Single(x => x.Campo == "Importación de Excel");
        Assert.Equal("Plan Convencional: con actividad · Plan No Convencional (CENCH): sin actividad", imp.Nuevo);
    }
}
