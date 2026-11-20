// Comunicaciones con la empresa: el formulario de registro muestra solo los campos que corresponden a cada tipo de movimiento.
(function () {
    'use strict';
    var modalEl = document.getElementById('modal-registro');
    if (modalEl) {
        var modal = new bootstrap.Modal(modalEl);
        var tipo = document.getElementById('r-tipo');
        var textosPlazo = {
            NotaEnviada: ['Plazo de respuesta', 'Hasta cuándo tiene que contestar la empresa. Sin fecha, la nota no queda como pendiente.'],
            ProrrogaOtorgada: ['Nuevo plazo de presentación', 'Es el nuevo límite de esta empresa: reemplaza al general mientras sea posterior.']
        };
        function aplicar() {
            var t = tipo.value;
            modalEl.querySelectorAll('[data-si]').forEach(function (el) { el.hidden = el.dataset.si.split(' ').indexOf(t) < 0; });
            var tp = textosPlazo[t];
            if (tp) { document.getElementById('r-plazo-et').textContent = tp[0]; document.getElementById('r-plazo-ayuda').textContent = tp[1]; }
            var plazo = document.getElementById('r-plazo'); plazo.required = (t === 'ProrrogaOtorgada');
            var titulo = tipo.options[tipo.selectedIndex].text;
            document.getElementById('registro-titulo').textContent = titulo;
        }
        tipo.addEventListener('change', aplicar);
        document.querySelectorAll('[data-registrar]').forEach(function (b) {
            b.addEventListener('click', function () { tipo.value = b.dataset.registrar; aplicar(); modal.show(); });
        });
        aplicar();
        // «Enviar mail»: se muestran los destinatarios solo si se tilda.
        var mail = document.getElementById('r-mail'), datos = document.getElementById('r-mail-datos');
        if (mail) mail.addEventListener('change', function () { datos.hidden = !mail.checked; });
    }
    var xm = document.getElementById('x-mail'), xd = document.getElementById('x-mail-datos');
    if (xm) xm.addEventListener('change', function () { xd.hidden = !xm.checked; });
})();

// Empresa: «Responder» abre el formulario con la nota elegida.
(function () {
    'use strict';
    var el = document.getElementById('modal-responder');
    if (!el) return;
    var modal = new bootstrap.Modal(el);
    document.querySelectorAll('[data-responder]').forEach(function (b) {
        b.addEventListener('click', function () {
            document.getElementById('resp-nota').value = b.dataset.responder;
            document.getElementById('resp-asunto').textContent = 'Nota: ' + b.dataset.asunto;
            modal.show();
        });
    });
})();

// Administración: una nota solo puede contestar un pedido de prórroga; se muestran solo esas opciones.
(function () {
    'use strict';
    var tipo = document.getElementById('r-tipo'), resp = document.getElementById('r-resp');
    if (!tipo || !resp) return;
    function filtrar() {
        Array.prototype.forEach.call(resp.options, function (o) {
            if (!o.value) return;
            o.hidden = tipo.value === 'NotaEnviada' && o.dataset.tipo !== 'ProrrogaSolicitada';
        });
        if (resp.selectedOptions[0] && resp.selectedOptions[0].hidden) resp.value = '';
    }
    tipo.addEventListener('change', filtrar); filtrar();
})();
