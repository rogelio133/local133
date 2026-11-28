// Formulario de producto (/app/productos/nuevo y /editar): foto con «Tomar foto» (cámara) o «Elegir imagen».
// 1. Los dos input[type=file] se llaman Input.Photo: al elegir en uno se vacía el otro (el servidor toma uno solo).
// 2. Vista previa de lo elegido y «Quitar foto» (en edición marca Input.RemovePhoto para borrar la guardada).
// 3. Revisa tipo y tamaño antes de enviar (misma regla que ProductPhotoRules en servidor).
// 4. Reduce en el navegador las fotos grandes a 2048 px (JPEG 0.85): menos datos móviles. Si el navegador no puede,
//    se sube el original y el servidor decide.
(function () {
    'use strict';

    var ACCEPTED = ['image/jpeg', 'image/png', 'image/webp'];
    var SHRINK_MAX_SIZE = 2048;
    var SHRINK_ABOVE_BYTES = 1.5 * 1024 * 1024;
    var MESSAGE_TYPE = 'No pudimos leer la imagen. Usa una foto JPG, PNG o WebP.';
    var MESSAGE_SIZE = 'La foto pesa más de 10 MB. Elige una más ligera.';

    var form = document.querySelector('form[data-product-form]');
    if (!form) {
        return;
    }

    var maxBytes = Number(form.getAttribute('data-max-photo-bytes')) || 10 * 1024 * 1024;
    var inputs = Array.prototype.slice.call(form.querySelectorAll('input[data-photo-input]'));
    var image = form.querySelector('[data-photo-image]');
    var placeholder = form.querySelector('[data-photo-placeholder]');
    var removeButton = form.querySelector('[data-photo-remove]');
    var removeFlag = form.querySelector('[data-photo-remove-flag]');
    var error = form.querySelector('[data-photo-error]');
    var reselect = form.querySelector('[data-photo-reselect]');
    var busy = form.querySelector('[data-photo-busy]');
    var submit = form.querySelector('[data-product-submit]');
    var objectUrl = null;
    var pending = 0;

    function setError(message) {
        error.textContent = message || '';
        error.classList.toggle('field-validation-error', !!message);
        error.classList.toggle('field-validation-valid', !message);
    }

    function show(src) {
        if (src) {
            image.src = src;
            image.classList.remove('d-none');
            placeholder.classList.add('d-none');
            removeButton.classList.remove('d-none');
        } else {
            image.removeAttribute('src');
            image.classList.add('d-none');
            placeholder.classList.remove('d-none');
            removeButton.classList.add('d-none');
        }
    }

    function releasePreview() {
        if (objectUrl) {
            URL.revokeObjectURL(objectUrl);
            objectUrl = null;
        }
    }

    function clearInputs(except) {
        inputs.forEach(function (input) {
            if (input !== except) {
                input.value = '';
            }
        });
    }

    function setBusy(isBusy) {
        pending += isBusy ? 1 : -1;
        busy.classList.toggle('d-none', pending === 0);
        submit.disabled = pending > 0;
    }

    function isAccepted(file) {
        // Algunos navegadores no informan el tipo: el servidor revisa el contenido de todos modos.
        return !file.type || ACCEPTED.indexOf(file.type) !== -1;
    }

    // Foto reducida a SHRINK_MAX_SIZE px como JPEG; null si no hace falta o el navegador no puede.
    function shrink(file) {
        if (typeof window.createImageBitmap !== 'function' || typeof window.DataTransfer !== 'function') {
            return Promise.resolve(null);
        }
        // createImageBitmap ya aplica la orientación EXIF en los navegadores actuales.
        return window.createImageBitmap(file).then(function (bitmap) {
            var scale = Math.min(1, SHRINK_MAX_SIZE / Math.max(bitmap.width, bitmap.height));
            if (scale === 1 && file.size <= SHRINK_ABOVE_BYTES) {
                bitmap.close();
                return null;
            }
            var canvas = document.createElement('canvas');
            canvas.width = Math.max(1, Math.round(bitmap.width * scale));
            canvas.height = Math.max(1, Math.round(bitmap.height * scale));
            var context = canvas.getContext('2d');
            context.fillStyle = '#fff';
            context.fillRect(0, 0, canvas.width, canvas.height);
            context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
            bitmap.close();
            return new Promise(function (resolve) {
                canvas.toBlob(function (blob) {
                    if (!blob || blob.size >= file.size) {
                        resolve(null); // no ganó nada: se sube el original
                        return;
                    }
                    var name = (file.name || 'foto').replace(/\.[^.]*$/, '') + '.jpg';
                    resolve(new File([blob], name, { type: 'image/jpeg', lastModified: Date.now() }));
                }, 'image/jpeg', 0.85);
            });
        });
    }

    function replaceFile(input, file) {
        var transfer = new DataTransfer();
        transfer.items.add(file);
        input.files = transfer.files; // no dispara «change»
    }

    function onChange(event) {
        var input = event.target;
        var file = input.files && input.files[0];
        if (!file) {
            return;
        }

        clearInputs(input);
        if (reselect) {
            reselect.classList.add('d-none');
        }
        if (!isAccepted(file)) {
            input.value = '';
            setError(MESSAGE_TYPE);
            return;
        }

        setError('');
        removeFlag.value = 'false'; // una foto nueva reemplaza la guardada
        releasePreview();
        objectUrl = URL.createObjectURL(file);
        show(objectUrl);

        setBusy(true);
        shrink(file).catch(function () {
            return null; // formato que el navegador no decodifica: decide el servidor
        }).then(function (smaller) {
            if (smaller && input.files && input.files[0] === file) {
                replaceFile(input, smaller);
            }
            var current = input.files && input.files[0];
            if (current && current.size > maxBytes) {
                input.value = '';
                releasePreview();
                show(removeFlag.value === 'true' ? null : image.getAttribute('data-current-src'));
                setError(MESSAGE_SIZE);
            }
        }).finally(function () {
            setBusy(false);
        });
    }

    inputs.forEach(function (input) {
        input.addEventListener('change', onChange);
    });

    // Las etiquetas hacen de botones: Enter o Espacio abren el selector (con el mouse ya lo hace el navegador).
    form.querySelectorAll('[data-photo-trigger]').forEach(function (label) {
        label.addEventListener('keydown', function (event) {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                document.getElementById(label.getAttribute('for')).click();
            }
        });
    });

    removeButton.addEventListener('click', function () {
        clearInputs(null);
        releasePreview();
        setError('');
        if (image.getAttribute('data-current-src')) {
            removeFlag.value = 'true';
        }
        show(null);
    });

    window.addEventListener('pagehide', releasePreview);

    // 5. Categoría: el nombre de la nueva solo se ve (y se envía) con «+ Nueva categoría…». Sin JS se ven los dos.
    var categorySelect = form.querySelector('[data-category-select]');
    var newCategory = form.querySelector('[data-new-category]');
    if (categorySelect && newCategory) {
        var newCategoryInput = newCategory.querySelector('[data-new-category-name]');
        var newCategoryLabel = newCategory.querySelector('[data-new-category-label]');
        var NEW_VALUE = 'nueva';

        newCategoryLabel.textContent = newCategoryLabel.getAttribute('data-js-text');

        // Si el servidor regresó un nombre o un error en ese campo, se queda la opción «nueva».
        var hasError = !!newCategory.querySelector('.field-validation-error');
        if (newCategoryInput.value.trim() !== '' || hasError) {
            categorySelect.value = NEW_VALUE;
        }

        var syncCategory = function (focus) {
            var isNew = categorySelect.value === NEW_VALUE;
            newCategory.classList.toggle('d-none', !isNew);
            newCategoryInput.disabled = !isNew;
            if (isNew && focus) {
                newCategoryInput.focus();
            }
        };

        categorySelect.addEventListener('change', function () {
            syncCategory(true);
        });
        syncCategory(false);
    }
})();
