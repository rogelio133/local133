// Adaptadores propios de jQuery Validation Unobtrusive (se carga después de unobtrusive).
(function ($) {
    // [MustBeTrue]: la casilla debe estar marcada.
    $.validator.addMethod('mustbetrue', function (value, element) {
        return element.checked;
    });
    $.validator.unobtrusive.adapters.addBool('mustbetrue');
})(jQuery);
