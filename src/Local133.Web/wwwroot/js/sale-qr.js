// Pantalla de una venta (/app/cobrar/venta/{id}):
// 1. Dibuja el QR CoDi con QRCode.js a partir de qrString (Pey no genera la imagen: generateImage = false).
// 2. Lleva la cuenta regresiva; los segundos restantes los calcula el servidor, para no depender del reloj del
//    teléfono. Al llegar a 0 la pantalla pasa a "expirado".
// 3. Espera el pago: escucha "PaymentReceived" por SignalR (/hubs/ventas, grupo del vendedor) y lo confirma con
//    GET /api/sales/{id}. Mientras SignalR no esté conectado consulta esa misma URL cada 3 s (respaldo), hasta
//    2 minutos después de la expiración. Un pago tardío (venta vencida o cancelada) también cambia la pantalla.
(function () {
    'use strict';

    var POLL_MS = 3000;
    var POLL_AFTER_EXPIRY_MS = 2 * 60 * 1000;

    var root = document.querySelector('[data-sale]');
    if (!root) {
        return;
    }

    var saleId = parseInt(root.getAttribute('data-sale'), 10);
    var statusUrl = root.getAttribute('data-status-url');
    var hubUrl = root.getAttribute('data-hub-url');
    var paid = root.getAttribute('data-sale-status') === 'Paid';

    function showAll(selector, visible) {
        root.querySelectorAll(selector).forEach(function (el) { el.classList.toggle('d-none', !visible); });
    }

    // --- 1. QR ---------------------------------------------------------------------------------------------------
    var box = root.querySelector('[data-sale-qr]');
    var text = box ? box.getAttribute('data-qr-string') : null;
    if (box && text && window.QRCode) {
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

    // --- 2. Cuenta regresiva -------------------------------------------------------------------------------------
    var countdown = root.querySelector('[data-expires-in]');
    var seconds = countdown ? parseInt(countdown.getAttribute('data-expires-in'), 10) : NaN;
    // Sin cuenta regresiva (QR sin expiración, o venta ya vencida/cancelada): se toma "ahora" como el vencimiento.
    var deadline = Date.now() + (isNaN(seconds) ? 0 : seconds * 1000);
    var timer = null;

    function pad(n) {
        return n < 10 ? '0' + n : String(n);
    }

    function expire() {
        if (paid) {
            return;
        }
        root.setAttribute('data-expired', 'true');
        showAll('[data-pending-only]', false);
        showAll('[data-expired-only]', true);
    }

    function tick() {
        var remaining = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
        countdown.textContent = pad(Math.floor(remaining / 60)) + ':' + pad(remaining % 60);
        if (remaining === 0) {
            if (timer !== null) {
                window.clearInterval(timer);
                timer = null;
            }
            expire();
        }
    }

    if (countdown && !isNaN(seconds)) {
        tick();
        if (seconds > 0) {
            timer = window.setInterval(tick, 1000);
        }
    }

    // --- 3. Pago -------------------------------------------------------------------------------------------------
    function showPaid(sale) {
        if (paid) {
            return;
        }
        paid = true;
        stopPolling();
        if (timer !== null) {
            window.clearInterval(timer);
            timer = null;
        }
        root.setAttribute('data-sale-status', 'Paid');
        root.setAttribute('data-expired', 'false');
        showAll('[data-pending-only]', false);
        showAll('[data-expired-only]', false);
        showAll('[data-cancelled-only]', false);
        showAll('[data-sale-notice]', false);
        var paidAt = root.querySelector('[data-paid-at]');
        if (paidAt && sale && sale.paidAtText) {
            // Texto formateado por el servidor (hora del centro de México), igual que al recargar la página.
            paidAt.textContent = sale.paidAtText;
        }
        showAll('[data-paid-only]', true);

        // Cierra la confirmación de cancelar si quedó abierta.
        var modal = document.getElementById('cancel-sale');
        if (modal && window.bootstrap) {
            var instance = window.bootstrap.Modal.getInstance(modal);
            if (instance) {
                instance.hide();
            }
        }

        if (navigator.vibrate) {
            try { navigator.vibrate(200); } catch (e) { /* sin vibración */ }
        }
        if (connection) {
            connection.stop();
        }
    }

    var checking = false;

    function checkStatus() {
        if (paid || checking || !statusUrl || !window.fetch) {
            return;
        }
        checking = true;
        fetch(statusUrl, { headers: { 'Accept': 'application/json' }, credentials: 'same-origin', cache: 'no-store' })
            .then(function (response) {
                if (response.status === 404 || response.status === 401 || response.status === 403) {
                    // La venta ya no existe o la sesión terminó: no tiene caso seguir preguntando.
                    stopPolling();
                    if (connection) {
                        connection.stop();
                    }
                    return null;
                }
                return response.ok ? response.json() : null;
            })
            .then(function (sale) {
                if (sale && sale.status === 'Paid') {
                    showPaid(sale);
                }
            })
            .catch(function () { /* red caída: el siguiente intento lo resuelve */ })
            .then(function () { checking = false; });
    }

    var pollTimer = null;

    function pollWindowOpen() {
        return Date.now() < deadline + POLL_AFTER_EXPIRY_MS;
    }

    function startPolling() {
        if (paid || pollTimer !== null || !pollWindowOpen()) {
            return;
        }
        pollTimer = window.setInterval(function () {
            if (!pollWindowOpen()) {
                stopPolling();
                return;
            }
            checkStatus();
        }, POLL_MS);
    }

    function stopPolling() {
        if (pollTimer !== null) {
            window.clearInterval(pollTimer);
            pollTimer = null;
        }
    }

    var connection = null;

    if (!paid) {
        if (window.signalR && hubUrl) {
            connection = new window.signalR.HubConnectionBuilder()
                .withUrl(hubUrl)
                .withAutomaticReconnect()
                .configureLogging(window.signalR.LogLevel.None)
                .build();

            connection.on('PaymentReceived', function (event) {
                if (event && event.saleId === saleId) {
                    checkStatus();
                }
            });
            connection.onreconnecting(startPolling);
            connection.onreconnected(function () {
                stopPolling();
                checkStatus(); // pudo llegar un pago mientras estaba desconectado
            });
            connection.onclose(function () {
                if (!paid) {
                    startPolling();
                }
            });

            // Mientras conecta, el polling cubre; si conecta, se apaga.
            startPolling();
            connection.start()
                .then(function () {
                    stopPolling();
                    checkStatus();
                })
                .catch(function () { startPolling(); });
        } else {
            startPolling();
        }

        // Al volver a la pestaña (el teléfono se bloqueó) se revisa de inmediato.
        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'visible') {
                checkStatus();
            }
        });
    }
})();
