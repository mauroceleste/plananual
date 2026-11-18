// Listado de Administración (Plan?cuit=...): modales de adjuntos y de observaciones internas de cada área.
(function () {
    'use strict';
    var el = document.getElementById('modal-area');
    if (!el) return;
    var modal = new bootstrap.Modal(el);
    var titulo = document.getElementById('modal-area-titulo');
    var cuerpo = document.getElementById('modal-area-cuerpo');
    var csrf = (document.querySelector('meta[name=csrf]') || {}).content || '';
    var botonObs = null;   // el botón de observaciones que abrió el modal (para actualizar su contador)

    function cargando() { cuerpo.innerHTML = '<p class="text-muted mb-0">Cargando…</p>'; }
    function error(txt) { cuerpo.innerHTML = '<div class="alert alert-danger mb-0"></div>'; cuerpo.firstChild.textContent = txt; }

    function pedir(url, opciones) {
        return fetch(url, Object.assign({ credentials: 'same-origin', headers: { 'RequestVerificationToken': csrf, 'X-Requested-With': 'XMLHttpRequest' } }, opciones || {}))
            .then(function (r) {
                if (r.status === 401) throw new Error('La sesión venció. Volvé a ingresar desde el portal.');
                if (r.status === 403) throw new Error('No tenés permiso para ver esto.');
                if (r.status === 400) return r.json().then(function (j) { throw new Error(j.error || 'No se pudo completar.'); });
                if (!r.ok) throw new Error('No se pudo cargar (HTTP ' + r.status + ').');
                return r.text();
            });
    }

    function mostrarObservaciones(html) {
        cuerpo.innerHTML = html;
        var raiz = cuerpo.querySelector('[data-cantidad]');
        if (raiz && botonObs) {
            var n = parseInt(raiz.dataset.cantidad, 10) || 0;
            var badge = botonObs.querySelector('[data-obs-n]');
            badge.textContent = n; badge.hidden = n === 0;
            botonObs.classList.toggle('border-warning', n > 0);
            botonObs.title = n > 0 ? 'Ver observaciones' : 'Agregar observación';
        }
    }

    document.addEventListener('click', function (e) {
        var adj = e.target.closest('[data-adjuntos]');
        if (adj) {
            titulo.textContent = 'Adjuntos · ' + adj.dataset.area; cargando(); modal.show(); botonObs = null;
            pedir('Plan/AdjuntosArea/' + encodeURIComponent(adj.dataset.adjuntos)).then(function (h) { cuerpo.innerHTML = h; }).catch(function (x) { error(x.message); });
            return;
        }
        var obs = e.target.closest('[data-obs]');
        if (obs) {
            botonObs = obs; titulo.textContent = 'Observaciones · ' + obs.dataset.area; cargando(); modal.show();
            pedir('Plan/ObservacionesArea?cuit=' + encodeURIComponent(obs.dataset.cuit) + '&idBloque=' + encodeURIComponent(obs.dataset.obs))
                .then(mostrarObservaciones).catch(function (x) { error(x.message); });
            return;
        }
        var hist = e.target.closest('[data-hist-area]');
        if (hist) {
            botonObs = null; titulo.textContent = 'Historial de modificaciones · ' + hist.dataset.area; cargando(); modal.show();
            pedir('Plan/HistorialArea?cuit=' + encodeURIComponent(hist.dataset.cuit) + '&idBloque=' + encodeURIComponent(hist.dataset.histArea))
                .then(function (h) { cuerpo.innerHTML = h; }).catch(function (x) { error(x.message); });
            return;
        }
        var his = e.target.closest('[data-historial]');
        if (his) {
            botonObs = null; titulo.textContent = 'Historial del expediente · ' + his.dataset.area; modal.show();
            cuerpo.innerHTML = '<div class="d-flex align-items-center gap-2 text-muted"><span class="spinner-border spinner-border-sm" aria-hidden="true"></span><span>Consultando GDE… puede tardar unos segundos.</span></div>';
            pedir('Plan/HistorialExpediente?numero=' + encodeURIComponent(his.dataset.historial)).then(function (h) { cuerpo.innerHTML = h; }).catch(function (x) { error(x.message); });
            return;
        }
        var todas = e.target.closest('[data-mostrar-todas]');
        if (todas) {
            todas.parentElement.querySelectorAll('[data-fila]').forEach(function (tr) { tr.classList.remove('d-none'); });
            todas.remove();
            return;
        }
        var del = e.target.closest('[data-obs-eliminar]');
        if (del && confirm('¿Eliminar esta observación?')) {
            var datos = new FormData(); datos.append('id', del.dataset.obsEliminar);
            pedir('Plan/EliminarObservacion', { method: 'POST', body: datos }).then(mostrarObservaciones).catch(function (x) { error(x.message); });
        }
    });

    cuerpo.addEventListener('submit', function (e) {
        var f = e.target.closest('[data-obs-agregar]');
        if (!f) return;
        e.preventDefault();
        var msg = f.querySelector('[data-obs-error]'); msg.textContent = '';
        pedir('Plan/AgregarObservacion', { method: 'POST', body: new FormData(f) })
            .then(mostrarObservaciones).catch(function (x) { msg.textContent = x.message; });
    });
})();
