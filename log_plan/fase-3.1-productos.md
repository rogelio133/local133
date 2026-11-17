# Fase 3.1 — CRUD de productos con baja lógica y foto

**Estado:** ✅ hecho 2026-11-14 · [← Plan](../PLAN_DESARROLLO.md#fase-3--productos-y-carrito-15-semanas)

## Alcance (del plan)
CRUD de productos con baja lógica, foto.

## Qué se hizo
- Dominio: `Domain/Products/Product`.
  - Nombre de hasta 80 caracteres con el mismo juego de caracteres del concepto (`Sale.ConceptTextPattern`), porque en la 3.2 los nombres formarán el concepto que va a Pey. Descripción opcional de hasta 500. Precio con los límites de una venta ($0.01–$999,999.99, centavos).
  - `ReplacePhoto`/`RemovePhoto` devuelven la llave anterior. `Delete` es baja lógica e idempotente; un producto dado de baja ya no se modifica.
  - `Merchant.CanManageProducts`: todos salvo `Disabled`. Desde que se registra (cuenta bancaria pendiente o en revisión) el vendedor ya arma su catálogo; un suspendido también, porque la suspensión solo frena los cobros. `Disabled` ve la lista en solo lectura.
- Tabla `Products` (migración `Products`): `Price decimal(18,2)` con check `> 0`, FK `Restrict`, índice `(MerchantId, IsDeleted, Name)` y filtro multi-tenant. La baja lógica **no** va en el filtro global (EF admite uno por entidad y `IgnoreQueryFilters` quitaría también el del tenant): las consultas usan `!IsDeleted`.
- Archivos: `IFileStorage` (Application/Storage) con `LocalDiskFileStorage` (Infrastructure/Storage; `Storage:LocalDirectory`, por defecto `App_Data/uploads`, fuera de git). Valida la llave con un patrón estricto (nunca sale de la carpeta) y escribe a un temporal + renombrar.
- Fotos: **SkiaSharp 4.156** (`Infrastructure/Products/SkiaProductPhotoProcessor`) en lugar de ImageSharp. La 3.1.12 de ImageSharp tiene vulnerabilidades conocidas (una alta) y la 3.2.0 exige una llave de licencia de Six Labors para compilar en Release (CI y publish fallaban). SkiaSharp es MIT y su DLL nativa está firmada por Microsoft (Smart App Control no la bloquea). Para CI en Ubuntu se agregó `SkiaSharp.NativeAssets.Linux.NoDependencies`.
  - Detecta el formato por el contenido: solo JPEG, PNG y WebP (HEIC, GIF, texto ⇒ «Usa una foto JPG, PNG o WebP»). Rechaza > 50 MP sin decodificar. Los JPEG grandes se decodifican ya reducidos (2048 px).
  - Endereza según la orientación EXIF, reduce a 1024 px y hace una miniatura cuadrada de 240 px, sobre fondo blanco. Guarda JPEG calidad 80. El codificador de Skia no escribe EXIF: no se guarda la ubicación (GPS) del celular.
  - Llaves `products/{merchantId}/{guid}.jpg` y `…-thumb.jpg`; `Product.PhotoKey` guarda la base.
- Caso de uso `IProductCatalog` (Application/Products; Infrastructure/Products/ProductCatalog). Valida (FluentValidation, mensajes en `ProductMessages`), procesa y guarda la foto, guarda el producto y, si la base falla, borra los archivos nuevos. Después borra los de la foto reemplazada o quitada. La baja conserva la fila y la foto.
- Web: `ProductsController` en `/app/productos` (pestaña «Más», con liga desde `/app/mas`):
  - lista **paginada de 20 en 20** (`?pagina=`; fuera de rango se ajusta a la primera o la última), ordenada por nombre, con búsqueda por nombre (`?q=`, se conserva al paginar), estado vacío y «sin resultados». La paginación es reutilizable: `Web/Models/PageInfo` + parcial `Views/Shared/_Pager` («Anterior · Página X de Y · Siguiente», para que quepa en 375 px);
  - `nuevo` y `{id}/editar` (multipart, límite de 12 MB por petición, PRG con aviso) y `{id}/eliminar` con modal de confirmación;
  - `{id}/foto` y `{id}/foto/miniatura` sirven el JPEG desde `IFileStorage` con `Cache-Control: private, max-age=31536000, immutable` (la URL cambia con cada foto: `?v={guid}`). El producto de otro vendedor da 404.
- Foto en el formulario: **«Tomar foto»** (`<input type=file accept="image/*" capture="environment">`, abre la cámara trasera del celular) y **«Elegir imagen»** (`accept="image/jpeg,image/png,image/webp"`: galería o archivos). Los dos campos se llaman `Input.Photo`. «Tomar foto» se oculta en equipos con mouse (`@media (hover: hover) and (pointer: fine)`), donde `capture` no aplica. `wwwroot/js/product-form.js`:
  - vacía el otro campo al elegir y muestra la vista previa; «Quitar foto» (en edición marca `Input.RemovePhoto`);
  - revisa tipo y tamaño (10 MB) antes de enviar;
  - reduce en el navegador las fotos de más de 1.5 MB o de más de 2048 px (JPEG 0.85, `createImageBitmap` + `canvas` + `DataTransfer`): menos datos móviles. Si el navegador no puede, sube el original y decide el servidor.

  Si el formulario vuelve con otro error, se pide volver a elegir la foto (el navegador no puede rellenar un campo de archivo).
- Corrección de paso: `/error/{code}` leía el código por model binding. Al re-ejecutar un POST con un formulario ilegible (petición de más del límite), MVC intentaba leer el formulario, `code` quedaba en 0 y la página de error respondía 500. Ahora lo lee de la ruta y responde 400.

## Pruebas
- 267 unitarias (`ProductTests`, `MerchantCanManageProductsTests`) y 350 de integración. Nuevas: `ProductPhotoProcessorTests` y `LocalDiskFileStorageTests` (sin SQL Server, corren en CI: formatos, orientación EXIF, sin EXIF, transparencia, truncado, > 50 MP, llaves inválidas) y `ProductCrudTests` (alta con y sin foto, datos y fotos inválidos, > 10 MB, > límite sin 500, edición, reemplazar y quitar foto, baja lógica, paginación 20/20/5, página fuera de rango, búsqueda + página, aislamiento, `Disabled`, antiforgery, admin, sin sesión). La fábrica guarda las fotos en una carpeta temporal propia. 617/617 verdes en local (384 en CI).

## Revisión en navegador
Con el MCP de Playwright (Edge) a 375, 768 y 1280 px, sin llamar a Pey: estado vacío; errores en cliente sin POST (vacíos, solo espacios, emoji y `@`, tercer decimal filtrado, descripción tope 500); alta con «Elegir imagen» (JPEG de 4000×3000 reducido en el navegador de 1.96 MB a 625 KB); PNG transparente ⇒ fondo blanco; archivo de texto con `.jpg` ⇒ error del servidor; editar, quitar foto y modal de eliminar; 45 productos insertados por SQL ⇒ paginación y búsqueda con paginación; nombres largos recortados con «…». Con pantalla táctil emulada (CDP) aparece «Tomar foto» con `capture="environment"`; con mouse se oculta. Sin scroll horizontal, la paginación no queda bajo la barra, pestaña «Más» activa, consola sin errores y sin respuestas ≥ 400. Se corrigió: el aviso «Corrige los datos y vuelve a elegir la foto» salía también cuando el error era la foto misma.

## Ajuste 2026-11-14: búsqueda asíncrona
Antes el buscador era un GET de página completa (parecía un "post"). Ahora `wwwroot/js/product-search.js` busca al escribir (300 ms) y de inmediato con Enter o el botón, sin recargar.
- Pide `GET /app/productos/resultados` (`ProductsController.Results`, `Cache-Control: no-store`): devuelve la parcial `_ProductResults` (conteo, lista y `_Pager`), la misma que pinta la lista completa. Cada búsqueda cancela la anterior (AbortController).
- La paginación y «Ver todos» también se cargan así. La barra queda en `/app/productos?q=…&pagina=…` (`replaceState` al escribir, `pushState` al paginar, `popstate` para atrás/adelante).
- Si la respuesta falla o redirige (sesión vencida), navega a la página completa: el login conserva la búsqueda en `ReturnUrl`. Sin JS el formulario sigue siendo un GET normal.
- Causa del error al dar Enter: en el log, `GET /app/productos` respondió 500 con `SqlException: Operation cancelled by user`. Pasa cuando el navegador abandona la petición (otro Enter u otra navegación) mientras la consulta corre. Con la página real no se reprodujo: Enter siempre respondió 200. Nuevo `Web/Hosting/ClientDisconnectedExceptionFilter` (filtro global de MVC): si la petición fue abandonada y la excepción es `OperationCanceledException` o `DbException` (también envuelta), responde 499, sin página de error y con log `Debug`. Antes quedaba `ERR` + re-ejecución de `/error`.
- Pruebas: `ProductCrudTests` (fragmento con búsqueda y página, sin resultados, sin búsqueda, aislamiento, admin y sin sesión) y `ClientDisconnectedExceptionFilterTests` (sin base, corre en CI). 631/631 verdes en local.
- Revisión con Playwright (Edge) a 375, 768 y 1280 px. Al escribir y con Enter, solo `GET …/resultados` (sin recarga). Probado: paginar (sube al inicio), atrás/adelante, «Ver todos», sin resultados y sesión vencida ⇒ login con `ReturnUrl`. 5 búsquedas seguidas ⇒ 4 canceladas (499 `INF` en el log, sin `ERR`) y gana la última. Sin scroll horizontal, paginador sobre la barra, «Más» activa, consola sin errores.

## Pendientes
- probar «Tomar foto» en un celular real (Android e iPhone): Playwright no puede abrir la cámara;
- confirmar que CI (Ubuntu) carga los binarios nativos de SkiaSharp (`NoDependencies`) en el próximo push;
- en producción, cambiar `LocalDiskFileStorage` por blob (Fase 7).
