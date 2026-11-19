// Portada: la última palabra del título ([data-rotating-words], JSON con las palabras de Home:RotatingWords) se
// borra y se escribe letra por letra, como si se tipeara a mano, en orden. El servidor ya pintó la primera.
// Sin animación con «reducir movimiento».
// - Ciclo: pausa con la palabra completa (cursor parpadeando) → borrado → pausa corta → tipeo de la siguiente.
// - El cursor es un ::after de .rotating-word.is-typing (site.scss): también sostiene el alto de la línea vacía.
// - Lector de pantalla: la palabra que cambia va con aria-hidden y junto a ella queda un texto estable
//   (visually-hidden), así no se anuncia cada letra.
(function () {
    'use strict';

    var PauseMs = 1800;          // palabra completa a la vista
    var DeleteMs = 45;           // por letra al borrar
    var GapMs = 350;             // línea vacía antes de escribir
    var TypeMinMs = 80;          // por letra al escribir: ritmo irregular entre mínimo y máximo
    var TypeMaxMs = 160;
    var HiddenRetryMs = 500;     // pestaña oculta: se reintenta sin avanzar

    var reducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)');

    document.querySelectorAll('[data-rotating-words]').forEach(function (element) {
        var words;
        try {
            words = JSON.parse(element.getAttribute('data-rotating-words') || '[]');
        } catch (e) {
            return;
        }
        if (!Array.isArray(words) || words.length < 2 || (reducedMotion && reducedMotion.matches)) {
            return;
        }

        var stable = document.createElement('span');
        stable.className = 'visually-hidden';
        stable.textContent = element.textContent;
        element.insertAdjacentElement('afterend', stable);
        element.setAttribute('aria-hidden', 'true');

        var index = Math.max(0, words.indexOf(element.textContent));
        var word = words[index];
        var length = word.length;

        element.classList.add('is-typing');

        function show() {
            element.textContent = word.slice(0, length);
        }

        function stop() {
            word = words[index];
            length = word.length;
            show();
            element.classList.remove('is-typing', 'is-paused');
        }

        function typingDelay() {
            return TypeMinMs + Math.random() * (TypeMaxMs - TypeMinMs);
        }

        // Cada paso decide el siguiente: pause → delete → gap → type → pause.
        function step(state) {
            if (reducedMotion && reducedMotion.matches) {
                stop();
                return;
            }
            if (document.hidden) {
                setTimeout(function () { step(state); }, HiddenRetryMs);
                return;
            }

            switch (state) {
                case 'pause':
                    element.classList.add('is-paused');
                    setTimeout(function () {
                        element.classList.remove('is-paused');
                        step('delete');
                    }, PauseMs);
                    break;
                case 'delete':
                    if (length > 0) {
                        length--;
                        show();
                        setTimeout(function () { step('delete'); }, DeleteMs);
                    } else {
                        index = (index + 1) % words.length;
                        word = words[index];
                        setTimeout(function () { step('type'); }, GapMs);
                    }
                    break;
                case 'type':
                    if (length < word.length) {
                        length++;
                        show();
                        setTimeout(function () { step('type'); }, typingDelay());
                    } else {
                        step('pause');
                    }
                    break;
            }
        }

        step('pause');
    });
})();
