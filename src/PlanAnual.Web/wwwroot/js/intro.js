/* Introducción animada al sistema (72 s). Un reloj maestro (T, en segundos) dibuja todo: cada escena es una función del tiempo,
   por eso se puede pausar, repetir y saltar a cualquier punto. Sin librerías. Las cifras son ilustrativas. */
(function () {
    'use strict';
    var NS = 'http://www.w3.org/2000/svg', W = 1280, H = 720;
    var svg = document.getElementById('stage'), root = document.getElementById('intro');
    var reduce = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) && !/[?&]movimiento=1/.test(location.search);   // ?movimiento=1 fuerza la animación completa
    var C = { noche: '#00212e', linea: '#2f6f86', conv: '#F8C60A', cench: '#00ADB5', coral: '#F26054', ok: '#87B867', tinta: '#eaf3f6', tenue: '#8fb0bd', petroleo: '#00374C' };

    // ---------- utilidades ----------
    function E(tag, attrs, parent) { var e = document.createElementNS(NS, tag); for (var k in attrs) e.setAttribute(k, attrs[k]); if (parent) parent.appendChild(e); return e; }
    function TX(parent, x, y, str, cls, attrs) { var a = { x: x, y: y, 'class': cls }; if (attrs) for (var k in attrs) a[k] = attrs[k]; var t = E('text', a, parent); t.textContent = str; return t; }
    function clamp(x) { return x < 0 ? 0 : x > 1 ? 1 : x; }
    function seg(t, a, b) { return clamp((t - a) / (b - a)); }
    function lerp(a, b, t) { return a + (b - a) * t; }
    function out(t) { return 1 - Math.pow(1 - t, 3); }
    function inout(t) { return t < .5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; }
    function back(t) { var c = 1.70158; return 1 + (c + 1) * Math.pow(t - 1, 3) + c * Math.pow(t - 1, 2); }
    function rng(seed) { return function () { seed |= 0; seed = seed + 0x6D2B79F5 | 0; var t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
    function op(e, o) { e.setAttribute('opacity', o.toFixed(3)); }
    function rise(e, p, dy) { e.setAttribute('opacity', p.toFixed(3)); e.setAttribute('transform', 'translate(0,' + ((1 - p) * (dy || 18)).toFixed(2) + ')'); }
    function draw(path, p) { path.setAttribute('stroke-dashoffset', (1 - p).toFixed(4)); }
    function dashed(path) { path.setAttribute('pathLength', '1'); path.setAttribute('stroke-dasharray', '1 1'); path.setAttribute('stroke-dashoffset', '1'); }
    function head(g, title, sub) {
        var a = TX(g, 80, 130, title, 't-head'), b = TX(g, 80, 168, sub, 't-sub');
        return function (t) { rise(a, out(seg(t, .25, 1.0))); rise(b, out(seg(t, .6, 1.4))); };
    }

    // ---------- fondo permanente: curvas de nivel (cuenca) y encabezado ----------
    var defs = E('defs', {}, svg);
    var bg = E('g', {}, svg), contours = [];
    for (var i = 0; i < 17; i++) {
        var d = '', y0 = 90 + i * 38;
        for (var x = -60; x <= W + 60; x += 40) { var y = y0 + Math.sin(x * .0058 + i * .55) * 20 + Math.sin(x * .0131 + i * 1.3) * 9; d += (x === -60 ? 'M' : 'L') + x + ' ' + y.toFixed(1) + ' '; }
        contours.push(E('path', { d: d, fill: 'none', stroke: C.linea, 'stroke-width': 1.2 }, bg));
    }
    var hdr = E('g', {}, svg);
    E('rect', { x: 80, y: 0, width: 64, height: 14, fill: C.coral }, hdr);
    TX(hdr, 80, 50, 'PLAN ANUAL DE INVERSIONES', 't-kicker');
    var kicker = TX(hdr, 1200, 50, '', 't-kicker', { 'text-anchor': 'end' });

    // ---------- escenas ----------
    var scenes = [];

    // 1 · Apertura
    scenes.push({ dur: 8, kicker: '01 · APERTURA', cap: 'Cada año, las operadoras de Neuquén planifican sus inversiones. Este sistema las reúne en un solo lugar.', build: function (g) {
        var r = rng(11), dots = [];
        var prod = E('path', { d: 'M80 440 C 260 430, 330 392, 470 372 S 700 312, 820 262 S 1050 200, 1200 178', fill: 'none', stroke: C.coral, 'stroke-width': 5, 'stroke-linecap': 'round' }, g); dashed(prod);
        for (var i = 0; i < 15; i++) {
            var x = 190 + r() * 930, y = 215 + r() * 250;
            var ring = E('circle', { cx: x, cy: y, r: 6, fill: 'none', stroke: i % 3 === 0 ? C.cench : C.conv, 'stroke-width': 1.5 }, g);
            var dot = E('circle', { cx: x, cy: y, r: 0, fill: i % 3 === 0 ? C.cench : C.conv }, g);
            dots.push([dot, ring]);
        }
        var t1 = TX(g, 80, 590, 'Plan Anual de Inversiones', 't-title'), t2 = TX(g, 80, 640, 'Del dato a la decisión', 't-sub', { fill: C.conv, style: 'font-size:30px;fill:#F8C60A' });
        return function (t) {
            draw(prod, out(seg(t, 1.4, 5)));
            for (var i = 0; i < dots.length; i++) {
                var p = seg(t, .5 + i * .11, .9 + i * .11), f = (t * 1.1 + i * .37) % 1;
                dots[i][0].setAttribute('r', (5 * back(p)).toFixed(2));
                dots[i][1].setAttribute('r', (6 + f * 24).toFixed(2)); op(dots[i][1], p * (1 - f) * .55);
            }
            rise(t1, out(seg(t, 3, 4.2)), 26); rise(t2, out(seg(t, 4.5, 5.5)));
        };
    } });

    // 2 · Un solo registro
    scenes.push({ dur: 8, kicker: '02 · UN SOLO REGISTRO', cap: 'Se acabaron las planillas sueltas, los mails perdidos y las versiones cruzadas: todo vive en un único registro.', build: function (g) {
        var r = rng(5), kinds = [['XLS', '#3a9d6a'], ['NOTA', C.conv], ['MAIL', '#6f9bf2'], ['PDF', '#e0574d']], papers = [];
        var hA = TX(g, 80, 130, 'Planillas, mails, notas…', 't-head'), sA = TX(g, 80, 168, 'La información, repartida en cualquier lado', 't-sub');
        var hB = TX(g, 80, 130, 'Un solo registro', 't-head'), sB = TX(g, 80, 168, 'Cada dato, en su lugar y con su historia', 't-sub');
        for (var i = 0; i < 18; i++) {
            var k = kinds[i % 4], pg = E('g', {}, g);
            E('rect', { x: -55, y: -40, width: 110, height: 80, rx: 7, fill: '#0a4560', stroke: '#3d7f97', 'stroke-width': 1.3 }, pg);
            E('rect', { x: -55, y: -40, width: 44, height: 18, rx: 4, fill: k[1] }, pg);
            TX(pg, -33, -27, k[0], 't-small', { 'text-anchor': 'middle', style: 'font-size:11px;fill:#00212e' });
            for (var j = 0; j < 3; j++) E('rect', { x: -44, y: -10 + j * 16, width: 88 - j * 22, height: 5, rx: 2.5, fill: '#2f6f86' }, pg);
            var ok = E('circle', { cx: 46, cy: 32, r: 7, fill: C.ok }, pg);
            papers.push({ g: pg, ok: ok, sx: 90 + r() * 1100, sy: 220 + r() * 330, rot: (r() - .5) * 56, ex: 155 + (i % 6) * 162 + 75, ey: 255 + Math.floor(i / 6) * 124 + 20, ph: r() * 6 });
        }
        return function (t) {
            var hh = inout(seg(t, 2.7, 3.7));
            rise(hA, out(seg(t, .25, 1)) * (1 - hh)); rise(sA, out(seg(t, .6, 1.4)) * (1 - hh));
            rise(hB, hh); rise(sB, seg(t, 3.2, 4.2));
            for (var i = 0; i < papers.length; i++) {
                var p = papers[i], m = inout(seg(t, 2 + i * .07, 4.4 + i * .04)), a = out(seg(t, 0 + i * .04, .6 + i * .04));
                var x = lerp(p.sx, p.ex, m), y = lerp(p.sy, p.ey, m) + Math.sin(t * 1.6 + p.ph) * 7 * (1 - m);
                p.g.setAttribute('transform', 'translate(' + x.toFixed(1) + ',' + y.toFixed(1) + ') rotate(' + (p.rot * (1 - m)).toFixed(1) + ') scale(' + (.9 + .1 * m).toFixed(3) + ')');
                op(p.g, a); op(p.ok, seg(t, 4.8 + i * .06, 5.2 + i * .06)); p.ok.setAttribute('r', (7 * back(seg(t, 4.8 + i * .06, 5.3 + i * .06))).toFixed(2));
            }
        };
    } });

    // 3 · La carga
    scenes.push({ dur: 12, kicker: '03 · LA CARGA', cap: 'Cada área carga su plan: Convencional, No Convencional o ambos. El primer año mes a mes; después, año a año.', build: function (g) {
        var hd = head(g, 'Cada área, su plan', 'Convencional y No Convencional, mes a mes y año a año');
        var r = rng(21), panels = [], cols = ['Inversión', 'Pozos', 'Petróleo', 'Gas'], rows = ['Ene', 'Feb', 'Mar', 'Abr', 'May', '…', 'Dic'];
        [['Plan Convencional', C.conv, 80, 1.2], ['Plan No Convencional (CENCH)', C.cench, 660, 3.6]].forEach(function (pn) {
            var pg = E('g', {}, g), x0 = pn[2], y0 = 215, cells = [];
            var ttl = TX(pg, x0, y0, pn[0], 't-small', { style: 'font-size:20px;font-weight:800', fill: pn[1] }); ttl.style.fill = pn[1];
            var rl = [];
            cols.forEach(function (c, ci) { var h = TX(pg, x0 + 70 + ci * 117 + 58, y0 + 36, c, 't-axis', { 'text-anchor': 'middle' }); rl.push(h); });
            rows.forEach(function (rw, ri) { var l = TX(pg, x0 + 8, y0 + 82 + ri * 42, rw, 't-axis'); rl.push(l); });
            for (var ri = 0; ri < rows.length; ri++) for (var ci = 0; ci < 4; ci++) {
                var cg = E('g', {}, pg), cx = x0 + 70 + ci * 117, cy = y0 + 52 + ri * 42;
                E('rect', { x: cx + 3, y: cy + 3, width: 111, height: 36, rx: 5, fill: pn[1], 'fill-opacity': .13, stroke: pn[1], 'stroke-opacity': .45 }, cg);
                TX(cg, cx + 58, cy + 27, ri === 5 ? '⋯' : (ci === 1 ? String(Math.round(r() * 9 + 1)) : (r() * 90 + 10).toFixed(1)), 't-cell', { 'text-anchor': 'middle' });
                cells.push(cg);
            }
            panels.push({ g: pg, cells: cells, ttl: ttl, rl: rl, t0: pn[3] });
        });
        var chip = E('g', {}, g);
        E('rect', { x: 340, y: 578, width: 600, height: 38, rx: 20, fill: 'rgba(255,255,255,.08)', stroke: '#2f6f86' }, chip);
        TX(chip, 640, 603, 'Formulario o Excel · se valida mientras se carga', 't-small', { 'text-anchor': 'middle', style: 'font-size:16px' });
        return function (t) {
            hd(t);
            panels.forEach(function (p) {
                rise(p.ttl, out(seg(t, p.t0 - .5, p.t0 + .2)), 12); p.rl.forEach(function (l) { op(l, seg(t, p.t0 - .2, p.t0 + .6)); });
                p.cells.forEach(function (c, i) { var a = out(seg(t, p.t0 + .3 + i * .1, p.t0 + .6 + i * .1)); op(c, a); });
            });
            rise(chip, out(seg(t, 9, 10)), 14);
        };
    } });

    // 4 · Enviar con garantías
    scenes.push({ dur: 9, kicker: '04 · ENVIAR CON GARANTÍAS', cap: 'Antes de enviar, el sistema valida los datos. Al enviar, entrega un comprobante con una huella que prueba que nada cambió.', build: function (g) {
        var hd = head(g, 'Enviar con garantías', 'Validación automática y un comprobante inalterable');
        E('rect', { x: 100, y: 205, width: 640, height: 345, rx: 18, fill: 'rgba(255,255,255,.05)', stroke: '#2f6f86' }, g);
        var items = ['Datos validados', 'Anexos completos', 'Dentro del plazo'], checks = [];
        items.forEach(function (s, i) {
            var y = 262 + i * 56;
            E('circle', { cx: 150, cy: y, r: 16, fill: 'none', stroke: '#2f6f86', 'stroke-width': 2 }, g);
            var fill = E('circle', { cx: 150, cy: y, r: 16, fill: C.ok }, g);
            var ck = E('path', { d: 'M-7 0 L-2 6 L8 -6', transform: 'translate(150,' + y + ')', fill: 'none', stroke: '#00212e', 'stroke-width': 3.2, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, g); dashed(ck);
            var tx = TX(g, 184, y + 7, s, 't-head', { style: 'font-size:23px;font-family:Manrope,sans-serif;font-weight:700' });
            checks.push([fill, ck, tx]);
        });
        E('line', { x1: 130, y1: 420, x2: 710, y2: 420, stroke: '#2f6f86', 'stroke-width': 1 }, g);
        TX(g, 130, 452, 'COMPROBANTE', 't-kicker', { style: 'font-size:12px' });
        var code = TX(g, 130, 488, '', 't-mono', { style: 'font-size:30px;fill:#F8C60A' });
        var hash = TX(g, 130, 526, '', 't-mono', { style: 'font-size:17px;fill:#8fb0bd' });
        var seal = E('g', {}, g);
        E('circle', { cx: 0, cy: 0, r: 96, fill: 'none', stroke: C.coral, 'stroke-width': 6 }, seal);
        E('circle', { cx: 0, cy: 0, r: 82, fill: 'none', stroke: C.coral, 'stroke-width': 1.6 }, seal);
        TX(seal, 0, 10, 'ENVIADO', 't-head', { 'text-anchor': 'middle', style: 'font-size:36px;fill:#F26054' });
        TX(seal, 0, 40, 'PLAN 2027 · v1', 't-small', { 'text-anchor': 'middle', style: 'font-size:14px;fill:#F26054;letter-spacing:.12em' });
        var CODE = 'PAI-2027-000123-v1', FIN = '9F2C·7A1E·5B03·D8C4·…·E41A', HEX = '0123456789ABCDEF', rr = rng(3);
        return function (t) {
            hd(t);
            checks.forEach(function (c, i) { var p = seg(t, 1 + i * 1.2, 1.5 + i * 1.2); c[0].setAttribute('r', (16 * back(p)).toFixed(2)); draw(c[1], seg(t, 1.2 + i * 1.2, 1.7 + i * 1.2)); op(c[2], .45 + .55 * p); });
            var s = out(seg(t, 4.6, 5.3)); seal.setAttribute('transform', 'translate(990,380) rotate(-12) scale(' + lerp(2, 1, s).toFixed(3) + ')'); op(seal, s);
            code.textContent = CODE.slice(0, Math.floor(seg(t, 5.6, 7) * CODE.length));
            var n = seg(t, 6.6, 8.2) * FIN.length, hs = '';
            for (var i = 0; i < FIN.length; i++) hs += (t < 6.6 ? ' ' : (i < n ? FIN[i] : (FIN[i] === '·' || FIN[i] === '…' ? FIN[i] : HEX[Math.floor(rr() * 16)])));
            hash.textContent = t < 6.6 ? '' : 'SHA-256  ' + hs;
        };
    } });

    // 5 · Línea de tiempo
    scenes.push({ dur: 10, kicker: '05 · EL DIÁLOGO', cap: 'Notas, respuestas, prórrogas y decisiones de la Subsecretaría quedan registradas en una línea de tiempo.', build: function (g) {
        var hd = head(g, 'Cada movimiento, registrado', 'Notas, respuestas, prórrogas y decisiones en una sola línea de tiempo');
        var line = E('line', { x1: 100, y1: 390, x2: 1180, y2: 390, stroke: C.linea, 'stroke-width': 3, 'stroke-linecap': 'round' }, g); dashed(line);
        var data = [['Nota con plazo', 'Subsecretaría · 10/11', C.conv], ['Respuesta', 'Empresa · 13/11', C.cench], ['Prórroga solicitada', 'Empresa · 14/11', C.coral], ['Prórroga otorgada', 'Nuevo plazo · 05/04', C.ok], ['Plan aprobado', 'Decisión · 19/11', '#ffffff']], nodes = [];
        data.forEach(function (d, i) {
            var x = 170 + i * 235, up = i % 2 === 0, ng = E('g', {}, g);
            E('line', { x1: x, y1: 390, x2: x, y2: up ? 318 : 462, stroke: d[2], 'stroke-width': 2, 'stroke-opacity': .6 }, ng);
            var c = E('circle', { cx: x, cy: 390, r: 15, fill: C.noche, stroke: d[2], 'stroke-width': 4 }, ng);
            var inner = E('circle', { cx: x, cy: 390, r: 6, fill: d[2] }, ng);
            var lb = E('g', {}, ng);
            TX(lb, x, up ? 292 : 498, d[0], 't-head', { 'text-anchor': 'middle', style: 'font-size:23px;font-family:Manrope,sans-serif;font-weight:800' });
            TX(lb, x, up ? 316 : 524, d[1], 't-axis', { 'text-anchor': 'middle', style: 'font-size:15px' });
            nodes.push({ g: ng, c: c, inner: inner, lb: lb });
        });
        return function (t) {
            hd(t); draw(line, out(seg(t, 1, 3)));
            nodes.forEach(function (n, i) { var p = seg(t, 1.8 + i * 1.3, 2.4 + i * 1.3); op(n.g, p); n.c.setAttribute('r', (15 * back(p)).toFixed(2)); n.inner.setAttribute('r', (6 * back(p)).toFixed(2)); rise(n.lb, out(seg(t, 2 + i * 1.3, 2.8 + i * 1.3)), 10); });
        };
    } });

    // 6 · El tablero
    scenes.push({ dur: 14, kicker: '06 · EL TABLERO', cap: 'Con los planes enviados, el tablero muestra la inversión, la producción y las alertas para decidir con información.', build: function (g) {
        var hd = head(g, 'La inversión, a la vista', 'Tablero de la Subsecretaría · datos ilustrativos');
        var years = [2027, 2028, 2029, 2030, 2031, 2032, 2033, 2034], conv = [320, 360, 380, 360, 330, 290, 250, 220], cen = [120, 220, 340, 420, 450, 440, 420, 390];
        var prodv = [30, 44, 62, 82, 98, 108, 112, 114], base = 612, top = 262, max = 900, sc = (base - top) / max;
        TX(g, 120, 236, 'INVERSIÓN PROYECTADA POR AÑO, MMUSD', 't-kicker', { style: 'font-size:13px' });
        [0, 300, 600, 900].forEach(function (v) { var y = base - v * sc; E('line', { x1: 120, y1: y, x2: 1160, y2: y, stroke: '#2f6f86', 'stroke-opacity': .5, 'stroke-width': 1 }, g); TX(g, 112, y + 5, String(v), 't-axis', { 'text-anchor': 'end' }); });
        var bars = [], pts = [];
        years.forEach(function (yr, i) {
            var x = 170 + i * 125, bg2 = E('g', {}, g);
            var b1 = E('rect', { x: x, y: base, width: 72, height: 0, fill: C.conv }, bg2), b2 = E('rect', { x: x, y: base, width: 72, height: 0, fill: C.cench }, bg2);
            TX(g, x + 36, base + 26, String(yr), 't-axis', { 'text-anchor': 'middle' });
            bars.push([b1, b2, i]);
            pts.push([x + 36, 545 - (prodv[i] - 30) / 84 * 250]);
        });
        var d = pts.map(function (p, i) { return (i ? 'L' : 'M') + p[0] + ' ' + p[1]; }).join(' ');
        var line = E('path', { d: d, fill: 'none', stroke: C.coral, 'stroke-width': 4, 'stroke-linejoin': 'round', 'stroke-linecap': 'round' }, g); dashed(line);
        var pdots = pts.map(function (p) { return E('circle', { cx: p[0], cy: p[1], r: 0, fill: C.coral, stroke: C.noche, 'stroke-width': 2 }, g); });
        // leyenda
        var lg = E('g', {}, g);
        var lx = 640;
        [[C.conv, 'Convencional', 0], [C.cench, 'No Convencional (CENCH)', 140], [C.coral, 'Producción', 355]].forEach(function (l) { E('rect', { x: lx + l[2], y: 224, width: 14, height: 14, rx: 3, fill: l[0] }, lg); TX(lg, lx + l[2] + 22, 236, l[1], 't-axis', { style: 'font-size:14px' }); });
        // anotación del pico
        var pk = 3, px = 170 + pk * 125 + 36, py = base - (conv[pk] + cen[pk]) * sc;
        var an = E('g', {}, g);
        E('line', { x1: px, y1: py - 6, x2: px, y2: 214, stroke: C.tinta, 'stroke-width': 1.5, 'stroke-dasharray': '4 4' }, an);
        E('rect', { x: px - 118, y: 190, width: 236, height: 32, rx: 4, fill: C.coral }, an);
        TX(an, px, 212, 'Pico de inversión · 2030', 't-small', { 'text-anchor': 'middle', style: 'font-size:16px;font-weight:800;fill:#fff' });
        an.setAttribute('transform', 'translate(0,0)');
        return function (t) {
            hd(t); op(lg, seg(t, 1, 2));
            bars.forEach(function (b) { var i = b[2], p = out(seg(t, 1.6 + i * .32, 2.7 + i * .32)), h1 = conv[i] * sc * p, h2 = cen[i] * sc * p; b[0].setAttribute('y', (base - h1).toFixed(1)); b[0].setAttribute('height', h1.toFixed(1)); b[1].setAttribute('y', (base - h1 - h2).toFixed(1)); b[1].setAttribute('height', h2.toFixed(1)); });
            draw(line, inout(seg(t, 5.6, 9.6)));
            pdots.forEach(function (c, i) { c.setAttribute('r', (6 * back(seg(t, 5.6 + i * .5, 6.1 + i * .5))).toFixed(2)); });
            rise(an, out(seg(t, 10, 11)), 10);
        };
    } });

    // 7 · Cierre
    scenes.push({ dur: 11, kicker: '07 · CIERRE', cap: 'Plan Anual de Inversiones. Del dato a la decisión.', build: function (g) {
        var a = TX(g, 640, 330, 'Cada dato,', 't-big', { 'text-anchor': 'middle' }), b = TX(g, 640, 430, 'una decisión.', 't-big', { 'text-anchor': 'middle', style: 'fill:#F8C60A' });
        var ln = E('rect', { x: 576, y: 470, width: 128, height: 6, fill: C.coral }, g);
        var s1 = TX(g, 640, 524, 'Plan Anual de Inversiones', 't-sub', { 'text-anchor': 'middle', style: 'font-size:26px;fill:#eaf3f6;font-weight:700' });
        var s2 = TX(g, 640, 556, 'Subsecretaría de Hidrocarburos · Provincia del Neuquén', 't-sub', { 'text-anchor': 'middle', style: 'font-size:19px' });
        return function (t) { rise(a, out(seg(t, .6, 1.6)), 24); rise(b, out(seg(t, 1.8, 2.8)), 24); ln.setAttribute('width', (128 * out(seg(t, 3.2, 4))).toFixed(1)); ln.setAttribute('x', (640 - 64 * out(seg(t, 3.2, 4))).toFixed(1)); rise(s1, out(seg(t, 4, 5))); rise(s2, out(seg(t, 4.4, 5.4))); };
    } });

    // ---------- armado ----------
    var TOTAL = 0;
    scenes.forEach(function (s) { s.start = TOTAL; TOTAL += s.dur; s.g = E('g', { visibility: 'hidden' }, svg); s.update = s.build(s.g); });
    var T = 0, playing = !reduce && !/[?&]pause=1/.test(location.search), last = 0, cur = -1, ended = false;
    var cap = document.getElementById('cap'), fill = document.getElementById('fill'), time = document.getElementById('time'), track = document.getElementById('track'), cta = document.getElementById('cta'), marks = document.getElementById('marks');
    scenes.forEach(function (s, i) { if (i) { var m = document.createElement('i'); m.style.left = (s.start / TOTAL * 100) + '%'; marks.appendChild(m); } });
    track.setAttribute('aria-valuemax', String(TOTAL));
    function mmss(x) { x = Math.floor(x); return Math.floor(x / 60) + ':' + ('0' + (x % 60)).slice(-2); }

    function setCap(text) { cap.classList.remove('on'); setTimeout(function () { cap.textContent = text; cap.classList.add('on'); }, 220); }
    function render() {
        var idx = 0;
        for (var i = 0; i < scenes.length; i++) if (T >= scenes[i].start) idx = i;
        if (idx !== cur) { cur = idx; kicker.textContent = scenes[idx].kicker; setCap(scenes[idx].cap); }
        // el fondo respira despacio y se ve más en la apertura
        var drift = T * 3; bg.setAttribute('transform', 'translate(' + (-(drift % 40)).toFixed(2) + ',0)');
        bg.setAttribute('opacity', (idx === 0 ? lerp(.15, .55, seg(T, 0, 1.5)) : .38).toFixed(2));
        scenes.forEach(function (s, i) {
            var lt = T - s.start, vis = lt >= -.01 && lt <= s.dur + .01;
            s.g.setAttribute('visibility', vis ? 'visible' : 'hidden');
            if (!vis) return;
            var fadeIn = i === 0 || reduce ? 1 : seg(lt, 0, .5), fadeOut = i === scenes.length - 1 || reduce ? 1 : 1 - seg(lt, s.dur - .5, s.dur);
            s.g.setAttribute('opacity', Math.min(fadeIn, fadeOut).toFixed(3));
            s.update(reduce ? s.dur : lt);
        });
        var pct = T / TOTAL * 100; fill.style.width = pct + '%'; time.textContent = mmss(T) + ' / ' + mmss(TOTAL);
        track.setAttribute('aria-valuenow', String(Math.floor(T)));
        var finalScene = scenes[scenes.length - 1];
        cta.hidden = !(T >= finalScene.start + 6);
    }
    function advance(dt) { if (playing) { T += dt; if (T >= TOTAL) { T = TOTAL; playing = false; ended = true; syncBtn(); } render(); } }
    function tick(ts) {
        var dt = last ? Math.min(.1, (ts - last) / 1000) : 0; last = ts;
        advance(dt);
        requestAnimationFrame(tick);
    }
    function syncBtn() { root.classList.toggle('is-paused', !playing); var b = document.getElementById('play'); b.setAttribute('aria-label', playing ? 'Pausar' : (ended ? 'Reproducir de nuevo' : 'Reproducir')); }
    function seek(x) { T = Math.max(0, Math.min(TOTAL, x)); ended = T >= TOTAL; if (T < TOTAL && ended) ended = false; render(); }
    function toggle() { if (ended || T >= TOTAL) { T = 0; ended = false; playing = true; } else playing = !playing; syncBtn(); render(); }
    function restart() { T = 0; ended = false; playing = true; syncBtn(); render(); }

    document.getElementById('play').addEventListener('click', toggle);
    document.getElementById('replay').addEventListener('click', restart);
    document.getElementById('again').addEventListener('click', restart);
    track.addEventListener('click', function (e) { var r = track.getBoundingClientRect(); seek((e.clientX - r.left) / r.width * TOTAL); });
    document.addEventListener('keydown', function (e) {
        if (e.key === ' ' && e.target === document.body) { e.preventDefault(); toggle(); }
        else if (e.key === 'ArrowRight') seek(T + 5);
        else if (e.key === 'ArrowLeft') seek(T - 5);
        else if (e.key === 'Escape') document.getElementById('skip').click();
    });
    window.__intro = { seek: seek, advance: advance, state: function () { return { T: T, playing: playing, ended: ended }; }, total: TOTAL, pause: function () { playing = false; syncBtn(); } };   // útil para revisar cuadros sueltos
    syncBtn(); render(); requestAnimationFrame(tick);
})();
