// NQN UI: tema claro/oscuro, menu movil, texto largo plegable y confirmacion de baja.
(function () {
    'use strict';

    // ---- Tema (el valor inicial se aplica en el <head> para evitar el parpadeo) ----
    var boton = document.querySelector('[data-theme-toggle]');
    if (boton) {
        boton.addEventListener('click', function () {
            var actual = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'dark' : 'light';
            var nuevo = actual === 'dark' ? 'light' : 'dark';
            document.documentElement.setAttribute('data-bs-theme', nuevo);
            try { localStorage.setItem('legajo-tema', nuevo); } catch (e) { /* sin almacenamiento: solo dura la visita */ }
            boton.setAttribute('aria-pressed', nuevo === 'dark' ? 'true' : 'false');
        });
    }

    // ---- Menu lateral: se oculta/muestra (se recuerda) y en pantallas chicas es un cajon ----
    var raiz = document.documentElement;
    var botonLado = document.querySelector('[data-side-toggle]');
    var chico = window.matchMedia('(max-width: 991.98px)');
    function abierto() { return chico.matches ? raiz.classList.contains('side-open') : raiz.getAttribute('data-side') !== 'closed'; }
    function reflejar() {
        if (!botonLado) return;
        var a = abierto();
        botonLado.setAttribute('aria-expanded', a ? 'true' : 'false');
        botonLado.setAttribute('aria-label', a ? 'Ocultar el menú lateral' : 'Mostrar el menú lateral');
    }
    if (botonLado) {
        botonLado.addEventListener('click', function () {
            if (chico.matches) { raiz.classList.toggle('side-open'); }
            else {
                var cerrar = abierto();
                if (cerrar) raiz.setAttribute('data-side', 'closed'); else raiz.removeAttribute('data-side');
                try { localStorage.setItem('legajo-menu', cerrar ? 'closed' : 'open'); } catch (e) { /* solo dura la visita */ }
            }
            reflejar();
        });
        document.querySelectorAll('[data-side-close]').forEach(function (el) {
            el.addEventListener('click', function () { raiz.classList.remove('side-open'); reflejar(); });
        });
        document.addEventListener('keydown', function (ev) {
            if (ev.key === 'Escape' && raiz.classList.contains('side-open')) { raiz.classList.remove('side-open'); reflejar(); botonLado.focus(); }
        });
        if (chico.addEventListener) chico.addEventListener('change', function () { raiz.classList.remove('side-open'); reflejar(); });
        reflejar();
    }

    // ---- Texto completo largo: se muestra recortado con un boton para expandirlo ----
    document.querySelectorAll('[data-clamp]').forEach(function (el) {
        if (el.scrollHeight <= el.clientHeight + 40) { el.classList.add('is-open'); return; }
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn btn-outline-secondary btn-sm clamp-btn';
        btn.textContent = 'Leer el texto completo';
        btn.addEventListener('click', function () {
            var abierto = el.classList.toggle('is-open');
            btn.textContent = abierto ? 'Mostrar menos' : 'Leer el texto completo';
            if (!abierto) el.scrollIntoView({ block: 'start', behavior: 'smooth' });
        });
        el.insertAdjacentElement('afterend', btn);
    });

    // ---- Confirmacion de baja de una norma (modal) ----
    var modal = document.getElementById('modalEliminar');
    if (modal) {
        modal.addEventListener('show.bs.modal', function (ev) {
            var origen = ev.relatedTarget;
            if (!origen) return;
            var form = modal.querySelector('form');
            form.action = form.dataset.plantilla.replace('__ID__', encodeURIComponent(origen.dataset.id));
            modal.querySelector('[data-modal-id]').textContent = origen.dataset.id;
            modal.querySelector('[data-modal-titulo]').textContent = origen.dataset.titulo;
        });
    }

    // ---- Los formularios que se envian una vez no deben poder duplicarse con doble clic ----
    document.querySelectorAll('form[data-una-vez]').forEach(function (f) {
        f.addEventListener('submit', function () {
            f.querySelectorAll('button[type=submit]').forEach(function (b) { b.disabled = true; });
        });
    });
})();

// Menú de la barra superior en pantallas chicas.
(function () {
    'use strict';
    var b = document.querySelector('[data-reg-menu]'), m = document.getElementById('reg-menu');
    if (!b || !m) return;
    b.addEventListener('click', function () {
        var abierto = m.classList.toggle('is-open');
        b.setAttribute('aria-expanded', abierto ? 'true' : 'false');
    });
})();
