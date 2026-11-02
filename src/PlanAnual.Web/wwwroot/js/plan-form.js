// Formulario por pasos del Plan de Inversiones: autoguardado, pegado desde Excel, totales y campos calculados.
(function () {
    'use strict';
    var tabla = document.querySelector('table.grilla');
    var estado = document.getElementById('estado-guardado');
    var csrf = (document.querySelector('meta[name=csrf]') || {}).content;
    var ro = !tabla || tabla.dataset.ro === '1';

    var AR = 'es-AR';
    function fmt(n) { return n === null || n === undefined ? '' : n.toLocaleString(AR, { maximumFractionDigits: 6, useGrouping: false }); }
    function fmt2(n) { return n.toLocaleString(AR, { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }

    // Convención es-AR: la coma es decimal y el punto, separador de miles ("1.234,5" y "1.500" valen 1234,5 y 1500).
    // Un punto aislado delante de ceros ("0.125") se toma como decimal.
    function normalizar(txt) {
        txt = (txt || '').replace(/\s/g, '');
        if (txt === '') return '';
        if (txt.indexOf(',') >= 0) txt = txt.replace(/\./g, '').replace(',', '.');
        else if (/^[1-9]\d{0,2}(\.\d{3})+$/.test(txt)) txt = txt.replace(/\./g, '');
        return /^(\d+\.?\d*|\.\d+)$/.test(txt) ? txt : null;     // null: no es un número (letras, signos, etc.)
    }
    function parsear(txt) {
        var n = normalizar(txt);
        return n === '' ? null : n === null ? NaN : parseFloat(n);
    }

    function mostrar(texto, clase) {
        if (!estado) return;
        estado.className = 'guardado' + (clase ? ' ' + clase : '');
        estado.querySelector('span').textContent = texto;
    }

    // ---------- totales ----------
    function recalcularTotales() {
        if (!tabla) return;
        tabla.querySelectorAll('output[data-total]').forEach(function (out) {
            var cod = out.dataset.total, suma = 0, alguno = false;
            tabla.querySelectorAll('tbody tr').forEach(function (tr) {
                var inp = tr.querySelector('input[data-campo="' + cod + '"]');
                var v = null;
                if (inp) v = inp.classList.contains('is-invalid') ? null : parsear(inp.value);
                else { var o = tr.querySelector('output[data-calc="' + cod + '"]'); if (o && o.dataset.v !== undefined && o.dataset.v !== '') v = parseFloat(o.dataset.v); }
                if (v !== null && !isNaN(v)) { suma += v; alguno = true; }
            });
            out.textContent = alguno ? (out.dataset.dec === '0' ? suma.toLocaleString(AR, { maximumFractionDigits: 0 }) : fmt2(suma)) : '';
        });
    }

    // Los calculados que llegaron renderizados por el servidor guardan su valor numérico para los totales.
    if (tabla) tabla.querySelectorAll('output[data-calc]').forEach(function (o) {
        var t = o.textContent.trim();
        if (t) { var n = parsear(t); if (!isNaN(n) && n !== null) o.dataset.v = String(n); }
    });
    recalcularTotales();
    if (ro) return;

    // ---------- cola de guardado ----------
    var pendientes = new Map();
    var timer = null, enVuelo = null;

    function encolar(tr, inp, valor) {
        pendientes.set(tr.dataset.anio + '|' + inp.dataset.campo, { anio: parseInt(tr.dataset.anio, 10), campo: inp.dataset.campo, valor: valor });
        mostrar('Guardando…', 'is-saving');
        clearTimeout(timer);
        timer = setTimeout(enviar, 500);
    }

    function aplicarFilas(filas) {
        Object.keys(filas).forEach(function (anio) {
            var tr = tabla.querySelector('tr[data-anio="' + anio + '"]');
            if (!tr) return;
            tr.querySelectorAll('output[data-calc]').forEach(function (o) {
                var v = filas[anio][o.dataset.calc];
                if (v === undefined || v === null) { o.textContent = ''; o.dataset.v = ''; }
                else { o.textContent = fmt2(v); o.dataset.v = String(v); }
            });
        });
        recalcularTotales();
    }

    function enviar() {
        clearTimeout(timer); timer = null;
        if (enVuelo) return enVuelo;
        if (pendientes.size === 0) return Promise.resolve();
        var lote = Array.from(pendientes.values());
        pendientes.clear();
        enVuelo = fetch(tabla.dataset.guardar, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrf || '', 'X-Requested-With': 'XMLHttpRequest' },
            body: JSON.stringify({ celdas: lote }),
            credentials: 'same-origin'
        }).then(function (r) {
            if (r.status === 401) { mostrar('La sesión venció. Volvé a ingresar desde el portal: lo guardado hasta ahora se conserva.', 'is-error'); return null; }
            if (r.status === 400) return r.json().then(function (j) { mostrar(j.error || 'No se pudo guardar.', 'is-error'); return null; });
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
        }).then(function (j) {
            if (j && j.ok) { aplicarFilas(j.filas); if (pendientes.size === 0) { if (tabla.querySelector('input.is-invalid')) mostrar('Hay celdas con valores inválidos (en rojo): no se guardan hasta que las corrijas.', 'is-error'); else if (tabla.querySelector('input.is-ambiguo')) mostrar('Revisá las celdas en amarillo: el punto se tomó como separador de miles (1.500 = 1500). Para decimales usá coma.', 'is-saving'); else mostrar('Guardado a las ' + j.hora, 'is-ok'); } }
        }).catch(function () {
            // Sin conexión: se vuelve a encolar lo que no fue pisado por una edición más nueva y se reintenta.
            lote.forEach(function (c) { var k = c.anio + '|' + c.campo; if (!pendientes.has(k)) pendientes.set(k, c); });
            mostrar('Sin conexión. Reintentando…', 'is-error');
            timer = setTimeout(enviar, 5000);
        }).then(function () {
            enVuelo = null;
            if (pendientes.size > 0 && !timer) timer = setTimeout(enviar, 200);
        });
        return enVuelo;
    }

    function vaciar() {
        // Espera a que termine lo que está en vuelo y a que se vacíe la cola.
        function paso() { return enviar().then(function () { return (pendientes.size || enVuelo) ? paso() : null; }); }
        return paso();
    }

    // ---------- edición de celdas ----------
    function marcar(inp, ok, msg) {
        inp.classList.toggle('is-invalid', !ok);
        if (ok) inp.removeAttribute('aria-invalid'); else inp.setAttribute('aria-invalid', 'true');
        if (!ok && msg) mostrar(msg, 'is-error');
    }

    function nombreDe(inp) { return (inp.getAttribute('aria-label') || '').replace(/,\s*\d{4}$/, ''); }
    function aplicarTexto(inp, txt) {
        var tr = inp.closest('tr'), v = parsear(txt), dec = parseInt(inp.dataset.dec, 10), max = inp.dataset.max ? parseFloat(inp.dataset.max) : null;
        var f = Math.pow(10, dec);
        function rechazar(msg) { inp.value = txt; inp.classList.remove('is-ambiguo'); marcar(inp, false, nombreDe(inp) + ': ' + msg); }
        if (isNaN(v)) return rechazar('solo admite números (sin letras ni signos). Para decimales usá coma.');
        if (v !== null && dec === 0 && Math.abs(v - Math.round(v)) > 1e-9) return rechazar('debe ser un número entero, sin decimales.');
        if (v !== null && dec > 0 && Math.abs(v * f - Math.round(v * f)) > 1e-6) return rechazar('admite hasta ' + dec + ' decimales.');
        if (v !== null && max !== null && v > max) return rechazar('no puede superar ' + max + '.');
        marcar(inp, true);
        // "1.500" es ambiguo en campos con decimales: lo leemos como 1500 (es-AR) pero lo marcamos para que la persona lo confirme.
        inp.classList.toggle('is-ambiguo', dec > 0 && v !== null && txt.indexOf(',') < 0 && /^[1-9]\d{0,2}(\.\d{3})+$/.test(txt.replace(/\s/g, '')));
        inp.value = fmt(v);
        encolar(tr, inp, v);
        recalcularTotales();
    }

    // Aviso corto cuando se descarta una tecla: vuelve solo al mensaje normal si no hay nada pendiente.
    var tAviso;
    function avisoCorto(msg) {
        mostrar(msg, 'is-error'); clearTimeout(tAviso);
        tAviso = setTimeout(function () {
            if (!tabla.querySelector('input.is-invalid') && !pendientes.size && !enVuelo) mostrar('Los cambios se guardan solos', '');
        }, 3000);
    }

    // Solo se pueden escribir números. Al teclear UNA tecla se descarta si no corresponde:
    // en campos enteros solo dígitos; en decimales, dígitos y coma (el punto se acepta como separador de miles).
    // Si entra más de un carácter de golpe (pegado, dictado, autocorrección) no se descarta todo: se limpia abajo.
    tabla.addEventListener('beforeinput', function (e) {
        var inp = e.target; if (!inp.matches || !inp.matches('input[data-campo]')) return;
        if (e.inputType !== 'insertText' || !e.data || e.data.length !== 1) return;
        var dec = parseInt(inp.dataset.dec, 10);
        if (!(dec === 0 ? /^[0-9]$/ : /^[0-9.,]$/).test(e.data)) {
            e.preventDefault();
            avisoCorto(dec === 0 && /^[.,]$/.test(e.data) ? 'Este campo lleva un número entero: no admite decimales.' : 'Solo se pueden escribir números.');
        }
    });
    // Limpieza de todo lo demás: se conservan los dígitos y los separadores y se descarta el resto (letras, espacios, signos).
    tabla.addEventListener('input', function (e) {
        var inp = e.target; if (!inp.matches || !inp.matches('input[data-campo]')) return;
        var limpio = inp.value.replace(/[^0-9.,]/g, '');
        if (limpio !== inp.value) { inp.value = limpio; avisoCorto('Solo se pueden escribir números.'); }
    });


    tabla.addEventListener('change', function (e) {
        if (e.target.matches('input[data-campo]')) aplicarTexto(e.target, e.target.value);
    });

    // Enter y flechas se mueven por la columna, como en una planilla.
    tabla.addEventListener('keydown', function (e) {
        var inp = e.target; if (!inp.matches || !inp.matches('input[data-campo]')) return;
        var dir = e.key === 'Enter' ? (e.shiftKey ? -1 : 1) : e.key === 'ArrowDown' ? 1 : e.key === 'ArrowUp' ? -1 : 0;
        if (!dir) return;
        var tr = inp.closest('tr'), sib = dir > 0 ? tr.nextElementSibling : tr.previousElementSibling;
        var dest = sib && sib.querySelector('input[data-campo="' + inp.dataset.campo + '"]');
        if (dest) { e.preventDefault(); if (e.key === 'Enter') inp.dispatchEvent(new Event('change', { bubbles: true })); dest.focus(); dest.select(); }
    });

    // Pegado de bloques desde Excel (filas separadas por salto de línea y columnas por tabulación).
    tabla.addEventListener('paste', function (e) {
        var inp = e.target; if (!inp.matches || !inp.matches('input[data-campo]')) return;
        var texto = (e.clipboardData || window.clipboardData).getData('text');
        if (!/[\t\n]/.test(texto.replace(/[\r\n]+$/, ''))) return;   // un solo valor: pegado normal
        e.preventDefault();
        var filas = Array.from(tabla.querySelectorAll('tbody tr'));
        var r0 = filas.indexOf(inp.closest('tr'));
        var c0 = Array.from(inp.closest('tr').querySelectorAll('input[data-campo]')).indexOf(inp);
        texto.replace(/[\r\n]+$/, '').split(/\r?\n/).forEach(function (linea, i) {
            var tr = filas[r0 + i]; if (!tr) return;
            var inputs = tr.querySelectorAll('input[data-campo]');
            linea.split('\t').forEach(function (celda, j) { var dest = inputs[c0 + j]; if (dest) aplicarTexto(dest, celda); });
        });
    });

    // ---------- no perder datos al navegar ----------
    document.querySelectorAll('a[data-flush]').forEach(function (a) {
        a.addEventListener('click', function (e) {
            if (!pendientes.size && !enVuelo) return;
            e.preventDefault();
            vaciar().then(function () { window.location.href = a.href; });
        });
    });
    window.addEventListener('beforeunload', function (e) { if (pendientes.size || enVuelo) { e.preventDefault(); e.returnValue = ''; } });
    document.addEventListener('visibilitychange', function () { if (document.visibilityState === 'hidden' && pendientes.size) enviar(); });
})();
