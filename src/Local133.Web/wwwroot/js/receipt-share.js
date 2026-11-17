// Recibo compartible ([data-receipt-share]): «Copiar liga» (portapapeles) y «Compartir» (Web Share API, sobre todo en
// celulares). Los botones nacen ocultos y solo se muestran si el navegador los soporta; WhatsApp y «Ver recibo» son
// enlaces normales que funcionan sin JS. En el recibo público, [data-receipt-print] abre el diálogo de impresión.
(function () {
    'use strict';

    document.querySelectorAll('[data-receipt-print]').forEach(function (button) {
        button.addEventListener('click', function () { window.print(); });
    });

    // Escritorio (mouse o trackpad; el mismo criterio que oculta «Tomar foto»): wa.me abre una página intermedia que no
    // siempre detecta la app de escritorio, así que los botones de WhatsApp abren la app con whatsapp:// y se muestra
    // el respaldo de WhatsApp Web. En el celular se queda wa.me, que abre la app.
    var isDesktop = window.matchMedia && window.matchMedia('(hover: hover) and (pointer: fine)').matches;

    document.querySelectorAll('[data-receipt-share]').forEach(function (root) {
        if (isDesktop) {
            root.querySelectorAll('[data-desktop-href]').forEach(function (link) {
                link.setAttribute('href', link.getAttribute('data-desktop-href'));
                // Un protocolo externo en pestaña nueva deja una pestaña vacía: se abre en la misma (la página no cambia).
                link.removeAttribute('target');
            });
            var web = root.querySelector('[data-receipt-whatsapp-web]');
            if (web) {
                web.classList.remove('d-none');
            }
        }

        var url = root.getAttribute('data-receipt-url');
        var message = root.getAttribute('data-receipt-message') || url;
        var copyButton = root.querySelector('[data-receipt-copy]');
        var shareButton = root.querySelector('[data-receipt-native-share]');
        var copied = root.querySelector('[data-receipt-copied]');
        var hideTimer;

        if (copyButton && navigator.clipboard && window.isSecureContext) {
            copyButton.classList.remove('d-none');
            copyButton.addEventListener('click', function () {
                navigator.clipboard.writeText(url).then(function () {
                    if (!copied) {
                        return;
                    }
                    copied.textContent = 'Liga copiada.';
                    copied.classList.remove('d-none', 'text-danger');
                    copied.classList.add('text-success');
                    clearTimeout(hideTimer);
                    hideTimer = setTimeout(function () { copied.classList.add('d-none'); }, 3000);
                }, function () {
                    if (copied) {
                        copied.textContent = 'No pudimos copiar la liga. Usa «Ver recibo» y cópiala desde ahí.';
                        copied.classList.remove('d-none', 'text-success');
                        copied.classList.add('text-danger');
                    }
                });
            });
        }

        if (shareButton && typeof navigator.share === 'function') {
            shareButton.classList.remove('d-none');
            shareButton.addEventListener('click', function () {
                // El mensaje ya trae la liga al final; se manda solo como texto para que no salga repetida.
                navigator.share({ title: 'Recibo de pago', text: message }).catch(function () {
                    // Cancelado por el usuario: no hay nada que hacer.
                });
            });
        }
    });
})();
