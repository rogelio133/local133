// input[data-amount-input]: monto en pesos sin signo, con hasta 6 enteros y 2 decimales (misma regla que
// ChargeInput.AmountPattern en servidor). Bloquea lo que no cabe al teclear, limpia lo pegado o autocompletado y
// convierte la coma decimal en punto.
(function () {
    'use strict';

    var MAX_INTEGERS = 6;
    var MAX_DECIMALS = 2;
    var ACCEPTABLE = new RegExp('^\\d{0,' + MAX_INTEGERS + '}(\\.\\d{0,' + MAX_DECIMALS + '})?$');

    function normalize(text) {
        return (text || '').replace(/,/g, '.');
    }

    // Deja solo dígitos y el primer punto; recorta enteros y decimales sobrantes.
    function sanitize(text) {
        var clean = normalize(text).replace(/[^\d.]/g, '');
        var dot = clean.indexOf('.');
        if (dot === -1) {
            return clean.slice(0, MAX_INTEGERS);
        }
        var integers = clean.slice(0, dot).slice(0, MAX_INTEGERS);
        var decimals = clean.slice(dot + 1).replace(/\./g, '').slice(0, MAX_DECIMALS);
        return integers + '.' + decimals;
    }

    // Forma final al salir del campo: sin ceros a la izquierda, sin punto colgando, ".5" ⇒ "0.5".
    function tidy(text) {
        if (text === '') {
            return text;
        }
        var parts = text.split('.');
        var integers = parts[0].replace(/^0+(?=\d)/, '') || '0';
        var decimals = parts.length > 1 ? parts[1] : '';
        return decimals === '' ? integers : integers + '.' + decimals;
    }

    function setValue(input, value, caret) {
        input.value = value;
        if (caret !== null && document.activeElement === input) {
            var position = Math.max(0, Math.min(caret, value.length));
            input.setSelectionRange(position, position);
        }
    }

    function attach(input) {
        // Lo que no puede formar un monto válido ni se escribe (incluye "-", "+" y "e").
        input.addEventListener('keydown', function (event) {
            if (!event.ctrlKey && !event.metaKey && (event.key === '-' || event.key === '+' || event.key === 'e' || event.key === 'E')) {
                event.preventDefault();
            }
        });

        input.addEventListener('beforeinput', function (event) {
            if (event.inputType !== 'insertText' || event.data === null || event.isComposing) {
                return; // borrar, pegar, autocompletar: lo resuelve el evento input
            }
            var start = input.selectionStart === null ? input.value.length : input.selectionStart;
            var end = input.selectionEnd === null ? start : input.selectionEnd;
            var candidate = normalize(input.value.slice(0, start) + event.data + input.value.slice(end));
            if (ACCEPTABLE.test(candidate)) {
                return;
            }
            // Una tecla que no cabe (signo, tercer decimal…) no cambia nada; un texto de varios caracteres
            // (autocompletar, algunos teclados) se limpia en lugar de descartarse.
            event.preventDefault();
            var clean = sanitize(candidate);
            if (clean !== input.value) {
                var caret = sanitize(input.value.slice(0, start) + event.data).length;
                setValue(input, clean, caret);
            }
        });

        // Respaldo (pegar, autocompletar, teclados que no permiten cancelar beforeinput).
        input.addEventListener('input', function () {
            var value = input.value;
            if (ACCEPTABLE.test(value)) {
                return;
            }
            var clean = sanitize(value);
            var caret = input.selectionStart === null ? null : input.selectionStart - (value.length - clean.length);
            setValue(input, clean, caret);
        });

        input.addEventListener('blur', function () {
            var value = tidy(sanitize(input.value));
            if (value !== input.value) {
                input.value = value;
            }
        });

        // Al entrar se selecciona todo: el primer dígito reemplaza el 0.01 inicial.
        input.addEventListener('focus', function () {
            window.setTimeout(function () { input.select(); }, 0);
        });

        // Valor inicial o devuelto por el servidor tras un error.
        var initial = tidy(sanitize(input.value));
        if (initial !== input.value) {
            input.value = initial;
        }
    }

    document.querySelectorAll('input[data-amount-input]').forEach(attach);
})();
