// Portada: menú del celular y carruseles (pasos y precios). Sin librerías. Sin JS la página se ve completa: el panel
// del menú queda abierto y las pistas se deslizan con el dedo (@media (scripting: none) en el SCSS).
(function () {
    'use strict';

    var reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    var desktopNav = window.matchMedia('(min-width: 992px)'); // lg: el menú va en la barra

    // --- Menú del celular -------------------------------------------------
    // El panel abre con .is-open (altura 0fr → 1fr y opacidad en CSS). Cerrado queda en visibility: hidden, así que
    // sus enlaces no se pueden enfocar. Se cierra al elegir un enlace, con Esc y al pasar a escritorio.
    (function initMenu() {
        var toggle = document.querySelector('[data-menu-toggle]');
        var panel = document.querySelector('[data-menu-panel]');
        if (!toggle || !panel) {
            return;
        }

        function setOpen(open) {
            panel.classList.toggle('is-open', open);
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
            toggle.setAttribute('aria-label', open ? toggle.dataset.labelClose : toggle.dataset.labelOpen);
        }

        function isOpen() {
            return panel.classList.contains('is-open');
        }

        toggle.addEventListener('click', function () {
            setOpen(!isOpen());
        });

        // Un ancla de la misma página: el panel se cierra al instante (sin transición) y luego se salta, porque si se
        // cerrara animado mientras el navegador desplaza la página, el destino quedaría corrido la altura del panel.
        panel.addEventListener('click', function (event) {
            var link = event.target.closest('a');
            if (!link) {
                return;
            }
            var hash = link.getAttribute('href');
            var target = hash && hash.charAt(0) === '#' ? document.getElementById(hash.slice(1)) : null;
            if (!target) {
                setOpen(false);
                return;
            }
            event.preventDefault();
            panel.classList.add('is-instant');
            setOpen(false);
            void panel.offsetHeight; // aplica el cierre antes de calcular el salto
            panel.classList.remove('is-instant');
            if (location.hash === hash) {
                target.scrollIntoView();
            } else {
                location.hash = hash;
            }
        });

        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape' && isOpen()) {
                setOpen(false);
                toggle.focus();
            }
        });

        desktopNav.addEventListener('change', function (event) {
            if (event.matches) {
                setOpen(false);
            }
        });
    })();

    // --- Carruseles -----------------------------------------------------------
    // [data-carousel] con su pista [data-carousel-track] y los puntos [data-carousel-dot="i"] que pinta el servidor.
    // data-carousel-autoplay="ms": avanza solo (pasos), únicamente mientras la pista se desplaza (celular), se ve en
    // pantalla y la pestaña está visible; nunca con «reducir movimiento». La barra del punto activo se llena en ese
    // tiempo (animación CSS que se reinicia en cada avance, al tocar un punto y al deslizar).
    document.querySelectorAll('[data-carousel]').forEach(function (root) {
        var track = root.querySelector('[data-carousel-track]');
        var dots = Array.prototype.slice.call(root.querySelectorAll('[data-carousel-dot]'));
        if (!track || track.children.length < 2) {
            return;
        }

        var items = track.children;
        var interval = parseInt(root.getAttribute('data-carousel-autoplay') || '0', 10);
        var active = 0;
        var timer = null;
        var inView = false;
        var scrollFrame = 0;

        if (interval > 0) {
            root.style.setProperty('--carousel-interval', interval + 'ms');
        }

        function scrolls() {
            return track.scrollWidth > track.clientWidth + 4;
        }

        function offsetOf(index) {
            return items[index].offsetLeft - items[0].offsetLeft;
        }

        function setActive(index) {
            active = index;
            dots.forEach(function (dot, i) {
                var on = i === index;
                dot.classList.toggle('is-active', on);
                dot.setAttribute('aria-current', on ? 'true' : 'false');
                if (!on) {
                    dot.classList.remove('is-running');
                }
            });
        }

        // Mientras el carrusel se desplaza solo (o por un punto), los índices intermedios no cuentan como deslizar.
        var target = null;
        var targetTimer = null;

        function go(index) {
            setActive(index);
            target = index;
            clearTimeout(targetTimer);
            targetTimer = setTimeout(function () { target = null; }, 1000);
            track.scrollTo({ left: offsetOf(index), behavior: reducedMotion.matches ? 'auto' : 'smooth' });
        }

        function canAutoplay() {
            return interval > 0 && !reducedMotion.matches && !document.hidden && inView && scrolls();
        }

        function stop() {
            clearTimeout(timer);
            timer = null;
            dots.forEach(function (dot) { dot.classList.remove('is-running'); });
        }

        // Empieza la cuenta (y la barra) desde cero en el punto activo.
        function restart() {
            stop();
            if (!canAutoplay()) {
                return;
            }
            var dot = dots[active];
            if (dot) {
                void dot.offsetWidth; // reinicia la animación de la barra
                dot.classList.add('is-running');
            }
            timer = setTimeout(function () {
                go((active + 1) % items.length);
                restart();
            }, interval);
        }

        // Deslizar con el dedo actualiza el punto; si cambió por el usuario, la cuenta vuelve a empezar.
        track.addEventListener('scroll', function () {
            cancelAnimationFrame(scrollFrame);
            scrollFrame = requestAnimationFrame(function () {
                var step = offsetOf(1);
                if (step <= 0) {
                    return;
                }
                var index = Math.max(0, Math.min(items.length - 1, Math.round(track.scrollLeft / step)));
                if (target !== null) {
                    if (index === target) {
                        target = null;
                        clearTimeout(targetTimer);
                    }
                    return;
                }
                if (index !== active) {
                    setActive(index);
                    restart();
                }
            });
        }, { passive: true });

        track.addEventListener('pointerdown', restart, { passive: true });

        dots.forEach(function (dot, i) {
            dot.addEventListener('click', function () {
                go(i);
                restart();
            });
        });

        if (interval > 0) {
            document.addEventListener('visibilitychange', restart);
            reducedMotion.addEventListener('change', restart);
            window.addEventListener('resize', function () {
                if (!scrolls()) {
                    stop();
                } else if (!timer) {
                    restart();
                }
            });

            if ('IntersectionObserver' in window) {
                new IntersectionObserver(function (entries) {
                    inView = entries[entries.length - 1].isIntersecting;
                    restart();
                }, { threshold: 0.5 }).observe(track);
            } else {
                inView = true;
                restart();
            }
        }
    });
})();
