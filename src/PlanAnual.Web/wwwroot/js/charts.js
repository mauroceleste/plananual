// Gráficos del tablero: SVG/HTML propios, sin librerías. Los colores salen de variables CSS (--chart-*),
// así que el modo claro/oscuro no requiere redibujar. Todo texto de usuario entra con textContent.
(function () {
    'use strict';
    var NS = 'http://www.w3.org/2000/svg';
    var AR = 'es-AR';

    function num(n, d) { return Number(n).toLocaleString(AR, { minimumFractionDigits: d || 0, maximumFractionDigits: d || 0 }); }
    function compacto(n, dp) { return Math.abs(n) >= 1000 ? num(n / 1000, n % 1000 === 0 ? 0 : 1) + ' mil' : num(n, dp || 0); }
    function decimalesDe(paso) { var d = 0; while (d < 3 && Math.abs(paso * Math.pow(10, d) - Math.round(paso * Math.pow(10, d))) > 1e-9) d++; return d; }

    function el(tag, attrs, texto) {
        var e = document.createElementNS(NS, tag);
        for (var k in attrs) e.setAttribute(k, attrs[k]);
        if (texto !== undefined) e.textContent = texto;
        return e;
    }
    function html(tag, clase, texto) {
        var e = document.createElement(tag);
        if (clase) e.className = clase;
        if (texto !== undefined) e.textContent = texto;
        return e;
    }

    // Escala "linda": 1, 2 o 5 por potencia de diez.
    function escala(max, marcas) {
        if (max <= 0) return { max: 1, paso: 0.25 };
        var bruto = max / marcas, pot = Math.pow(10, Math.floor(Math.log10(bruto))), f = bruto / pot;
        var paso = (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * pot;
        return { max: Math.ceil(max / paso) * paso, paso: paso };
    }

    // ---------- tooltip único ----------
    var tip;
    function tooltip() {
        if (!tip) { tip = html('div', 'chart-tip'); tip.setAttribute('role', 'tooltip'); document.body.appendChild(tip); }
        return tip;
    }
    function mostrarTip(titulo, filas, x, y) {
        var t = tooltip();
        t.textContent = '';
        t.appendChild(html('div', 'chart-tip__t', titulo));
        filas.forEach(function (f) {
            var r = html('div', 'chart-tip__r');
            if (f.color) { var k = html('span', 'chart-tip__k'); k.style.background = f.color; r.appendChild(k); }
            r.appendChild(html('strong', null, f.valor));
            r.appendChild(html('span', null, f.nombre));
            t.appendChild(r);
        });
        t.style.display = 'block';
        var w = t.offsetWidth, h = t.offsetHeight;
        var px = Math.min(x + 14, window.innerWidth - w - 8), py = y - h - 12;
        if (py < 8) py = y + 18;
        t.style.left = Math.max(8, px) + 'px'; t.style.top = py + 'px';
    }
    function ocultarTip() { if (tip) tip.style.display = 'none'; }
    function conTip(nodo, datos) {
        function en(ev) {
            var r = nodo.getBoundingClientRect();
            var x = ev && ev.clientX !== undefined && ev.type !== 'focus' ? ev.clientX : r.left + r.width / 2;
            var y = ev && ev.clientY !== undefined && ev.type !== 'focus' ? ev.clientY : r.top;
            mostrarTip(datos.titulo, datos.filas, x, y);
            nodo.classList.add('is-hover');
        }
        nodo.addEventListener('pointermove', en); nodo.addEventListener('pointerenter', en); nodo.addEventListener('focus', en);
        ['pointerleave', 'blur'].forEach(function (n) { nodo.addEventListener(n, function () { ocultarTip(); nodo.classList.remove('is-hover'); }); });
    }

    // ---------- leyenda ----------
    function leyenda(series) {
        if (series.length < 2) return null;
        var l = html('ul', 'chart-legend');
        series.forEach(function (s) {
            var li = html('li'); var k = html('span', 'chart-legend__k'); k.style.background = s.color; li.appendChild(k); li.appendChild(html('span', null, s.nombre)); l.appendChild(li);
        });
        return l;
    }

    // ---------- columnas (apiladas o agrupadas) ----------
    function arriba(x, y, w, h, r) {   // rectángulo con las esquinas superiores redondeadas (base cuadrada)
        r = Math.max(0, Math.min(r, h, w / 2));
        return 'M' + x + ' ' + (y + h) + 'V' + (y + r) + 'Q' + x + ' ' + y + ' ' + (x + r) + ' ' + y + 'H' + (x + w - r) + 'Q' + (x + w) + ' ' + y + ' ' + (x + w) + ' ' + (y + r) + 'V' + (y + h) + 'Z';
    }

    function columnas(host, cfg) {
        host.textContent = '';
        var lg = leyenda(cfg.series); if (lg) host.appendChild(lg);
        var W = Math.max(host.clientWidth, 260), H = cfg.alto || 250, m = { l: 46, r: 10, t: 22, b: 26 };
        var pw = W - m.l - m.r, ph = H - m.t - m.b, n = cfg.categorias.length, banda = pw / n;
        var apilado = cfg.modo === 'apilado', k = cfg.series.length;
        var etiquetar = banda >= 56;                       // valores sobre las barras solo si caben (si no, quedan en el tooltip y la tabla)
        var paso = Math.max(1, Math.ceil(34 / banda));   // etiquetas del eje: con muchos años se saltean para no pisarse

        var totales = cfg.categorias.map(function (_, i) {
            return apilado ? cfg.series.reduce(function (a, s) { return a + (s.valores[i] || 0); }, 0) : Math.max.apply(null, cfg.series.map(function (s) { return s.valores[i] || 0; }));
        });
        var esc = escala(Math.max.apply(null, totales.concat([0])), 4), dpEje = decimalesDe(esc.paso);
        function Y(v) { return m.t + ph - (v / esc.max) * ph; }

        var svg = el('svg', { viewBox: '0 0 ' + W + ' ' + H, width: W, height: H, role: 'img', 'aria-label': cfg.titulo });
        for (var v = 0; v <= esc.max + 1e-9; v += esc.paso) {
            svg.appendChild(el('line', { x1: m.l, x2: W - m.r, y1: Y(v), y2: Y(v), class: 'chart-grid' }));
            svg.appendChild(el('text', { x: m.l - 8, y: Y(v) + 4, 'text-anchor': 'end', class: 'chart-axis' }, compacto(v, dpEje)));
        }
        cfg.categorias.forEach(function (cat, i) {
            var cx = m.l + banda * i + banda / 2;
            var hit = el('rect', { x: m.l + banda * i, y: m.t, width: banda, height: ph, class: 'chart-hit', tabindex: 0 });
            svg.appendChild(hit);
            if (i % paso === 0) svg.appendChild(el('text', { x: cx, y: H - 8, 'text-anchor': 'middle', class: 'chart-cat' }, String(cat)));

            if (apilado) {
                var bw = Math.min(24, banda * 0.5), acum = 0, ultimo = -1;
                cfg.series.forEach(function (s, si) { if ((s.valores[i] || 0) > 0) ultimo = si; });
                cfg.series.forEach(function (s, si) {
                    var val = s.valores[i] || 0; if (val <= 0) return;
                    var yTop = Y(acum + val), yBase = Y(acum), h = yBase - yTop - (acum > 0 ? 2 : 0);
                    if (h > 0) svg.appendChild(el('path', { d: si === ultimo ? arriba(cx - bw / 2, yTop, bw, h, 4) : 'M' + (cx - bw / 2) + ' ' + yTop + 'h' + bw + 'v' + h + 'h' + (-bw) + 'Z', style: 'fill:' + s.color, class: 'chart-mark' }));
                    acum += val;
                });
                if (etiquetar && acum > 0) svg.appendChild(el('text', { x: cx, y: Y(acum) - 6, 'text-anchor': 'middle', class: 'chart-val' }, num(acum, cfg.dec)));
            } else {
                var bw2 = Math.min(24, (banda * 0.7) / k - 2), tot = bw2 * k + 2 * (k - 1), x0 = cx - tot / 2;
                cfg.series.forEach(function (s, si) {
                    var val = s.valores[i] || 0, h = Y(0) - Y(val);
                    if (h > 0) svg.appendChild(el('path', { d: arriba(x0 + si * (bw2 + 2), Y(val), bw2, h, 4), style: 'fill:' + s.color, class: 'chart-mark' }));
                });
                if (etiquetar && k === 1 && totales[i] > 0) svg.appendChild(el('text', { x: cx, y: Y(totales[i]) - 6, 'text-anchor': 'middle', class: 'chart-val' }, num(totales[i], cfg.dec)));
            }

            var filas = cfg.series.map(function (s) { return { color: s.color, valor: num(s.valores[i] || 0, cfg.dec) + ' ' + cfg.unidad, nombre: s.nombre }; });
            if (apilado && k > 1) filas.push({ valor: num(totales[i], cfg.dec) + ' ' + cfg.unidad, nombre: 'Total' });
            hit.setAttribute('aria-label', cat + ': ' + filas.map(function (f) { return f.nombre + ' ' + f.valor; }).join(', '));
            conTip(hit, { titulo: String(cat), filas: filas });
        });
        svg.appendChild(el('line', { x1: m.l, x2: W - m.r, y1: Y(0), y2: Y(0), class: 'chart-base' }));
        host.appendChild(svg);
    }

    // ---------- líneas (admite valores negativos) ----------
    function lineas(host, cfg) {
        host.textContent = '';
        var lg = leyenda(cfg.series); if (lg) host.appendChild(lg);
        var W = Math.max(host.clientWidth, 260), H = cfg.alto || 250, m = { l: 50, r: 14, t: 18, b: 26 };
        var pw = W - m.l - m.r, ph = H - m.t - m.b, n = cfg.categorias.length, banda = pw / n;
        var paso = Math.max(1, Math.ceil(34 / banda));
        var todos = []; cfg.series.forEach(function (s) { todos = todos.concat(s.valores); });
        var max = Math.max.apply(null, todos.concat([0])), min = Math.min.apply(null, todos.concat([0]));
        var esc = escala(Math.max(max, -min, 1e-9), 4), dpEje = decimalesDe(esc.paso);
        var techo = max > 0 ? Math.ceil(max / esc.paso) * esc.paso : 0, piso = min < 0 ? Math.floor(min / esc.paso) * esc.paso : 0;
        if (techo === piso) techo = piso + esc.paso;
        function Y(v) { return m.t + ph - ((v - piso) / (techo - piso)) * ph; }
        function X(i) { return m.l + banda * i + banda / 2; }

        var svg = el('svg', { viewBox: '0 0 ' + W + ' ' + H, width: W, height: H, role: 'img', 'aria-label': cfg.titulo });
        for (var v = piso; v <= techo + 1e-9; v += esc.paso) {
            svg.appendChild(el('line', { x1: m.l, x2: W - m.r, y1: Y(v), y2: Y(v), class: Math.abs(v) < 1e-9 ? 'chart-base' : 'chart-grid' }));
            svg.appendChild(el('text', { x: m.l - 8, y: Y(v) + 4, 'text-anchor': 'end', class: 'chart-axis' }, compacto(v, dpEje)));
        }
        cfg.series.forEach(function (s) {
            var d = s.valores.map(function (val, i) { return (i ? 'L' : 'M') + X(i) + ' ' + Y(val); }).join('');
            svg.appendChild(el('path', { d: d, fill: 'none', style: 'stroke:' + s.color, 'stroke-width': 2.5, 'stroke-linejoin': 'round', 'stroke-linecap': 'round' }));
            if (banda >= 14) s.valores.forEach(function (val, i) { svg.appendChild(el('circle', { cx: X(i), cy: Y(val), r: 3.5, style: 'fill:' + s.color, class: 'chart-dot' })); });
        });
        cfg.categorias.forEach(function (cat, i) {
            if (i % paso === 0) svg.appendChild(el('text', { x: X(i), y: H - 8, 'text-anchor': 'middle', class: 'chart-cat' }, String(cat)));
            var hit = el('rect', { x: m.l + banda * i, y: m.t, width: banda, height: ph, class: 'chart-hit', tabindex: 0 });
            svg.appendChild(hit);
            var filas = cfg.series.map(function (s) { return { color: s.color, valor: num(s.valores[i] || 0, cfg.dec) + ' ' + cfg.unidad, nombre: s.nombre }; });
            hit.setAttribute('aria-label', cat + ': ' + filas.map(function (f) { return f.nombre + ' ' + f.valor; }).join(', '));
            conTip(hit, { titulo: String(cat), filas: filas });
        });
        host.appendChild(svg);
    }

    // ---------- mezcla CENCH / convencional: una barra al 100 % por fila ----------
    function mix(host, cfg) {
        host.textContent = '';
        var lg = leyenda(cfg.series); if (lg) host.appendChild(lg);
        var lista = html('ul', 'mixbar'); lista.setAttribute('aria-label', cfg.titulo);
        cfg.items.forEach(function (it) {
            var li = html('li', 'mixbar__row'); li.appendChild(html('span', 'mixbar__n', it.nombre));
            var pista = html('span', 'mixbar__track'), total = it.valores[0] + it.valores[1];
            it.valores.forEach(function (val, si) {
                if (val <= 0) return;
                var s = html('span', 'mixbar__s', Math.round(val / total * 100) + '%');
                s.style.flexGrow = val; s.style.background = cfg.series[si].color; s.style.color = si === 1 ? '#0b0b0b' : '#fff'; s.tabIndex = 0;
                conTip(s, { titulo: it.nombre, filas: [{ color: cfg.series[si].color, valor: num(val, cfg.dec) + ' ' + cfg.unidad, nombre: cfg.series[si].nombre }] });
                pista.appendChild(s);
            });
            if (total <= 0) pista.appendChild(html('span', 'mixbar__vacio', 'sin datos'));
            li.appendChild(pista); lista.appendChild(li);
        });
        host.appendChild(lista);
    }

    // ---------- mapa de calor: empresa × año (un solo color, la intensidad marca el valor) ----------
    function mapa(host, cfg) {
        host.textContent = '';
        var max = Math.max.apply(null, cfg.items.reduce(function (a, f) { return a.concat(f.valores); }, [0]));
        var t = html('table', 'table mapa'), cab = html('tr'), th = html('thead'), tb = html('tbody');
        t.setAttribute('aria-label', cfg.titulo);
        var h0 = html('th', null, 'Empresa'); h0.scope = 'col'; cab.appendChild(h0);
        cfg.categorias.forEach(function (c) { var h = html('th', 'text-end', String(c)); h.scope = 'col'; cab.appendChild(h); });
        var ht = html('th', 'text-end', 'Total'); ht.scope = 'col'; cab.appendChild(ht);
        th.appendChild(cab);
        cfg.items.forEach(function (f) {
            var r = html('tr'), n = html('th', null, f.nombre); n.scope = 'row'; n.title = f.nombre; r.appendChild(n);
            var tot = 0;
            f.valores.forEach(function (val, i) {
                tot += val;
                var c = html('td', 't-num text-end', val > 0 ? num(val, cfg.dec) : '·');
                if (val > 0 && max > 0) c.style.background = 'color-mix(in srgb, var(--chart-1) ' + Math.round(8 + (val / max) * 62) + '%, transparent)';
                c.tabIndex = 0;
                conTip(c, { titulo: f.nombre + ' · ' + cfg.categorias[i], filas: [{ valor: num(val, cfg.dec) + ' ' + cfg.unidad, nombre: 'inversión' }] });
                r.appendChild(c);
            });
            r.appendChild(html('td', 't-num text-end fw-bold', num(tot, cfg.dec)));
            tb.appendChild(r);
        });
        t.appendChild(th); t.appendChild(tb);
        var w = html('div', 'table-responsive'); w.appendChild(t); host.appendChild(w);
    }

    // ---------- barras horizontales (HTML) ----------
    function barras(host, cfg) {
        host.textContent = '';
        var items = cfg.items, max = Math.max.apply(null, items.map(function (i) { return i.valor; }).concat([0]));
        var lista = html('ul', 'hbar'); lista.setAttribute('aria-label', cfg.titulo);
        items.forEach(function (it) {
            var li = html('li', 'hbar__row'); li.tabIndex = 0;
            var nombre = html('span', 'hbar__n', it.nombre); nombre.title = it.nombre;
            var pista = html('span', 'hbar__track'), barra = html('span', 'hbar__bar' + (it.resto ? ' is-resto' : ''));
            barra.style.width = max > 0 ? Math.max(0.5, (it.valor / max) * 100) + '%' : '0';
            pista.appendChild(barra);
            li.appendChild(nombre); li.appendChild(pista); li.appendChild(html('span', 'hbar__v', num(it.valor, cfg.dec)));
            li.setAttribute('aria-label', it.nombre + ': ' + num(it.valor, cfg.dec) + ' ' + cfg.unidad);
            conTip(li, { titulo: it.nombre, filas: [{ valor: num(it.valor, cfg.dec) + ' ' + cfg.unidad, nombre: cfg.etiqueta || '' }] });
            lista.appendChild(li);
        });
        host.appendChild(lista);
    }

    // ---------- barra segmentada (cumplimiento) ----------
    function segmentos(host, cfg) {
        host.textContent = '';
        var total = cfg.items.reduce(function (a, i) { return a + i.valor; }, 0);
        var barra = html('div', 'seg'); barra.setAttribute('role', 'img'); barra.setAttribute('aria-label', cfg.titulo);
        cfg.items.forEach(function (it) {
            if (it.valor <= 0) return;
            var s = html('div', 'seg__s'); s.style.flexGrow = it.valor; s.style.background = it.color; s.style.color = it.texto || '#fff'; s.tabIndex = 0;
            s.appendChild(html('span', null, String(it.valor)));
            conTip(s, { titulo: it.nombre, filas: [{ color: it.color, valor: it.valor + ' de ' + total, nombre: cfg.unidad }] });
            barra.appendChild(s);
        });
        host.appendChild(barra);
        var l = html('ul', 'chart-legend');
        cfg.items.forEach(function (it) {
            var li = html('li'); var k = html('span', 'chart-legend__k'); k.style.background = it.color; li.appendChild(k);
            li.appendChild(html('span', null, it.nombre + ' (' + it.valor + ')')); l.appendChild(li);
        });
        host.appendChild(l);
    }

    // ---------- vista de tabla (siempre disponible) ----------
    function tabla(cfg) {
        var t = html('table', 'table data-table chart-table'), th = html('thead'), tb = html('tbody');
        var cab = html('tr');
        var porAnio = cfg.tipo === 'columnas' || cfg.tipo === 'lineas';
        var nCol = porAnio ? ['Año'].concat(cfg.series.map(function (s) { return s.nombre + ' (' + cfg.unidad + ')'; })) : ['', cfg.unidad];
        nCol.forEach(function (c, i) { var h = html('th', i ? 'text-end' : '', c); h.scope = 'col'; cab.appendChild(h); });
        th.appendChild(cab);
        if (porAnio) cfg.categorias.forEach(function (c, i) {
            var r = html('tr'); r.appendChild(html('th', null, String(c))); r.firstChild.scope = 'row';
            cfg.series.forEach(function (s) { r.appendChild(html('td', 't-num text-end', num(s.valores[i] || 0, cfg.dec))); }); tb.appendChild(r);
        });
        else cfg.items.forEach(function (it) { var r = html('tr'); r.appendChild(html('th', null, it.nombre)); r.firstChild.scope = 'row'; r.appendChild(html('td', 't-num text-end', num(it.valor, cfg.dec))); tb.appendChild(r); });
        t.appendChild(th); t.appendChild(tb);
        var w = html('div', 'table-responsive'); w.appendChild(t); return w;
    }

    var DIBUJOS = { columnas: columnas, lineas: lineas, barras: barras, segmentos: segmentos, mix: mix, mapa: mapa };

    window.Charts = {
        montar: function (host, cfg) {
            var dibujar = function () { DIBUJOS[cfg.tipo](host, cfg); };
            dibujar();
            // También cuando la pestaña estaba oculta (ancho 0) y se muestra.
            if ((cfg.tipo === 'columnas' || cfg.tipo === 'lineas') && window.ResizeObserver) {
                var ultimo = host.clientWidth, raf;
                new ResizeObserver(function () { if (host.clientWidth === ultimo) return; ultimo = host.clientWidth; cancelAnimationFrame(raf); raf = requestAnimationFrame(dibujar); }).observe(host);
            }
            // Alternar gráfico / tabla (la tabla da acceso a todos los valores sin hover).
            var card = host.closest('[data-chart-card]'), btn = card && card.querySelector('[data-ver-tabla]');
            if (btn) {
                var vista = html('div', 'chart-tablewrap'); vista.hidden = true; vista.appendChild(tabla(cfg)); host.after(vista);
                btn.addEventListener('click', function () {
                    var ver = vista.hidden; vista.hidden = !ver; host.hidden = ver;
                    btn.textContent = ver ? 'Ver gráfico' : 'Ver tabla'; btn.setAttribute('aria-pressed', ver ? 'true' : 'false');
                    if (!ver) dibujar();
                });
            }
        },
        num: num
    };
})();
