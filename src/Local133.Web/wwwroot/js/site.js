// JS común del sitio. Mantenerlo ligero: solo lo que una vista Razor no resuelve en servidor
// (teclado numérico, carrito, cuenta regresiva del QR y SignalR llegan con sus fases).

// form[data-disable-on-submit]: deshabilita el botón al enviar para evitar un doble envío (p. ej. un alta en Pey,
// que no es idempotente). Si la validación en cliente cancela el envío, el evento no llega aquí.
document.addEventListener('submit', function (event) {
    var form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-disable-on-submit') || event.defaultPrevented) {
        return;
    }
    // Un botón deshabilitado no se envía: si el que se pulsó trae name/value (p. ej. provider=Google),
    // se copia a un campo oculto para que el servidor lo siga recibiendo.
    var submitter = event.submitter;
    if (submitter && submitter.name && !submitter.disabled) {
        var hidden = form.querySelector('input[type="hidden"][data-submitter-value]');
        if (!hidden) {
            hidden = document.createElement('input');
            hidden.type = 'hidden';
            hidden.setAttribute('data-submitter-value', '');
            form.appendChild(hidden);
        }
        hidden.name = submitter.name;
        hidden.value = submitter.value;
    }
    form.querySelectorAll('button[type="submit"]').forEach(function (button) {
        button.disabled = true;
        button.setAttribute('aria-busy', 'true');
    });
});

// Al volver con "atrás" (bfcache) el botón no debe quedarse deshabilitado.
window.addEventListener('pageshow', function () {
    document.querySelectorAll('form[data-disable-on-submit] button[type="submit"][aria-busy]').forEach(function (button) {
        button.disabled = false;
        button.removeAttribute('aria-busy');
    });
});
