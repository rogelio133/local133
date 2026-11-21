// Confirmación del pago de Pro (/app/plan/pago/{id}), al volver de la página de Pey. Consulta el estado del cargo
// cada 3 s (el servidor le pregunta a Pey con freno) hasta que queda pagado, incompleto o fallido. El navegador no
// abona nada: solo refleja lo que ya registró el servidor (webhook o consulta a Pey).
(function () {
    'use strict';

    var root = document.querySelector('[data-subscription-payment]');
    if (!root) {
        return;
    }

    var POLL_MS = 3000;
    var SLOW_AFTER_MS = 10 * 60 * 1000;
    var FINAL = ['Paid', 'Partial', 'Failed'];

    var statusUrl = root.getAttribute('data-status-url');
    var status = root.getAttribute('data-status');
    var startedAt = Date.now();
    var timer = null;
    var checking = false;
    var stopped = false;

    function toggle(selector, visible) {
        root.querySelectorAll(selector).forEach(function (el) {
            el.classList.toggle('d-none', !visible);
        });
    }

    function render(next, proUntilText) {
        status = next;
        toggle('[data-waiting-only]', next === 'Pending' || next === 'Processing');
        toggle('[data-paid-only]', next === 'Paid');
        toggle('[data-partial-only]', next === 'Partial');
        toggle('[data-failed-only]', next === 'Failed');

        var waitingText = root.querySelector('[data-waiting-text]');
        if (waitingText) {
            waitingText.textContent = next === 'Processing' ? 'Procesando tu pago…' : 'Confirmando tu pago…';
        }

        var until = root.querySelector('[data-pro-until]');
        if (until && proUntilText) {
            until.textContent = proUntilText;
        }

        if (next === 'Paid') {
            toggle('[data-pay-again]', false);
            if (navigator.vibrate) {
                try { navigator.vibrate(200); } catch (e) { /* sin vibración */ }
            }
        }
    }

    function stop() {
        stopped = true;
        if (timer) {
            clearInterval(timer);
            timer = null;
        }
    }

    function check() {
        if (stopped || checking) {
            return;
        }

        if (Date.now() - startedAt > SLOW_AFTER_MS) {
            toggle('[data-slow-only]', true);
            stop();
            return;
        }

        checking = true;
        fetch(statusUrl, { headers: { Accept: 'application/json' }, credentials: 'same-origin', cache: 'no-store' })
            .then(function (response) {
                if (response.status === 404 || response.status === 401 || response.status === 403) {
                    stop();
                    return null;
                }

                return response.ok ? response.json() : null;
            })
            .then(function (data) {
                if (!data || !data.status) {
                    return;
                }

                render(data.status, data.proUntilText);
                if (FINAL.indexOf(data.status) >= 0) {
                    stop();
                }
            })
            .catch(function () { /* la siguiente vuelta reintenta */ })
            .then(function () { checking = false; });
    }

    if (FINAL.indexOf(status) >= 0) {
        return;
    }

    timer = setInterval(check, POLL_MS);
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') {
            check();
        }
    });
})();
