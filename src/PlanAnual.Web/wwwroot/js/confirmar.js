// Pide confirmación en los formularios con data-confirmar="¿Seguro?".
// Va en captura y corta la propagación: así, si se cancela, data-una-vez (nqn-ui.js) no deshabilita los botones.
document.addEventListener('submit', function (ev) {
    var f = ev.target, msg = f && f.dataset ? f.dataset.confirmar : null;
    if (msg && !window.confirm(msg)) { ev.preventDefault(); ev.stopPropagation(); }
}, true);
