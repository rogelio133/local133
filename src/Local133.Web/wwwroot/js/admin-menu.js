// Menú del admin (ajustes 2026-11-26, punto 13). En ≥lg el botón [data-admin-menu-toggle] oculta el menú a un riel de
// íconos (clase admin-menu-collapsed en <html>) y lo vuelve a mostrar. El estado se guarda en localStorage
// (l133-admin-menu) y un script en el <head> de _AdminLayout lo aplica antes de pintar, sin parpadeo.
// En el riel, cada liga muestra su nombre con un tooltip de Bootstrap; expandido o en el offcanvas (<lg), no.
(function () {
    'use strict';

    var STORAGE_KEY = 'l133-admin-menu';
    var COLLAPSED_CLASS = 'admin-menu-collapsed';
    var root = document.documentElement;
    var toggle = document.querySelector('[data-admin-menu-toggle]');
    var desktop = window.matchMedia('(min-width: 992px)');
    var tooltips = [];

    if (window.bootstrap && window.bootstrap.Tooltip) {
        document.querySelectorAll('[data-admin-nav-link]').forEach(function (link) {
            var label = link.querySelector('.admin-nav-label');
            tooltips.push(new window.bootstrap.Tooltip(link, {
                title: label ? label.textContent.trim() : link.getAttribute('title'),
                placement: 'right',
                trigger: 'hover focus'
            }));
        });
    }

    function isCollapsed() {
        return root.classList.contains(COLLAPSED_CLASS);
    }

    function syncTooltips() {
        var enabled = isCollapsed() && desktop.matches;
        tooltips.forEach(function (tooltip) {
            if (enabled) {
                tooltip.enable();
            } else {
                tooltip.hide();
                tooltip.disable();
            }
        });
    }

    function syncToggle() {
        if (!toggle) {
            return;
        }
        var collapsed = isCollapsed();
        var label = collapsed ? 'Mostrar menú' : 'Ocultar menú';
        toggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
        toggle.setAttribute('aria-label', label);
        toggle.setAttribute('title', label);
    }

    function apply(collapsed) {
        root.classList.toggle(COLLAPSED_CLASS, collapsed);
        try {
            localStorage.setItem(STORAGE_KEY, collapsed ? 'collapsed' : 'expanded');
        } catch (e) {
            // Sin almacenamiento (modo privado estricto): el estado dura hasta recargar.
        }
        syncToggle();
        syncTooltips();
    }

    if (toggle) {
        toggle.addEventListener('click', function () {
            apply(!isCollapsed());
        });
    }

    if (desktop.addEventListener) {
        desktop.addEventListener('change', syncTooltips);
    } else if (desktop.addListener) {
        desktop.addListener(syncTooltips);
    }

    // Estado inicial: lo puso el <head>; aquí solo se alinean el botón y los tooltips, sin escribir en el storage.
    syncToggle();
    syncTooltips();
})();
