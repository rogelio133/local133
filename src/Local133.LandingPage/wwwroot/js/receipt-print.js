// Recibo público ([data-receipt-print]): abre el diálogo de impresión (imprimir o guardar como PDF). Sin JS el botón
// no hace nada; el recibo se sigue viendo completo.
(function () {
    'use strict';

    document.querySelectorAll('[data-receipt-print]').forEach(function (button) {
        button.addEventListener('click', function () { window.print(); });
    });
})();
