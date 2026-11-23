// Imágenes de «Mi información» (/app/mi-informacion): cada form[data-image-upload] sube una sola imagen (logo o foto
// del local) con «Elegir imagen» y, en la foto, «Tomar foto».
// 1. Los input[type=file] del formulario se llaman «image»: al elegir en uno se vacía el otro.
// 2. «Guardar» se habilita solo con una imagen elegida; vista previa de lo elegido.
// 3. Revisa tipo y tamaño antes de enviar (misma regla que MerchantImageRules en servidor).
// 4. Reduce en el navegador las imágenes grandes a 2048 px, en el tipo de data-output-type: PNG en el logo (conserva
//    la transparencia, sin fondo blanco) y JPEG 0.85 en la foto. Si el navegador no puede, sube el original.
(function () {
    'use strict';

    var ACCEPTED = ['image/jpeg', 'image/png', 'image/webp'];
    var SHRINK_MAX_SIZE = 2048;
    var SHRINK_ABOVE_BYTES = 1.5 * 1024 * 1024;
    var MESSAGE_TYPE = 'No pudimos leer la imagen. Usa una imagen JPG, PNG o WebP.';
    var MESSAGE_SIZE = 'La imagen pesa más de 10 MB. Elige una más ligera.';

    function setup(form) {
        var maxBytes = Number(form.getAttribute('data-max-bytes')) || 10 * 1024 * 1024;
        var outputType = form.getAttribute('data-output-type') === 'image/png' ? 'image/png' : 'image/jpeg';
        var inputs = Array.prototype.slice.call(form.querySelectorAll('input[data-image-input]'));
        var image = form.querySelector('[data-image-preview]');
        var placeholder = form.querySelector('[data-image-placeholder]');
        var error = form.querySelector('[data-image-error]');
        var busy = form.querySelector('[data-image-busy]');
        var submit = form.querySelector('[data-image-submit]');
        var objectUrl = null;
        var pending = 0;

        if (!submit || inputs.length === 0) {
            return; // solo lectura
        }

        function hasFile() {
            return inputs.some(function (input) { return input.files && input.files.length > 0; });
        }

        function refreshSubmit() {
            submit.disabled = pending > 0 || !hasFile();
        }

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
            } else {
                image.removeAttribute('src');
                image.classList.add('d-none');
                placeholder.classList.remove('d-none');
            }
        }

        function releasePreview() {
            if (objectUrl) {
                URL.revokeObjectURL(objectUrl);
                objectUrl = null;
            }
        }

        function restore() {
            releasePreview();
            show(image.getAttribute('data-current-src'));
        }

        function setBusy(isBusy) {
            pending += isBusy ? 1 : -1;
            busy.classList.toggle('d-none', pending === 0);
            refreshSubmit();
        }

        function isAccepted(file) {
            // Algunos navegadores no informan el tipo: el servidor revisa el contenido de todos modos.
            return !file.type || ACCEPTED.indexOf(file.type) !== -1;
        }

        // Imagen reducida a SHRINK_MAX_SIZE px en outputType; null si no hace falta o el navegador no puede.
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
                if (outputType === 'image/jpeg') {
                    context.fillStyle = '#fff';
                    context.fillRect(0, 0, canvas.width, canvas.height);
                }
                context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
                bitmap.close();
                return new Promise(function (resolve) {
                    canvas.toBlob(function (blob) {
                        if (!blob || blob.size >= file.size) {
                            resolve(null); // no ganó nada: se sube el original
                            return;
                        }
                        var extension = outputType === 'image/png' ? '.png' : '.jpg';
                        var name = (file.name || 'imagen').replace(/\.[^.]*$/, '') + extension;
                        resolve(new File([blob], name, { type: outputType, lastModified: Date.now() }));
                    }, outputType, 0.85);
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
                restore();
                refreshSubmit();
                return;
            }

            inputs.forEach(function (other) {
                if (other !== input) {
                    other.value = '';
                }
            });
            if (!isAccepted(file)) {
                input.value = '';
                restore();
                setError(MESSAGE_TYPE);
                refreshSubmit();
                return;
            }

            setError('');
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
                    restore();
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
        form.querySelectorAll('[data-image-trigger]').forEach(function (label) {
            label.addEventListener('keydown', function (event) {
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    document.getElementById(label.getAttribute('for')).click();
                }
            });
        });

        // Sin imagen elegida el servidor respondería «Elige una imagen»: se avisa sin enviar.
        form.addEventListener('submit', function (event) {
            if (!hasFile()) {
                event.preventDefault();
                setError('Elige una imagen.');
            }
        });

        refreshSubmit();
        window.addEventListener('pagehide', releasePreview);
    }

    document.querySelectorAll('form[data-image-upload]').forEach(setup);
})();
