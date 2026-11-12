// Pantalla del QR de una venta: dibuja el QR CoDi con QRCode.js a partir de qrString (Pey no genera la imagen:
// generateImage = false) y lleva la cuenta regresiva. Los segundos restantes los calcula el servidor, para no
// depender del reloj del teléfono. Al llegar a 0 la pantalla pasa a "expirado".
(function () {
    'use strict';

    var root = document.querySelector('[data-sale]');
    if (!root) {
        return;
    }

    var box = root.querySelector('[data-sale-qr]');
    var text = box ? box.getAttribute('data-qr-string') : null;
    if (box && text) {
        try {
            box.innerHTML = '';
            // Nivel M: el qrString de CoDi es un JSON largo; con H el QR queda muy denso para cámaras modestas.
            new QRCode(box, {
                text: text,
                width: 256,
                height: 256,
                colorDark: '#000000',
                colorLight: '#ffffff',
                correctLevel: QRCode.CorrectLevel.M
            });
            box.removeAttribute('title'); // QRCode.js pone el contenido como tooltip
            box.setAttribute('data-qr-rendered', 'true');
        } catch (error) {
            box.classList.add('d-none');
            var message = root.querySelector('[data-qr-error]');
            if (message) {
                message.classList.remove('d-none');
            }
            if (window.console) {
                console.error('No se pudo dibujar el QR', error);
            }
        }
    }

    var countdown = root.querySelector('[data-expires-in]');
    if (!countdown) {
        return;
    }

    var seconds = parseInt(countdown.getAttribute('data-expires-in'), 10);
    if (isNaN(seconds)) {
        return;
    }

    var deadline = Date.now() + seconds * 1000;
    var timer = null;

    function pad(n) {
        return n < 10 ? '0' + n : String(n);
    }

    function expire() {
        root.setAttribute('data-expired', 'true');
        root.querySelectorAll('[data-pending-only]').forEach(function (el) { el.classList.add('d-none'); });
        root.querySelectorAll('[data-expired-only]').forEach(function (el) { el.classList.remove('d-none'); });
    }

    function tick() {
        var remaining = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
        countdown.textContent = pad(Math.floor(remaining / 60)) + ':' + pad(remaining % 60);
        if (remaining === 0) {
            if (timer !== null) {
                window.clearInterval(timer);
            }
            expire();
        }
    }

    tick();
    if (seconds > 0) {
        timer = window.setInterval(tick, 1000);
    }
})();
