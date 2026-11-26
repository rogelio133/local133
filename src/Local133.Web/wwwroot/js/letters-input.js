// input[data-letters-only]: nombre de la persona, solo letras (con acentos, ñ, ü) y espacios. Mismos rangos que
// PersonName.Pattern en el servidor. Descarta lo demás al teclear, al pegar y al autocompletar. Respeta la composición
// del teclado (tecla muerta para los acentos, IME, predicción del celular): limpia al terminar de componer.
(function () {
    'use strict';

    var NOT_ALLOWED = /[^A-Za-zÀ-ÖØ-öø-ÿ ]/g;
    var ALLOWED_TEXT = /^[A-Za-zÀ-ÖØ-öø-ÿ ]*$/;

    function sanitize(text) {
        // Tabuladores y saltos de lo pegado cuentan como espacio; lo demás se quita.
        return (text || '').replace(/\s/g, ' ').replace(NOT_ALLOWED, '');
    }

    function clean(input) {
        var value = input.value;
        if (ALLOWED_TEXT.test(value)) {
            return;
        }
        var caret = input.selectionStart;
        var before = caret === null ? value : value.slice(0, caret);
        var result = sanitize(value);
        input.value = result;
        if (caret !== null && document.activeElement === input) {
            var position = sanitize(before).length;
            input.setSelectionRange(position, position);
        }
    }

    function attach(input) {
        // Una tecla no permitida (número, signo) ni se escribe. Pegar, autocompletar y los textos de varios
        // caracteres los limpia el evento input.
        input.addEventListener('beforeinput', function (event) {
            if (event.inputType === 'insertText' && !event.isComposing && event.data !== null
                && event.data.length === 1 && !ALLOWED_TEXT.test(event.data)) {
                event.preventDefault();
            }
        });

        input.addEventListener('input', function (event) {
            if (event.isComposing) {
                return; // se limpia en compositionend
            }
            clean(input);
        });

        input.addEventListener('compositionend', function () {
            clean(input);
        });

        // Valor que devolvió el servidor tras un error: se deja tal cual para que se vea qué falló.
    }

    document.querySelectorAll('input[data-letters-only]').forEach(attach);
})();
