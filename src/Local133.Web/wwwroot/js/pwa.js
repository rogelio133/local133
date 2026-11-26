// PWA (ajustes 2026-11-26, punto 12). Solo se carga en las pantallas de la app (ViewData["Pwa"]): vendedor, admin, home e
// Identity; nunca en /recibo ni en /tienda.
// 1. Registra /sw.js (ruta fija, sin huella, para que el service worker no cambie de URL en cada versión).
// 2. Tarjeta «Instalar app» de «Más» ([data-pwa-install], oculta por defecto):
//    - Android y escritorio: aparece al llegar beforeinstallprompt y lanza el diálogo del navegador al tocarla;
//    - iOS (no tiene ese evento): aparece siempre que no esté instalada y abre el modal con las instrucciones;
//    - ya instalada (display-mode: standalone) o sin soporte (Firefox, Safari de escritorio): se queda oculta.
(function () {
    'use strict';

    if ('serviceWorker' in navigator) {
        window.addEventListener('load', function () {
            navigator.serviceWorker.register('/sw.js').catch(function (error) {
                console.warn('No se pudo registrar el service worker.', error);
            });
        });
    }

    var item = document.querySelector('[data-pwa-install]');
    if (!item) {
        return;
    }
    var button = item.querySelector('[data-more-install]');
    var standalone = window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
    if (!button || standalone) {
        return;
    }

    var isIos = /iphone|ipad|ipod/i.test(navigator.userAgent) ||
        (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    var deferredPrompt = null;

    function show() { item.hidden = false; }
    function hide() { item.hidden = true; }

    window.addEventListener('beforeinstallprompt', function (event) {
        event.preventDefault();
        deferredPrompt = event;
        show();
    });

    window.addEventListener('appinstalled', function () {
        deferredPrompt = null;
        hide();
    });

    if (isIos) {
        show();
    }

    button.addEventListener('click', function () {
        if (deferredPrompt) {
            var prompt = deferredPrompt;
            deferredPrompt = null;
            prompt.prompt();
            // El evento solo sirve una vez: instale o no, la tarjeta se oculta hasta que el navegador mande otro
            // beforeinstallprompt.
            prompt.userChoice.then(hide, hide);
            return;
        }
        var modal = document.querySelector('[data-pwa-ios-modal]');
        if (isIos && modal && window.bootstrap) {
            window.bootstrap.Modal.getOrCreateInstance(modal).show();
        }
    });
})();
