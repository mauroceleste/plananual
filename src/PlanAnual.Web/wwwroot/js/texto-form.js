// Paso "Restricciones a la actividad": los cuatro textos se guardan solos al salir de cada cuadro.
(function () {
    'use strict';
    var form = document.getElementById('form-texto');
    if (!form || form.dataset.ro === '1') return;
    var estado = document.getElementById('estado-texto');
    var csrf = (document.querySelector('meta[name=csrf]') || {}).content;
    var enVuelo = null;

    function mostrar(texto, clase) {
        if (!estado) return;
        estado.className = 'guardado' + (clase ? ' ' + clase : '');
        estado.querySelector('span').textContent = texto;
    }

    function guardar() {
        var datos = new FormData(form);
        datos.append('ajax', 'true');
        mostrar('Guardando…', 'is-saving');
        enVuelo = fetch(form.dataset.ajax, {
            method: 'POST', body: datos, credentials: 'same-origin',
            headers: { 'RequestVerificationToken': csrf || '', 'X-Requested-With': 'XMLHttpRequest' }
        }).then(function (r) {
            if (r.status === 401) { mostrar('La sesión venció. Volvé a ingresar desde el portal.', 'is-error'); return null; }
            if (r.status === 400) return r.json().then(function (j) { mostrar(j.error || 'No se pudo guardar.', 'is-error'); return null; });
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
        }).then(function (j) { if (j && j.ok) mostrar('Guardado a las ' + j.hora, 'is-ok'); })
          .catch(function () { mostrar('Sin conexión: no se guardó. Probá de nuevo.', 'is-error'); })
          .then(function () { enVuelo = null; });
    }

    form.addEventListener('change', function (e) { if (e.target.matches('textarea')) guardar(); });

    // No perder lo escrito al cambiar de paso: se espera a que termine el guardado en curso.
    document.querySelectorAll('a[data-flush]').forEach(function (a) {
        a.addEventListener('click', function (e) {
            if (!enVuelo) return;
            e.preventDefault();
            enVuelo.then(function () { window.location.href = a.href; });
        });
    });
})();
