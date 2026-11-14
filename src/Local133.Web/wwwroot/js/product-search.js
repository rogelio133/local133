// Búsqueda de productos en /app/productos sin recargar la página.
// - Busca al escribir (300 ms después de la última tecla) y de inmediato con Enter o el botón.
// - Pide a GET /app/productos/resultados el fragmento con el conteo, la lista y la paginación, y lo reemplaza.
//   Cada búsqueda nueva cancela la anterior (AbortController).
// - La paginación y «Ver todos» también se cargan así. La URL de la barra queda en /app/productos?q=…&pagina=…, así que
//   recargar, compartir o usar atrás/adelante dan lo mismo que sin JS.
// - Si la respuesta no es la esperada (error, sesión vencida ⇒ redirige al login), navega a la página completa.
(function () {
    'use strict';

    var DEBOUNCE_MS = 300;
    var PAGE_KEY = 'pagina';

    var form = document.querySelector('form[data-product-search]');
    var results = document.querySelector('[data-product-results]');
    if (!form || !results || !window.fetch || !window.AbortController) {
        return;
    }

    var input = form.querySelector('input[name="q"]');
    var resultsUrl = form.getAttribute('data-results-url');
    var listPath = new URL(form.action, location.href).pathname;
    var timer = null;
    var controller = null;
    var composing = false;
    var shown = stateFromUrl(location.href);

    function stateFromUrl(href) {
        var params = new URL(href, location.href).searchParams;
        var page = parseInt(params.get(PAGE_KEY), 10);
        return { q: (params.get('q') || '').trim(), page: page > 1 ? page : 1 };
    }

    function queryString(state) {
        var params = new URLSearchParams();
        if (state.q) {
            params.set('q', state.q);
        }
        if (state.page > 1) {
            params.set(PAGE_KEY, String(state.page));
        }
        var text = params.toString();
        return text ? '?' + text : '';
    }

    function sameState(a, b) {
        return a.q === b.q && a.page === b.page;
    }

    // historyMode: 'replace' (al escribir), 'push' (paginar, «Ver todos») o 'none' (atrás/adelante).
    // scrollTop: al cambiar de página se sube al inicio, como sin JS (después de pintar: antes, el navegador conserva
    // la posición al reemplazar la lista).
    function load(state, historyMode, scrollTop) {
        clearTimeout(timer);
        if (controller) {
            controller.abort();
        }
        controller = new AbortController();
        var current = controller;
        var listUrl = listPath + queryString(state);
        results.setAttribute('aria-busy', 'true');

        fetch(resultsUrl + queryString(state), {
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
            shown = state;
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
            // Sesión vencida, error del servidor o sin red: la página completa sabe qué mostrar.
            location.assign(listUrl);
        }).then(function () {
            if (current === controller) {
                results.removeAttribute('aria-busy');
                controller = null;
            }
        });
    }

    function typedState() {
        return { q: input.value.trim(), page: 1 };
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
            var state = typedState();
            if (!sameState(state, shown) || controller) {
                load(state, 'replace');
            }
        }, DEBOUNCE_MS);
    });

    // Enter o el botón: busca ya, sin recargar. En el celular cierra el teclado para que se vean los resultados.
    form.addEventListener('submit', function (event) {
        event.preventDefault();
        input.blur();
        load(typedState(), 'replace');
    });

    // Paginación y «Ver todos» dentro de los resultados.
    results.addEventListener('click', function (event) {
        var link = event.target.closest('a[data-pager-previous], a[data-pager-next], a[data-clear-search]');
        if (!link || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
            return;
        }
        event.preventDefault();
        var clear = link.hasAttribute('data-clear-search');
        if (clear) {
            input.value = '';
        }
        load(stateFromUrl(link.href), 'push', !clear);
    });

    window.addEventListener('popstate', function () {
        var state = stateFromUrl(location.href);
        input.value = state.q;
        load(state, 'none');
    });
})();
