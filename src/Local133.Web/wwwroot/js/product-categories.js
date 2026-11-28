// Categorías (/app/productos/categorias).
// 1. Modales de renombrar y borrar: uno para cada acción; al abrirlo se pone la categoría del botón que lo abrió.
// 2. Orden: arrastrando el asa (SortableJS, también con el dedo) o con ↑/↓. Con JS, las flechas mueven la fila sin
//    recargar; en los dos casos se manda el orden completo a POST /app/productos/categorias/orden (con el token
//    antiforgery). Si el servidor responde 400 (la lista cambió en otra pestaña), se recarga la página. Sin JS, las
//    flechas son formularios normales.
(function () {
    'use strict';

    var root = document.querySelector('[data-product-categories]');
    if (!root) {
        return;
    }

    // --- 1. Modales ---------------------------------------------------------------------------------------------------
    var renameModal = document.getElementById('rename-category');
    if (renameModal) {
        var renameForm = renameModal.querySelector('[data-category-rename-form]');
        var renameInput = renameModal.querySelector('[data-category-rename-input]');
        renameModal.addEventListener('show.bs.modal', function (event) {
            var button = event.relatedTarget;
            var row = button && button.closest('[data-category]');
            if (!row) {
                return;
            }
            renameForm.action = button.getAttribute('data-action');
            renameInput.value = row.getAttribute('data-name');
            // Errores de una apertura anterior.
            if (window.jQuery && window.jQuery(renameForm).data('validator')) {
                window.jQuery(renameForm).validate().resetForm();
            }
        });
        renameModal.addEventListener('shown.bs.modal', function () {
            renameInput.focus();
            renameInput.select();
        });
    }

    var deleteModal = document.getElementById('delete-category');
    if (deleteModal) {
        var deleteForm = deleteModal.querySelector('[data-category-delete-form]');
        var deleteName = deleteModal.querySelector('[data-category-delete-name]');
        var deleteText = deleteModal.querySelector('[data-category-delete-text]');
        deleteModal.addEventListener('show.bs.modal', function (event) {
            var button = event.relatedTarget;
            var row = button && button.closest('[data-category]');
            if (!row) {
                return;
            }
            var count = Number(button.getAttribute('data-product-count')) || 0;
            deleteForm.action = button.getAttribute('data-action');
            deleteName.textContent = row.getAttribute('data-name');
            deleteText.textContent = count === 0
                ? 'No tiene productos.'
                : count === 1
                    ? 'Su producto queda sin categoría. No se borra.'
                    : 'Los ' + count.toLocaleString('es-MX') + ' productos quedan sin categoría. No se borran.';
        });
    }

    // --- 2. Orden -----------------------------------------------------------------------------------------------------
    var list = root.querySelector('[data-category-list]');
    var tokenInput = root.querySelector('[data-category-order-token] input[name="__RequestVerificationToken"]');
    if (!list || !tokenInput || !window.fetch || list.children.length < 2) {
        return;
    }

    var orderUrl = list.getAttribute('data-order-url');
    var status = root.querySelector('[data-category-status]');
    var error = root.querySelector('[data-category-order-error]');
    var saving = null;
    var pendingSave = false;

    function rows() {
        return Array.prototype.slice.call(list.querySelectorAll(':scope > [data-category]'));
    }

    // Primera ↑ y última ↓ deshabilitadas, como las pinta el servidor.
    function refreshButtons() {
        var all = rows();
        all.forEach(function (row, index) {
            var up = row.querySelector('[data-category-up]');
            var down = row.querySelector('[data-category-down]');
            if (up) {
                up.disabled = index === 0;
            }
            if (down) {
                down.disabled = index === all.length - 1;
            }
        });
    }

    function announce(row) {
        var all = rows();
        status.textContent = '«' + row.getAttribute('data-name') + '» quedó en el lugar ' + (all.indexOf(row) + 1) + ' de ' + all.length + '.';
    }

    // Un guardado a la vez; si se mueve otra fila mientras tanto, se manda el orden final al terminar.
    function save() {
        if (saving) {
            pendingSave = true;
            return;
        }
        var body = new URLSearchParams();
        rows().forEach(function (row) {
            body.append('ids', row.getAttribute('data-category'));
        });
        body.append('__RequestVerificationToken', tokenInput.value);
        list.setAttribute('aria-busy', 'true');
        saving = fetch(orderUrl, {
            method: 'POST',
            credentials: 'same-origin',
            body: body
        }).then(function (response) {
            if (response.status === 400 || response.status === 403 || response.status === 404 || response.redirected) {
                // La lista cambió (otra pestaña), sesión vencida o solo lectura: la página completa sabe qué mostrar.
                location.reload();
                return;
            }
            if (!response.ok) {
                throw new Error('Respuesta inesperada: ' + response.status);
            }
            error.classList.add('d-none');
        }).catch(function () {
            error.classList.remove('d-none');
        }).then(function () {
            saving = null;
            list.removeAttribute('aria-busy');
            if (pendingSave) {
                pendingSave = false;
                save();
            }
        });
    }

    // ↑/↓: mueven la fila aquí mismo y guardan; el foco se queda en la flecha (o pasa a la otra si llegó a un extremo).
    list.addEventListener('submit', function (event) {
        var form = event.target.closest('[data-category-move]');
        if (!form) {
            return;
        }
        event.preventDefault();
        var row = form.closest('[data-category]');
        var up = form.getAttribute('data-category-move') === 'up';
        var sibling = up ? row.previousElementSibling : row.nextElementSibling;
        if (!sibling) {
            return;
        }
        if (up) {
            list.insertBefore(row, sibling);
        } else {
            list.insertBefore(sibling, row);
        }
        refreshButtons();
        var button = form.querySelector('button');
        var other = row.querySelector(up ? '[data-category-down]' : '[data-category-up]');
        (button.disabled ? other : button).focus();
        announce(row);
        save();
    });

    if (window.Sortable) {
        var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        window.Sortable.create(list, {
            handle: '[data-drag-handle]',
            draggable: '[data-category]',
            animation: reduceMotion ? 0 : 150,
            ghostClass: 'category-row-ghost',
            chosenClass: 'category-row-chosen',
            // Solo el asa arrastra: en el celular, el resto de la fila sigue haciendo scroll.
            onEnd: function (event) {
                if (event.oldIndex === event.newIndex) {
                    return;
                }
                refreshButtons();
                announce(event.item);
                save();
            }
        });
    }
})();
