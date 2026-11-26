// Service worker de Local133 (ajustes 2026-11-26, punto 12). Hace instalable el sitio y muestra offline.html cuando una
// navegación falla por falta de red. NO guarda datos: solo precarga la página sin conexión y su logo en el install, y nunca
// escribe en la caché lo que responde el sitio (/app, /admin, /api, /hubs, /Identity, /recibo…). El offline real sigue
// fuera del MVP. Va en la raíz para tener scope "/"; MapStaticAssets lo sirve con Cache-Control: no-cache.
// Al cambiar la lista de PRECACHE o offline.html, subir la versión de CACHE.
'use strict';

var CACHE = 'l133-offline-v1';
var OFFLINE_URL = '/offline.html';
// El favicon también: el navegador lo pide solo y, sin red, dejaba un error en la consola.
var PRECACHE = [OFFLINE_URL, '/favicon.ico', '/img/local133-logo-160.png', '/img/local133-logo-320.png'];

self.addEventListener('install', function (event) {
    event.waitUntil(
        caches.open(CACHE)
            .then(function (cache) { return cache.addAll(PRECACHE.map(function (url) { return new Request(url, { cache: 'reload' }); })); })
            .then(function () { return self.skipWaiting(); })
    );
});

self.addEventListener('activate', function (event) {
    event.waitUntil(
        caches.keys()
            .then(function (keys) {
                return Promise.all(keys
                    .filter(function (key) { return key.indexOf('l133-') === 0 && key !== CACHE; })
                    .map(function (key) { return caches.delete(key); }));
            })
            .then(function () { return self.clients.claim(); })
    );
});

self.addEventListener('fetch', function (event) {
    var request = event.request;
    if (request.method !== 'GET') {
        return;
    }
    var url = new URL(request.url);
    if (url.origin !== self.location.origin) {
        return;
    }

    // Navegaciones: siempre a la red; sin red, la página sin conexión.
    if (request.mode === 'navigate') {
        event.respondWith(fetch(request).catch(function () {
            return caches.match(OFFLINE_URL).then(function (response) {
                return response || Response.error();
            });
        }));
        return;
    }

    // Lo que usa offline.html: red y, sin red, la copia precargada.
    if (PRECACHE.indexOf(url.pathname) !== -1) {
        event.respondWith(fetch(request).catch(function () {
            return caches.match(url.pathname).then(function (response) {
                return response || Response.error();
            });
        }));
    }
    // Todo lo demás pasa directo a la red, sin caché.
});
