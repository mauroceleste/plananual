// Arma los gráficos del tablero a partir del JSON embebido en la página.
(function () {
    'use strict';
    var nodo = document.getElementById('tablero-datos');

    // Los filtros se aplican solos al cambiar (el servidor recalcula todo con el mismo recorte).
    var filtros = document.getElementById('filtros');
    // La pestaña elegida queda en la dirección (#fiscal, #operacion…) para no volver al Resumen al filtrar.
    var tabs = document.querySelectorAll('.tablero-tabs [data-bs-toggle="tab"]');
    function hashDe(b) { return b.getAttribute('data-bs-target').replace('#tab-', '#'); }
    function irAPestana() {
        tabs.forEach(function (b) { if (location.hash && hashDe(b) === location.hash && window.bootstrap) window.bootstrap.Tab.getOrCreateInstance(b).show(); });
    }
    tabs.forEach(function (b) {
        b.addEventListener('shown.bs.tab', function () { history.replaceState(null, '', location.pathname + location.search + hashDe(b)); });
    });
    window.addEventListener('hashchange', irAPestana);
    irAPestana();
    if (filtros) filtros.addEventListener('change', function () { filtros.action = location.pathname + location.hash; filtros.submit(); });
    if (!nodo) return;

    var d = JSON.parse(nodo.textContent);
    function host(id) { return document.querySelector('[data-chart="' + id + '"]'); }
    function hay(arr, f) { return arr.some(f); }
    function vacio(h) {
        h.textContent = '';
        var p = document.createElement('p'); p.className = 'chart-vacio'; p.textContent = 'Todavía no hay datos enviados.'; h.appendChild(p);
        var btn = h.closest('[data-chart-card]').querySelector('[data-ver-tabla]'); if (btn) btn.hidden = true;
    }
    function pintar(id, cfg, conDatos) { var h = host(id); if (!h) return; if (!conDatos) vacio(h); else window.Charts.montar(h, cfg); }

    var anios = d.inversionPorAnio.map(function (x) { return x.anio; });

    pintar('inversion-anio', {
        tipo: 'columnas', modo: 'apilado', titulo: 'Inversión por año y tipo de plan', unidad: 'MMUSD', dec: 1, categorias: anios,
        series: [
            { nombre: 'Convencional', color: 'var(--chart-1)', valores: d.inversionPorAnio.map(function (x) { return x.convencional; }) },
            { nombre: 'CENCH', color: 'var(--chart-2)', valores: d.inversionPorAnio.map(function (x) { return x.cench; }) }
        ]
    }, hay(d.inversionPorAnio, function (x) { return x.convencional + x.cench > 0; }));

    pintar('inversion-rubro', {
        tipo: 'barras', titulo: 'Inversión por rubro', unidad: 'MMUSD', dec: 1, etiqueta: 'de la inversión firme',
        items: d.inversionPorRubro.map(function (r) { return { nombre: r.nombre, valor: r.valor }; })
    }, d.inversionPorRubro.length > 0);

    pintar('inversion-empresa', {
        tipo: 'barras', titulo: 'Inversión por empresa', unidad: 'MMUSD', dec: 1, etiqueta: 'de la inversión firme',
        items: d.inversionPorEmpresa.map(function (r) { return { nombre: r.nombre, valor: r.valor, resto: /^Otras \(/.test(r.nombre) }; })
    }, d.inversionPorEmpresa.length > 0);

    pintar('pozos', {
        tipo: 'columnas', modo: 'agrupado', titulo: 'Pozos por año', unidad: 'pozos', dec: 0, categorias: anios, alto: 230,
        series: [
            { nombre: 'Perforados', color: 'var(--chart-1)', valores: d.pozos.map(function (x) { return x.perforados; }) },
            { nombre: 'A abandonar', color: 'var(--chart-muted)', valores: d.pozos.map(function (x) { return x.abandonos; }) }
        ]
    }, hay(d.pozos, function (x) { return x.perforados + x.abandonos > 0; }));

    pintar('petroleo', {
        tipo: 'columnas', modo: 'agrupado', titulo: 'Producción de petróleo', unidad: 'MMbbl', dec: 2, categorias: anios, alto: 230,
        series: [{ nombre: 'Petróleo', color: 'var(--chart-1)', valores: d.produccion.map(function (x) { return x.petroleo; }) }]
    }, hay(d.produccion, function (x) { return x.petroleo > 0; }));

    pintar('gas', {
        tipo: 'columnas', modo: 'agrupado', titulo: 'Producción de gas', unidad: 'MMm3', dec: 1, categorias: anios, alto: 230,
        series: [{ nombre: 'Gas', color: 'var(--chart-1)', valores: d.produccion.map(function (x) { return x.gas; }) }]
    }, hay(d.produccion, function (x) { return x.gas > 0; }));

    // ---------- indicadores (pestañas Fiscal, Operación y Calidad) ----------
    var ind = d.indicadores;
    if (ind) {
        var ia = ind.anios, iAnios = ia.map(function (x) { return x.anio; });
        function col(campo) { return ia.map(function (x) { return x[campo] || 0; }); }
        function algo(campo) { return hay(ia, function (x) { return (x[campo] || 0) !== 0; }); }

        pintar('flujo-acumulado', {
            tipo: 'lineas', titulo: 'Flujo de fondos acumulado', unidad: 'MMUSD', dec: 1, categorias: iAnios,
            series: [{ nombre: 'Flujo acumulado', color: 'var(--chart-1)', valores: col('flujoAcumulado') }]
        }, algo('flujoAcumulado'));

        pintar('ingresos-renta', {
            tipo: 'columnas', modo: 'agrupado', titulo: 'Ingresos, inversión y renta fiscal', unidad: 'MMUSD', dec: 1, categorias: iAnios,
            series: [
                { nombre: 'Ingresos', color: 'var(--chart-1)', valores: col('ingresos') },
                { nombre: 'Inversión', color: 'var(--chart-2)', valores: col('inversion') },
                { nombre: 'Renta fiscal', color: 'var(--chart-muted)', valores: ia.map(function (x) { return x.rentaFiscal; }) }
            ]
        }, algo('ingresos') || algo('inversion'));

        pintar('mix', {
            tipo: 'mix', titulo: 'Convencional y CENCH', dec: 1, unidad: '',
            series: [{ nombre: 'Convencional', color: 'var(--chart-1)' }, { nombre: 'CENCH', color: 'var(--chart-2)' }],
            items: ind.mix.map(function (m) { return { nombre: m.nombre, valores: [m.convencional, m.cench] }; })
        }, ind.mix.length > 0);

        pintar('mapa', {
            tipo: 'mapa', titulo: 'Inversión por empresa y año', unidad: 'MMUSD', dec: 1, categorias: iAnios,
            items: ind.mapaInversion.map(function (f) { return { nombre: f.nombre, valores: f.valores }; })
        }, ind.mapaInversion.length > 0);

        pintar('petroleo-dia', {
            tipo: 'columnas', modo: 'agrupado', titulo: 'Petróleo por día', unidad: 'mil bbl/d', dec: 1, categorias: iAnios, alto: 230,
            series: [{ nombre: 'Petróleo', color: 'var(--chart-1)', valores: col('petroleoKbbld') }]
        }, algo('petroleoKbbld'));

        pintar('gas-dia', {
            tipo: 'columnas', modo: 'agrupado', titulo: 'Gas por día', unidad: 'MMm3/d', dec: 2, categorias: iAnios, alto: 230,
            series: [{ nombre: 'Gas', color: 'var(--chart-2)', valores: col('gasMMm3d') }]
        }, algo('gasMMm3d'));

        pintar('sin-conectar', {
            tipo: 'lineas', titulo: 'Pozos perforados sin conectar', unidad: 'pozos', dec: 0, categorias: iAnios, alto: 230,
            series: [{ nombre: 'Sin conectar (acumulado)', color: 'var(--chart-2)', valores: col('sinConectarAcumulado') }]
        }, algo('sinConectarAcumulado'));

        pintar('gor', {
            tipo: 'lineas', titulo: 'Relación gas/petróleo', unidad: 'm3/m3', dec: 0, categorias: iAnios, alto: 230,
            series: [{ nombre: 'GOR', color: 'var(--chart-1)', valores: ia.map(function (x) { return x.gor || 0; }) }]
        }, hay(ia, function (x) { return x.gor; }));

        pintar('wor', {
            tipo: 'lineas', titulo: 'Relación agua/petróleo', unidad: 'm3/m3', dec: 2, categorias: iAnios, alto: 230,
            series: [{ nombre: 'WOR', color: 'var(--chart-2)', valores: ia.map(function (x) { return x.wor || 0; }) }]
        }, hay(ia, function (x) { return x.wor; }));

        var c = ind.cumplimiento;
        pintar('plazo', {
            tipo: 'segmentos', titulo: 'Presentaciones según plazo', unidad: 'áreas',
            items: [
                { nombre: 'En plazo', valor: c.enPlazo, color: 'var(--st-ok)' },
                { nombre: 'Fuera de plazo', valor: c.fueraDePlazo, color: 'var(--st-warn)', texto: '#0b0b0b' }
            ]
        }, c.enviadas > 0);
    }

    // Estado de cumplimiento: colores de estado reservados (verde / amarillo / rojo), siempre con nombre y cantidad.
    pintar('cumplimiento', {
        tipo: 'segmentos', titulo: 'Empresas según avance de la presentación', unidad: 'empresas',
        items: [
            { nombre: 'Completas', valor: d.empresasCompletas, color: 'var(--st-ok)' },
            { nombre: 'Parciales', valor: d.empresasParciales, color: 'var(--st-warn)', texto: '#0b0b0b' },
            { nombre: 'Sin presentar', valor: d.empresasSinPresentar, color: 'var(--st-crit)' }
        ]
    }, d.empresasTotales > 0);
})();
