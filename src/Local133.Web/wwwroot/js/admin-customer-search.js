// Búsqueda y filtros de clientes en /admin/clientes sin recargar la página.
// - Busca al escribir (300 ms después de la última tecla), de inmediato con Enter y al cambiar un filtro.
// - Pide a GET /admin/clientes/resultados el fragmento con el conteo, la lista y la paginación, y lo reemplaza.
//   Cada búsqueda nueva cancela la anterior (AbortController).
// - La paginación y «Ver todos» también se cargan así. La URL de la barra queda en /admin/clientes?…, así que
//   recargar, compartir o usar atrás/adelante dan lo mismo que sin JS.
// - Si la respuesta no es la esperada (error, sesión vencida ⇒ redirige al login), navega a la página completa.
(function () {
    'use strict';

    var DEBOUNCE_MS = 300;

    var form = document.querySelector('form[data-customer-search]');
    var results = document.querySelector('[data-customer-results]');
    if (!form || !results || !window.fetch || !window.AbortController || !window.URLSearchParams) {
        return;
    }

    var input = form.querySelector('input[name="q"]');
    var resultsUrl = form.getAttribute('data-results-url');
    var listPath = new URL(form.action, location.href).pathname;
    var timer = null;
    var controller = null;
    var composing = false;
    var shown = location.search;

    // Parámetros del formulario sin los vacíos (la URL queda como la arma el servidor).
    function formQuery() {
        var params = new URLSearchParams();
        new FormData(form).forEach(function (value, key) {
            var text = String(value).trim();
            if (text) {
                params.set(key, text);
            }
        });
        var text = params.toString();
        return text ? '?' + text : '';
    }

    // Llena el formulario con los parámetros de una URL (atrás/adelante, «Ver todos»).
    function fillForm(search) {
        var params = new URLSearchParams(search);
        Array.prototype.forEach.call(form.elements, function (element) {
            if (element.name) {
                element.value = params.get(element.name) || '';
            }
        });
    }

    // historyMode: 'replace' (al escribir o filtrar), 'push' (paginar, «Ver todos») o 'none' (atrás/adelante).
    function load(search, historyMode, scrollTop) {
        clearTimeout(timer);
        if (controller) {
            controller.abort();
        }
        controller = new AbortController();
        var current = controller;
        var listUrl = listPath + search;
        results.setAttribute('aria-busy', 'true');

        fetch(resultsUrl + search, {
            credentials: 'same-origin',
            headers: { Accept: 'text/html' },
            signal: current.signal
        }).then(function (response) {
            if (!response.ok || response.redirected) {
                throw new Error('Respuesta inesperada: ' + response.status);
            }
            return response.text();
        }).then(function (html) {
            if (current !== controller) {
                return;
            }
            results.innerHTML = html;
            shown = search;
            if (scrollTop) {
                window.scrollTo(0, 0);
            }
            if (historyMode === 'push') {
                history.pushState(null, '', listUrl);
            } else if (historyMode === 'replace') {
                history.replaceState(null, '', listUrl);
            }
        }).catch(function (error) {
            if (error && error.name === 'AbortError') {
                return;
            }
            location.assign(listUrl);
        }).then(function () {
            if (current === controller) {
                results.removeAttribute('aria-busy');
                controller = null;
            }
        });
    }

    input.addEventListener('compositionstart', function () {
        composing = true;
    });

    input.addEventListener('compositionend', function () {
        composing = false;
        input.dispatchEvent(new Event('input'));
    });

    input.addEventListener('input', function () {
        if (composing) {
            return;
        }
        clearTimeout(timer);
        timer = setTimeout(function () {
            var search = formQuery();
            if (search !== shown || controller) {
                load(search, 'replace');
            }
        }, DEBOUNCE_MS);
    });

    // Selects y fechas: al cambiar, con los demás filtros y desde la primera página.
    form.addEventListener('change', function (event) {
        if (event.target !== input) {
            load(formQuery(), 'replace');
        }
    });

    form.addEventListener('submit', function (event) {
        event.preventDefault();
        input.blur();
        load(formQuery(), 'replace');
    });

    results.addEventListener('click', function (event) {
        var link = event.target.closest('a[data-pager-previous], a[data-pager-next], a[data-clear-search]');
        if (!link || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
            return;
        }
        event.preventDefault();
        var search = new URL(link.href, location.href).search;
        var clear = link.hasAttribute('data-clear-search');
        if (clear) {
            fillForm('');
        }
        load(search, 'push', !clear);
    });

    window.addEventListener('popstate', function () {
        fillForm(location.search);
        load(location.search, 'none');
    });
})();
