// Índice de secciones (nav[data-section-index], p. ej. Términos y Aviso de privacidad, 5.11).
// En Chrome y Edge de escritorio el clic deja el foco en la liga del índice, y al terminar el desplazamiento suave el
// navegador podía regresar al elemento con foco (el índice). Antes de que el navegador salte a la sección, el foco pasa
// a su título (sin desplazar): el salto lo hace el navegador como siempre, y el teclado sigue desde la sección.
(function () {
    document.querySelectorAll('nav[data-section-index]').forEach(function (nav) {
        nav.addEventListener('click', function (event) {
            var link = event.target instanceof Element ? event.target.closest('a[href^="#"]') : null;
            if (!link) {
                return;
            }

            var target = document.getElementById(decodeURIComponent(link.getAttribute('href').slice(1)));
            if (!target) {
                return;
            }

            if (!target.hasAttribute('tabindex')) {
                target.setAttribute('tabindex', '-1');
            }
            target.focus({ preventScroll: true });
        });
    });
})();
