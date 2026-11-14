// Formulario de /app/cobrar:
// 1. Selector "QR | Celular": con Celular muestra (y habilita) el campo del celular y el botón dice "Enviar cobro";
//    con QR lo oculta y lo deshabilita (no se envía y jQuery Validation lo ignora) y el botón dice "Generar QR".
// 2. input[data-phone-input]: deja solo los 10 dígitos del celular. Al pegar "+52 (55) 1234-5678" quita la lada,
//    espacios, guiones, paréntesis y puntos (misma regla que Domain/Sales/PhoneNumber.Normalize).
(function () {
    'use strict';

    var PHONE_DIGITS = 10;

    var form = document.querySelector('form[data-charge-form]');
    if (!form) {
        return;
    }

    var field = form.querySelector('[data-phone-field]');
    var phone = form.querySelector('input[data-phone-input]');
    var icon = form.querySelector('[data-charge-submit-icon]');
    var label = form.querySelector('[data-charge-submit-label]');
    var radios = form.querySelectorAll('input[name="Input.Method"]');

    function clearPhoneErrors() {
        if (!phone) {
            return;
        }
        phone.classList.remove('input-validation-error');
        phone.removeAttribute('aria-invalid');
        var message = form.querySelector('[data-valmsg-for="' + phone.name + '"]');
        if (message) {
            message.textContent = '';
            message.classList.remove('field-validation-error');
            message.classList.add('field-validation-valid');
        }
    }

    function selectedMethod() {
        for (var i = 0; i < radios.length; i++) {
            if (radios[i].checked) {
                return radios[i].value;
            }
        }
        return 'DynamicQr';
    }

    function apply() {
        var isPush = selectedMethod() === 'PushNotification';
        if (field) {
            field.classList.toggle('d-none', !isPush);
        }
        if (phone) {
            phone.disabled = !isPush;
            if (!isPush) {
                clearPhoneErrors();
            }
        }
        if (icon) {
            icon.classList.toggle('bi-qr-code', !isPush);
            icon.classList.toggle('bi-send', isPush);
        }
        if (label) {
            label.textContent = isPush ? 'Enviar cobro' : 'Generar QR';
        }
    }

    radios.forEach(function (radio) {
        radio.addEventListener('change', apply);
    });

    function sanitizePhone(text) {
        var clean = (text || '').replace(/[\s\-().]/g, '');
        if (clean.indexOf('+52') === 0) {
            clean = clean.slice(3);
        }
        return clean.replace(/\D/g, '').slice(0, PHONE_DIGITS);
    }

    if (phone) {
        phone.addEventListener('input', function () {
            var clean = sanitizePhone(phone.value);
            if (clean !== phone.value) {
                phone.value = clean;
            }
            // jQuery Validation revalida con keyup: al pegar con el mouse el error anterior se quedaría.
            if (phone.classList.contains('input-validation-error') && window.jQuery && window.jQuery.fn.valid) {
                window.jQuery(phone).valid();
            }
        });
        var initial = sanitizePhone(phone.value);
        if (initial !== phone.value) {
            phone.value = initial;
        }
    }

    apply();
})();
