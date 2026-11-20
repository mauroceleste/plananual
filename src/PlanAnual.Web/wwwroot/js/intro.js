/* Introducción animada de Plan Anual de Inversiones, en tres tiempos (~11 s). Misma receta que la de Legajo de Áreas:
     A. La economía se dibuja: barras de inversión (Convencional y CENCH) suben año a año, la curva de producción corre por encima y la cámara se aleja.
     B. Las palabras del sistema: INVERSIONES · PRODUCCIÓN · PLANES · DECISIONES.
     C. El cierre: logo, título y cifras (empresas, áreas y convocatoria).
   Se ve una vez por sesión (cookie de sesión "plananual_intro"; el layout solo dibuja #intro cuando corresponde). ?intro=1 la repite, ?intro=0 la omite.
   Se puede saltar con el botón o con Escape. Quien tiene activada la opción "reducir movimiento" no la ve (salvo ?intro=1).
   Las barras y la curva son ilustrativas: no usan datos de ninguna empresa. Revisión cuadro a cuadro: ?intro=1&introT=3.5 */
(function () {
    'use strict';
    var root = document.getElementById('intro');
    if (!root) return;

    var NS = 'http://www.w3.org/2000/svg';
    var html = document.documentElement;
    var q = new URLSearchParams(location.search);

    function marcarVista() {
        try { document.cookie = 'plananual_intro=1; path=' + (root.getAttribute('data-cookie-path') || '/') + '; SameSite=Lax'; } catch (e) { /* sin cookies: se vuelve a ver, no pasa nada */ }
    }
    function quitar() { html.classList.remove('intro-activa'); if (root.parentNode) root.parentNode.removeChild(root); }
    function desvanecer(ms) {
        if (!root.animate) { quitar(); return; }
        var a = root.animate([{ opacity: 1 }, { opacity: 0 }], { duration: ms, easing: 'ease-in', fill: 'forwards' });
        a.onfinish = quitar;
    }

    marcarVista();                                    // se marca al empezar: si recarga a mitad de camino, no se repite
    var forzada = q.get('intro') === '1';
    if (!('animate' in Element.prototype) || (!forzada && matchMedia('(prefers-reduced-motion: reduce)').matches)) { quitar(); return; }
    html.classList.add('intro-activa');
    root.focus();

    var CIFRAS = {
        empresas: parseInt(root.getAttribute('data-empresas'), 10) || 0,
        areas: parseInt(root.getAttribute('data-areas'), 10) || 0,
        anio: parseInt(root.getAttribute('data-anio'), 10) || new Date().getFullYear()
    };
    var semilla = 7;                                  // azar repetible: la animación se ve siempre igual
    function azar() { semilla = (semilla * 16807) % 2147483647; return semilla / 2147483647; }
    function el(nombre, attrs, padre) { var e = document.createElementNS(NS, nombre); for (var k in attrs) e.setAttribute(k, attrs[k]); if (padre) padre.appendChild(e); return e; }
    var anims = [];
    function anima(nodo, frames, opc) { var a = nodo.animate(frames, Object.assign({ fill: 'both' }, opc)); anims.push(a); return a; }
    function $(sel) { return root.querySelector(sel); }
    function todos(sel) { return Array.prototype.slice.call(root.querySelectorAll(sel)); }

    // ---------------------------------------------------------------- línea de tiempo (ms)
    var T_C = 4300, PASO_C = 470, DUR_C = 4 * PASO_C;     // B: palabras (1,9 s)
    var T_TEXTO = T_C + DUR_C;                            // C: cierre
    var FIN = T_TEXTO + 4800;                             // ≈ 11,1 s (+ 0,7 s de desvanecido)
    var T_DIM = T_C - 500;                                // desde cuándo el gráfico queda de fondo, atenuado

    // ---------------------------------------------------------------- composición según la pantalla
    var lienzo = $('.intro__lienzo');
    var W = root.clientWidth, H = root.clientHeight;
    lienzo.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
    var CW = 1600, CH = 900;                              // "plano" del gráfico (unidades propias)
    var apaisado = W / H > 1.2;
    var s1 = apaisado ? Math.min((H * 0.74) / CH, (W * 0.53) / CW) : Math.min((H * 0.40) / CH, (W * 0.92) / CW);
    var fin = apaisado ? { x: W * 0.725 - (CW * s1) / 2, y: (H - CH * s1) / 2 } : { x: (W - CW * s1) / 2, y: H * 0.09 };
    var foco = { x: CW * 0.60, y: CH * 0.55 };            // donde la cámara arranca: el tramo de mayor inversión
    var s0 = s1 * 2.6;
    var ini = { x: W * 0.5 - foco.x * s0, y: H * 0.5 - foco.y * s0 };
    function mat(p, s) { return 'translate(' + p.x.toFixed(1) + 'px,' + p.y.toFixed(1) + 'px) scale(' + s.toFixed(4) + ')'; }
    var camara = $('.intro__camara');
    camara.style.transformOrigin = '0 0';

    // ================================================================ A. LA ECONOMÍA SE DIBUJA
    var gr = $('.intro__graticula');
    for (var gy = -500; gy <= 1500; gy += 90) el('line', { x1: -1500, x2: 3200, y1: gy, y2: gy, stroke: 'rgba(252,242,220,.055)', 'stroke-width': .7 }, gr);
    for (var gx = -1500; gx <= 3200; gx += 80) el('line', { y1: -500, y2: 1500, x1: gx, x2: gx, stroke: 'rgba(252,242,220,.055)', 'stroke-width': .7 }, gr);
    anima(gr, [{ opacity: 0 }, { opacity: 1 }], { duration: 1600, delay: 200 });

    var BASE = 790, K = 0.62;                              // línea de base y escala de las barras
    var conv = [300, 340, 360, 340, 310, 270, 235, 205, 180], cen = [110, 200, 310, 390, 430, 425, 400, 370, 340];
    var prod = [28, 40, 58, 78, 94, 104, 108, 110, 109];
    var gB = $('.intro__barras'), gC = $('.intro__curva'), gF = $('.intro__flujo'), gN = $('.intro__notas');
    var BARRIDO_INI = 600, BARRIDO = 2500;                 // ms: cuándo empieza y cuánto dura el barrido oeste → este
    var CONVC = 'rgba(248,198,10,', CENC = 'rgba(0,173,181,';

    // eje
    el('line', { x1: 90, x2: 1560, y1: BASE, y2: BASE, stroke: 'rgba(252,242,220,.35)', 'stroke-width': 1.2 }, gB);
    var pts = [];
    conv.forEach(function (_, i) {
        var x = 150 + i * 156, w = 96, hC = conv[i] * K, hN = cen[i] * K, t0 = BARRIDO_INI + (i / (conv.length - 1)) * BARRIDO + azar() * 120;
        var b1 = el('rect', { x: x, y: BASE - hC, width: w, height: hC, fill: CONVC + '.55)', stroke: 'rgba(255,236,150,.95)', 'stroke-width': 1.4 }, gB);
        var b2 = el('rect', { x: x, y: BASE - hC - hN, width: w, height: hN, fill: CENC + '.50)', stroke: 'rgba(170,250,255,.95)', 'stroke-width': 1.4 }, gB);
        [b1, b2].forEach(function (b) { b.style.transformBox = 'fill-box'; b.style.transformOrigin = '50% 100%'; b.style.opacity = 0; });
        anima(b1, [{ transform: 'scaleY(0)', opacity: 0 }, { transform: 'scaleY(1)', opacity: 1, offset: .35 }, { transform: 'scaleY(1)', opacity: 1 }], { duration: 1100, delay: t0, easing: 'cubic-bezier(.2,.7,.2,1)' });
        anima(b2, [{ transform: 'scaleY(0)', opacity: 0 }, { transform: 'scaleY(1)', opacity: 1, offset: .35 }, { transform: 'scaleY(1)', opacity: 1 }], { duration: 1100, delay: t0 + 260, easing: 'cubic-bezier(.2,.7,.2,1)' });
        var ye = el('text', { x: x + w / 2, y: BASE + 36, 'text-anchor': 'middle', fill: 'rgba(252,242,220,.55)', 'font-size': 22, 'font-weight': 700 }, gB);
        ye.textContent = CIFRAS.anio + i; ye.style.opacity = 0;
        anima(ye, [{ opacity: 0 }, { opacity: 1 }], { duration: 600, delay: t0 + 300 });
        pts.push([x + w / 2, 640 - (prod[i] - 28) / 82 * 330]);
    });

    // curva de producción: Catmull-Rom → Bézier; se dibuja y después corre el fluido por dentro
    var d = 'M' + pts[0][0] + ' ' + pts[0][1];
    for (var k = 0; k < pts.length - 1; k++) {
        var p0 = pts[Math.max(0, k - 1)], p1 = pts[k], p2 = pts[k + 1], p3 = pts[Math.min(pts.length - 1, k + 2)];
        d += ' C' + (p1[0] + (p2[0] - p0[0]) / 6).toFixed(1) + ' ' + (p1[1] + (p2[1] - p0[1]) / 6).toFixed(1) + ',' + (p2[0] - (p3[0] - p1[0]) / 6).toFixed(1) + ' ' + (p2[1] - (p3[1] - p1[1]) / 6).toFixed(1) + ',' + p2[0] + ' ' + p2[1];
    }
    var T_CURVA = 900, DUR_CURVA = 2300;
    var base = el('path', { d: d, pathLength: 1, fill: 'none', stroke: 'rgba(242,96,84,.26)', 'stroke-width': 9, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, gC);
    var fino = el('path', { d: d, pathLength: 1, fill: 'none', stroke: 'rgba(255,150,138,.98)', 'stroke-width': 3, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, gC);
    [base, fino].forEach(function (n) { n.style.strokeDasharray = 1; n.style.strokeDashoffset = 1; });
    anima(base, [{ strokeDashoffset: 1, opacity: 0 }, { strokeDashoffset: 0, opacity: 1 }], { duration: DUR_CURVA, delay: T_CURVA, easing: 'cubic-bezier(.5,0,.2,1)' });
    anima(fino, [{ strokeDashoffset: 1 }, { strokeDashoffset: 0 }], { duration: DUR_CURVA, delay: T_CURVA, easing: 'cubic-bezier(.5,0,.2,1)' });
    var flujo = el('path', { d: d, pathLength: 1, fill: 'none', stroke: '#FFE9E4', 'stroke-width': 3.4, 'stroke-linecap': 'round' }, gF);
    flujo.style.strokeDasharray = '0.012 0.07'; flujo.style.opacity = 0;
    anima(flujo, [{ opacity: 0 }, { opacity: .95 }], { duration: 700, delay: T_CURVA + DUR_CURVA });
    anima(flujo, [{ strokeDashoffset: 0 }, { strokeDashoffset: -0.082 }], { duration: 1500, delay: T_CURVA + DUR_CURVA, iterations: Infinity, fill: 'none' });

    // el pico de inversión: ping y rótulo
    var pk = 4, pkx = 150 + pk * 156 + 48, pky = BASE - (conv[pk] + cen[pk]) * K;
    [0, 420].forEach(function (retraso) {
        var c = el('circle', { cx: pkx, cy: pky, r: 6, fill: 'none', stroke: '#FFF1B0', 'stroke-width': 2 }, gN); c.style.opacity = 0;
        c.style.transformBox = 'fill-box'; c.style.transformOrigin = 'center';
        anima(c, [{ opacity: .95, transform: 'scale(1)' }, { opacity: 0, transform: 'scale(9)' }], { duration: 1500, delay: 2800 + retraso, easing: 'cubic-bezier(.1,.6,.3,1)' });
    });
    var nota = el('g', {}, gN); nota.style.opacity = 0;
    el('line', { x1: pkx, y1: pky - 10, x2: pkx, y2: pky - 78, stroke: 'rgba(255,241,176,.8)', 'stroke-width': 1.6, 'stroke-dasharray': '5 5' }, nota);
    var tn = el('text', { x: pkx, y: pky - 92, 'text-anchor': 'middle', fill: '#FFF1B0', 'font-size': 26, 'font-weight': 800 }, nota);
    tn.textContent = 'Pico de inversión · ' + (CIFRAS.anio + pk);
    anima(nota, [{ opacity: 0, transform: 'translateY(10px)' }, { opacity: 1, transform: 'none' }], { duration: 800, delay: 3000, easing: 'ease-out' });

    // barrido de luz que recorre el gráfico de oeste a este (acompaña la subida de las barras)
    anima($('.intro__barrido'), [{ transform: 'translateX(0px)', opacity: 0 }, { opacity: 1, offset: .1 }, { opacity: 1, offset: .85 },
        { transform: 'translateX(' + (CW + 200) + 'px)', opacity: 0 }], { duration: BARRIDO + 500, delay: BARRIDO_INI - 150, easing: 'linear' });

    // cámara: arranca sobre el tramo de mayor inversión y se aleja hasta mostrar todo el gráfico; después, una deriva lentísima
    anima(camara, [{ transform: mat(ini, s0) }, { transform: mat(fin, s1) }], { duration: 4700, delay: 500, easing: 'cubic-bezier(.45,.02,.18,1)' });
    anima(camara, [{ transform: mat(fin, s1) }, { transform: mat({ x: fin.x + 8, y: fin.y - 4 }, s1 * 1.012) }], { duration: FIN - 5200, delay: 5200, easing: 'linear', fill: 'forwards' });
    // el gráfico pasa a ser el fondo mientras corren las palabras, y vuelve a brillar en el cierre
    anima(camara, [{ opacity: 1 }, { opacity: .13 }], { duration: 700, delay: T_DIM, easing: 'ease-in-out' });
    anima(camara, [{ opacity: .13 }, { opacity: 1 }], { duration: 1300, delay: T_TEXTO - 300, easing: 'ease-out', fill: 'forwards' });

    // ping inicial (anillos de radar en el centro del encuadre)
    var pg = $('.intro__pings');
    [0, 380].forEach(function (retraso) {
        var c = el('circle', { cx: W * .5, cy: H * .5, r: 4, fill: 'none', stroke: '#FFF1B0', 'stroke-width': 1.4 }, pg);
        c.style.opacity = 0;
        anima(c, [{ opacity: .9, r: 4 }, { opacity: 0, r: Math.min(W, H) * .22 }], { duration: 1500, delay: 250 + retraso, easing: 'cubic-bezier(.1,.6,.3,1)' });
    });
    var punto = el('circle', { cx: W * .5, cy: H * .5, r: 3.2, fill: '#FFFBE6' }, pg); punto.style.opacity = 0;
    anima(punto, [{ opacity: 0, r: 1 }, { opacity: 1, r: 3.4, offset: .25 }, { opacity: 1, r: 3.4, offset: .7 }, { opacity: 0, r: 2 }], { duration: 2400, delay: 150 });

    // barras de cine
    anima($('.intro__barra--sup'), [{ transform: 'translateY(-100%)' }, { transform: 'translateY(0)' }], { duration: 900, easing: 'cubic-bezier(.2,.7,.2,1)' });
    anima($('.intro__barra--inf'), [{ transform: 'translateY(100%)' }, { transform: 'translateY(0)' }], { duration: 900, easing: 'cubic-bezier(.2,.7,.2,1)' });

    // ================================================================ B. LAS PALABRAS DEL SISTEMA
    todos('.intro__pal').forEach(function (p, i) {
        var texto = p.textContent; p.textContent = '';
        var letras = texto.split('').map(function (ch) { var s = document.createElement('i'); s.textContent = ch; p.appendChild(s); return s; });
        var t = T_C + i * PASO_C;
        anima(p, [{ visibility: 'visible' }, { visibility: 'visible' }], { duration: PASO_C + 20, delay: t, fill: 'none' });
        letras.forEach(function (l, j) {
            anima(l, [{ transform: 'translateY(62%) scale(1.06)', opacity: 0, filter: 'blur(14px)' }, { transform: 'translateY(0) scale(1)', opacity: 1, filter: 'blur(0)' }],
                { duration: 400, delay: t + j * 16, easing: 'cubic-bezier(.16,.9,.2,1)', fill: 'backwards' });
            anima(l, [{ transform: 'translateY(0)', opacity: 1, filter: 'blur(0)' }, { transform: 'translateY(-34%)', opacity: 0, filter: 'blur(10px)' }],
                { duration: 160, delay: t + PASO_C - 105 + j * 6, easing: 'ease-in', fill: 'forwards' });
        });
    });

    // ================================================================ C. EL CIERRE
    anima($('.intro__marca'), [{ opacity: 0, transform: 'translateY(14px)' }, { opacity: 1, transform: 'none' }], { duration: 900, delay: T_TEXTO - 300, easing: 'cubic-bezier(.2,.7,.2,1)' });
    todos('.intro__palabra').forEach(function (p, i) {
        var t = T_TEXTO + 200 + i * 240;
        anima(p, [{ transform: 'translateY(115%)', opacity: 0, filter: 'blur(14px)', letterSpacing: '.16em' }, { transform: 'translateY(0)', opacity: 1, filter: 'blur(0)', letterSpacing: '-.03em' }],
            { duration: 1200, delay: t, easing: 'cubic-bezier(.16,.9,.2,1)' });
        anima(p, [{ backgroundPosition: '130% 0' }, { backgroundPosition: '-30% 0' }], { duration: 1400, delay: t + 800, easing: 'ease-in-out', composite: 'replace' });
    });
    anima($('.intro__filete'), [{ width: 0 }, { width: 'min(22vw, 220px)' }], { duration: 1000, delay: T_TEXTO + 800, easing: 'cubic-bezier(.2,.7,.2,1)' });
    anima($('.intro__lema'), [{ opacity: 0, transform: 'translateY(10px)' }, { opacity: 1, transform: 'none' }], { duration: 800, delay: T_TEXTO + 1000, easing: 'ease-out' });
    var T_CIFRAS = T_TEXTO + 1300, DUR_CIFRAS = 1700;
    todos('.intro__cifra').forEach(function (c, i) {
        anima(c, [{ opacity: 0, transform: 'translateY(12px)' }, { opacity: 1, transform: 'none' }], { duration: 650, delay: T_CIFRAS + i * 150, easing: 'ease-out' });
    });

    // ---------------------------------------------------------------- números que cuentan
    var fmt = new Intl.NumberFormat('es-AR');
    function frena(t) { t = Math.max(0, Math.min(1, t)); return 1 - Math.pow(1 - t, 3); }
    function pintar(ms) {
        todos('[data-n]').forEach(function (b, i) {
            var n = b.getAttribute('data-n'), f = frena((ms - (T_CIFRAS + i * 150)) / DUR_CIFRAS);
            if (n === 'anio') b.textContent = String(Math.round(CIFRAS.anio - 8 + 8 * f));      // el año "corre" hasta la convocatoria vigente
            else b.textContent = fmt.format(Math.round(CIFRAS[n] * f));
        });
    }
    // sin las cifras de Legajos (servicio caído) se ocultan esas dos y queda la convocatoria
    if (!CIFRAS.empresas) todos('[data-n="empresas"], [data-n="areas"]').forEach(function (b) { b.parentNode.style.display = 'none'; });
    var saltar = $('.intro__saltar');
    anima(saltar, [{ opacity: 0 }, { opacity: 1 }], { duration: 600, delay: 1200 });

    // ---------------------------------------------------------------- reloj
    var raf = 0, terminada = false;
    function terminar(ms) {
        if (terminada) return; terminada = true; cancelAnimationFrame(raf);
        document.removeEventListener('keydown', alTeclado);
        root.dispatchEvent(new CustomEvent('intro:fin', { bubbles: true }));
        desvanecer(ms);
    }
    function alTeclado(e) { if (e.key === 'Escape') terminar(350); }
    document.addEventListener('keydown', alTeclado);
    saltar.addEventListener('click', function () { terminar(350); });

    var fijo = q.get('introT');                       // cuadro fijo, para revisar la animación: ?intro=1&introT=3.5
    if (fijo !== null) {
        var ms = parseFloat(fijo) * 1000;
        anims.forEach(function (a) { a.pause(); a.currentTime = ms; });
        pintar(ms);
        return;
    }
    var t0 = performance.now();
    (function paso() {
        var ms = performance.now() - t0;
        pintar(ms);
        if (ms >= FIN) { terminar(700); return; }
        raf = requestAnimationFrame(paso);
    })();
})();
