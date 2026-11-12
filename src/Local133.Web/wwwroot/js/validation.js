// Adaptadores propios de jQuery Validation Unobtrusive (se carga después de unobtrusive).
(function ($) {
    // [MustBeTrue]: la casilla debe estar marcada.
    $.validator.addMethod('mustbetrue', function (value, element) {
        return element.checked;
    });
    $.validator.unobtrusive.adapters.addBool('mustbetrue');

    // [Clabe]: 18 dígitos (se ignoran espacios y guiones) con dígito verificador válido. Misma regla que
    // Domain/BankAccounts/Clabe: pesos 3, 7, 1 sobre los primeros 17 y (10 − Σ(d·w mod 10) mod 10) mod 10.
    $.validator.addMethod('clabe', function (value, element) {
        if (this.optional(element)) {
            return true;
        }
        var digits = value.replace(/[\s-]/g, '');
        if (!/^\d{18}$/.test(digits)) {
            return false;
        }
        var weights = [3, 7, 1];
        var sum = 0;
        for (var i = 0; i < 17; i++) {
            sum += (digits.charCodeAt(i) - 48) * weights[i % 3] % 10;
        }
        return (10 - sum % 10) % 10 === digits.charCodeAt(17) - 48;
    });
    $.validator.unobtrusive.adapters.addBool('clabe');
})(jQuery);
