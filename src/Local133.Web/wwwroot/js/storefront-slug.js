// «Catálogo en línea» (5.10): normaliza la liga mientras se escribe, igual que PublicSlug.Normalize en el servidor:
// minúsculas sin acentos (ñ ⇒ n) y cualquier otro carácter como guion, sin guiones repetidos. Al escribir deja un guion
// al final (para seguir escribiendo); al salir del campo lo quita. La validación (regex) corre sobre el valor ya limpio.
(function () {
    'use strict';

    function normalize(value, trimEnd) {
        var slug = value.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
            .replace(/[^a-z0-9]/g, '-').replace(/-{2,}/g, '-').replace(/^-/, '');
        return trimEnd ? slug.replace(/-$/, '') : slug;
    }

    document.querySelectorAll('[data-storefront-slug]').forEach(function (input) {
        input.addEventListener('input', function () {
            var next = normalize(input.value, false);
            if (next !== input.value) {
                input.value = next;
            }
        });
        input.addEventListener('change', function () {
            input.value = normalize(input.value, true);
        });
        if (input.form) {
            input.form.addEventListener('submit', function () {
                input.value = normalize(input.value, true);
            }, true);
        }
    });
})();
