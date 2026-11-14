// Carrito de /app/cobrar/productos.
// - Tocar un producto del catálogo lo agrega (si ya está, suma 1). En la venta: − / + / cantidad tecleada (1–999) y quitar.
// - Pinta los productos, el total, la barra «Ver venta» (móvil y tablet) y los campos ocultos que se envían
//   (Input.Lines[i].ProductId / Quantity e Input.ExpectedTotal). El servidor vuelve a tomar los precios del catálogo.
// - Concepto automático («2 Taco al pastor, Refresco»; si no cabe en 100 caracteres, «… y 3 más») mientras el vendedor
//   no lo edite. Si lo edita, deja de cambiar y aparece «Usar texto automático».
// - El carrito se guarda en sessionStorage (por vendedor) para no perderlo al recargar. Si el formulario volvió con
//   errores, mandan los productos que manda el servidor (con sus precios vigentes). Se limpia al enviar.
(function () {
    'use strict';

    var MAX_QUANTITY = 999;
    var MAX_LINES = 50;
    var MAX_TOTAL_CENTS = 99999999; // $999,999.99
    var CONCEPT_MAX = 100;

    var page = document.querySelector('[data-cart-page]');
    var form = document.querySelector('form[data-cart-form]');
    if (!page || !form) {
        return;
    }

    var storageKey = page.getAttribute('data-storage-key');
    var hasErrors = page.getAttribute('data-has-errors') === 'true';
    var results = document.querySelector('[data-product-results]');
    var linesList = form.querySelector('[data-cart-lines]');
    var hidden = form.querySelector('[data-cart-hidden]');
    var expectedTotal = form.querySelector('[data-cart-expected-total]');
    var emptyText = form.querySelector('[data-cart-empty]');
    var totalText = form.querySelector('[data-cart-total]');
    var limitText = form.querySelector('[data-cart-limit]');
    var submit = form.querySelector('[data-charge-submit]');
    var concept = form.querySelector('[data-cart-concept]');
    var conceptAuto = form.querySelector('[data-cart-concept-auto]');
    var bar = document.querySelector('[data-cart-bar]');
    var barCount = bar && bar.querySelector('[data-cart-bar-count]');
    var barTotal = bar && bar.querySelector('[data-cart-bar-total]');
    var panel = document.getElementById('cart-panel');
    var money = new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' });

    var lines = [];
    var conceptEdited = false;
    var limitMessage = '';

    // --- Estado ----------------------------------------------------------------------------------------------------

    function cents(price) {
        return Math.round(parseFloat(price) * 100);
    }

    function totalCents() {
        return lines.reduce(function (sum, line) {
            return sum + cents(line.price) * line.quantity;
        }, 0);
    }

    function itemCount() {
        return lines.reduce(function (sum, line) {
            return sum + line.quantity;
        }, 0);
    }

    function formatCents(value) {
        return money.format(value / 100);
    }

    function find(productId) {
        for (var i = 0; i < lines.length; i++) {
            if (lines[i].productId === productId) {
                return lines[i];
            }
        }
        return null;
    }

    function clampQuantity(value) {
        var n = parseInt(value, 10);
        if (isNaN(n) || n < 1) {
            return 1;
        }
        return Math.min(n, MAX_QUANTITY);
    }

    function normalize(raw) {
        if (!raw || typeof raw !== 'object') {
            return null;
        }
        var productId = parseInt(raw.productId, 10);
        if (!(productId > 0) || !raw.name || isNaN(parseFloat(raw.price))) {
            return null;
        }
        return {
            productId: productId,
            name: String(raw.name),
            price: parseFloat(raw.price).toFixed(2),
            priceText: raw.priceText ? String(raw.priceText) : formatCents(cents(raw.price)),
            thumb: raw.thumb || null,
            quantity: clampQuantity(raw.quantity)
        };
    }

    function load(list) {
        lines = [];
        (Array.isArray(list) ? list : []).forEach(function (raw) {
            var line = normalize(raw);
            if (line && !find(line.productId) && lines.length < MAX_LINES) {
                lines.push(line);
            }
        });
    }

    function save() {
        try {
            if (lines.length) {
                sessionStorage.setItem(storageKey, JSON.stringify(lines));
            } else {
                sessionStorage.removeItem(storageKey);
            }
        } catch (e) {
            // Sin sessionStorage (modo privado, bloqueado): el carrito vive solo en la página.
        }
    }

    function restore() {
        try {
            var text = sessionStorage.getItem(storageKey);
            return text ? JSON.parse(text) : [];
        } catch (e) {
            return [];
        }
    }

    function clearStorage() {
        try {
            sessionStorage.removeItem(storageKey);
        } catch (e) {
            // Nada que limpiar.
        }
    }

    // --- Concepto --------------------------------------------------------------------------------------------------

    function autoConcept() {
        var parts = lines.map(function (line) {
            return line.quantity > 1 ? line.quantity + ' ' + line.name : line.name;
        });
        var full = parts.join(', ');
        if (full.length <= CONCEPT_MAX) {
            return full;
        }
        for (var k = parts.length - 1; k >= 1; k--) {
            var text = parts.slice(0, k).join(', ') + ' y ' + (parts.length - k) + ' más';
            if (text.length <= CONCEPT_MAX) {
                return text;
            }
        }
        return parts[0].slice(0, CONCEPT_MAX);
    }

    function revalidate(input) {
        if (input.classList.contains('input-validation-error') && window.jQuery && window.jQuery.fn.valid) {
            window.jQuery(input).valid();
        }
    }

    function updateConcept() {
        if (!concept) {
            return;
        }
        if (!conceptEdited) {
            concept.value = autoConcept();
            revalidate(concept);
        }
        if (conceptAuto) {
            conceptAuto.classList.toggle('d-none', !conceptEdited || !lines.length);
        }
    }

    // --- Pintar ----------------------------------------------------------------------------------------------------

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) {
            node.className = className;
        }
        if (text !== undefined) {
            node.textContent = text;
        }
        return node;
    }

    function iconButton(className, icon, label, attribute) {
        var button = el('button', className);
        button.type = 'button';
        button.setAttribute('aria-label', label);
        button.setAttribute(attribute, '');
        var i = el('i', 'bi ' + icon);
        i.setAttribute('aria-hidden', 'true');
        button.appendChild(i);
        return button;
    }

    function renderLine(line) {
        var item = el('li', 'list-group-item px-0');
        item.setAttribute('data-cart-line', String(line.productId));

        var top = el('div', 'd-flex align-items-start gap-2');
        var info = el('div', 'flex-grow-1 product-info');
        var name = el('div', 'fw-semibold text-truncate', line.name);
        name.setAttribute('data-cart-line-name', '');
        info.appendChild(name);
        info.appendChild(el('div', 'small text-body-secondary', line.priceText + ' c/u'));
        top.appendChild(info);
        top.appendChild(iconButton('btn btn-link link-danger p-1 lh-1', 'bi-trash', 'Quitar ' + line.name, 'data-cart-remove'));
        item.appendChild(top);

        var bottom = el('div', 'd-flex align-items-center justify-content-between mt-1');
        var group = el('div', 'input-group cart-qty');
        var dec = iconButton('btn btn-outline-secondary px-0 cart-qty-btn', 'bi-dash-lg', 'Uno menos de ' + line.name, 'data-cart-dec');
        dec.disabled = line.quantity <= 1;
        var qty = el('input', 'form-control text-center px-1 cart-qty-input');
        qty.type = 'text';
        qty.inputMode = 'numeric';
        qty.maxLength = 3;
        qty.autocomplete = 'off';
        qty.value = String(line.quantity);
        qty.setAttribute('aria-label', 'Cantidad de ' + line.name);
        qty.setAttribute('data-cart-qty', '');
        var inc = iconButton('btn btn-outline-secondary px-0 cart-qty-btn', 'bi-plus-lg', 'Uno más de ' + line.name, 'data-cart-inc');
        inc.disabled = line.quantity >= MAX_QUANTITY;
        group.appendChild(dec);
        group.appendChild(qty);
        group.appendChild(inc);
        bottom.appendChild(group);
        var amount = el('span', 'fw-semibold', formatCents(cents(line.price) * line.quantity));
        amount.setAttribute('data-cart-line-amount', '');
        bottom.appendChild(amount);
        item.appendChild(bottom);
        return item;
    }

    // Solo totales y campos (sin repintar las filas): así no se pierde el foco al teclear una cantidad.
    function renderTotals() {
        var total = totalCents();
        var count = itemCount();
        var overLimit = total > MAX_TOTAL_CENTS;

        totalText.textContent = formatCents(total);
        expectedTotal.value = lines.length ? (total / 100).toFixed(2) : '';

        hidden.textContent = '';
        lines.forEach(function (line, index) {
            [['ProductId', line.productId], ['Quantity', line.quantity]].forEach(function (pair) {
                var input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'Input.Lines[' + index + '].' + pair[0];
                input.value = String(pair[1]);
                hidden.appendChild(input);
            });
        });

        var message = overLimit ? 'El total máximo de una venta es $999,999.99. Quita productos o cantidades.' : limitMessage;
        limitText.textContent = message;
        limitText.classList.toggle('d-none', !message);

        emptyText.classList.toggle('d-none', lines.length > 0);
        submit.disabled = !lines.length || overLimit;

        if (bar) {
            bar.classList.toggle('d-none', !lines.length);
            document.body.classList.toggle('has-cart-bar', lines.length > 0);
            barCount.textContent = count === 1 ? '1 producto' : count.toLocaleString('es-MX') + ' productos';
            barTotal.textContent = formatCents(total);
        }

        lines.forEach(function (line) {
            var row = linesList.querySelector('[data-cart-line="' + line.productId + '"]');
            if (row) {
                row.querySelector('[data-cart-line-amount]').textContent = formatCents(cents(line.price) * line.quantity);
                row.querySelector('[data-cart-dec]').disabled = line.quantity <= 1;
                row.querySelector('[data-cart-inc]').disabled = line.quantity >= MAX_QUANTITY;
            }
        });

        updateConcept();
        markCatalog();
        save();
    }

    function render() {
        linesList.textContent = '';
        lines.forEach(function (line) {
            linesList.appendChild(renderLine(line));
        });
        renderTotals();
    }

    // Cantidad en el catálogo: el «+» de cada producto que ya está en la venta muestra cuántos lleva.
    function markCatalog() {
        if (!results) {
            return;
        }
        results.querySelectorAll('[data-cart-add]').forEach(function (button) {
            var line = find(parseInt(button.getAttribute('data-product-id'), 10));
            var icon = button.querySelector('.cart-add-icon');
            if (!icon) {
                return;
            }
            icon.textContent = '';
            if (line) {
                icon.textContent = String(line.quantity);
                icon.classList.add('cart-add-icon-count');
            } else {
                var i = el('i', 'bi bi-plus-lg');
                icon.appendChild(i);
                icon.classList.remove('cart-add-icon-count');
            }
        });
    }

    // --- Acciones --------------------------------------------------------------------------------------------------

    function add(button) {
        var productId = parseInt(button.getAttribute('data-product-id'), 10);
        var line = find(productId);
        limitMessage = '';
        if (line) {
            line.quantity = Math.min(line.quantity + 1, MAX_QUANTITY);
            var input = linesList.querySelector('[data-cart-line="' + productId + '"] [data-cart-qty]');
            if (input) {
                input.value = String(line.quantity);
            }
            renderTotals();
            return;
        }
        if (lines.length >= MAX_LINES) {
            limitMessage = 'Una venta admite hasta ' + MAX_LINES + ' productos distintos.';
            renderTotals();
            return;
        }
        var added = normalize({
            productId: productId,
            name: button.getAttribute('data-name'),
            price: button.getAttribute('data-price'),
            priceText: button.getAttribute('data-price-text'),
            thumb: button.getAttribute('data-thumb'),
            quantity: 1
        });
        if (added) {
            lines.push(added);
            render();
        }
    }

    function lineOf(target) {
        var row = target.closest('[data-cart-line]');
        return row ? find(parseInt(row.getAttribute('data-cart-line'), 10)) : null;
    }

    if (results) {
        results.addEventListener('click', function (event) {
            var button = event.target.closest('[data-cart-add]');
            if (button) {
                add(button);
            }
        });
        // product-search.js reemplaza los resultados al buscar o paginar.
        if (window.MutationObserver) {
            new MutationObserver(markCatalog).observe(results, { childList: true });
        }
    }

    linesList.addEventListener('click', function (event) {
        var target = event.target.closest('[data-cart-remove], [data-cart-dec], [data-cart-inc]');
        var line = target && lineOf(target);
        if (!line) {
            return;
        }
        limitMessage = '';
        if (target.hasAttribute('data-cart-remove')) {
            lines = lines.filter(function (l) {
                return l !== line;
            });
            render();
            var next = linesList.querySelector('[data-cart-remove]');
            (next || (results && results.querySelector('input, button')) || submit).focus();
            return;
        }
        line.quantity = clampQuantity(line.quantity + (target.hasAttribute('data-cart-inc') ? 1 : -1));
        target.closest('[data-cart-line]').querySelector('[data-cart-qty]').value = String(line.quantity);
        renderTotals();
    });

    linesList.addEventListener('input', function (event) {
        var input = event.target.closest('[data-cart-qty]');
        var line = input && lineOf(input);
        if (!line) {
            return;
        }
        var digits = input.value.replace(/\D/g, '').slice(0, 3);
        if (digits !== input.value) {
            input.value = digits;
        }
        // Vacío o 0 mientras teclea: se ajusta a 1 al salir del campo.
        if (parseInt(digits, 10) >= 1) {
            line.quantity = clampQuantity(digits);
            renderTotals();
        }
    });

    linesList.addEventListener('focusout', function (event) {
        var input = event.target.closest('[data-cart-qty]');
        var line = input && lineOf(input);
        if (line) {
            line.quantity = clampQuantity(input.value);
            input.value = String(line.quantity);
            renderTotals();
        }
    });

    // Enter en una cantidad no envía el formulario (en el celular, cierra el teclado).
    linesList.addEventListener('keydown', function (event) {
        if (event.key === 'Enter' && event.target.closest('[data-cart-qty]')) {
            event.preventDefault();
            event.target.blur();
        }
    });

    if (concept) {
        concept.addEventListener('input', function () {
            conceptEdited = concept.value.trim() !== autoConcept();
            updateConcept();
        });
    }

    if (conceptAuto) {
        conceptAuto.addEventListener('click', function () {
            conceptEdited = false;
            updateConcept();
            concept.focus();
        });
    }

    // Se limpia solo si el envío sigue (la validación en cliente lo cancela antes: listeners de jQuery primero).
    form.addEventListener('submit', function (event) {
        if (!event.defaultPrevented) {
            clearStorage();
        }
    });

    // --- Inicio ----------------------------------------------------------------------------------------------------

    function readInitial() {
        var script = document.querySelector('script[data-cart-initial]');
        try {
            return script ? JSON.parse(script.textContent || '[]') : [];
        } catch (e) {
            return [];
        }
    }

    function start(fromServer) {
        var initial = fromServer ? readInitial() : [];
        load(initial.length ? initial : restore());
        // Al volver con errores, el concepto que escribió el vendedor se respeta si no es el automático.
        conceptEdited = !!(concept && concept.value.trim() && concept.value.trim() !== autoConcept());
        render();
    }

    start(true);

    if (hasErrors && panel && window.bootstrap && window.matchMedia('(max-width: 991.98px)').matches) {
        window.bootstrap.Offcanvas.getOrCreateInstance(panel).show();
    }

    // Al volver con «atrás» (bfcache) después de cobrar, el carrito ya se limpió: se rearma desde sessionStorage.
    window.addEventListener('pageshow', function (event) {
        if (event.persisted) {
            if (concept) {
                concept.value = '';
            }
            start(false);
        }
    });
})();
