# Ajustes 2026-11-26 — Bloque 3: pantallas del vendedor

**Estado:** 🚧 en curso (puntos 9 y 10 ✅ 2026-11-26) · [← Plan](../PLAN_DESARROLLO.md) · [Documento de los ajustes](../actualizaciones/ajustes_2026_11_26.md)

Puntos 9 (recibo tipo ticket), 10 (Ventas: resumen del día y paginación) y 11 («Más» con tarjetas). Esta bitácora se completa conforme se termina cada punto.

## 9. Recibo tipo ticket ✅

### Qué se hizo
- **Folio solo con sesión**: `ReceiptViewModel.ShowFolio`, que `ReceiptsController.Show` llena con `User.Identity?.IsAuthenticated == true` (cualquier rol, decisión). Sin sesión no sale la fila «Venta» (`data-receipt-folio`). El recibo ya respondía `private, no-store`.
- **Vista** (`Views/Receipts/Show.cshtml`): sin `card` ni `list-group`.
  - `ViewData["BodyClass"] = "receipt-page"`: fondo `$rosa-papel` para que el ticket blanco se distinga.
  - `.receipt-ticket-wrap` (sombra) › `article.receipt-ticket` (dientes) con secciones `.receipt-ticket-section` separadas por una línea punteada: encabezado, productos (solo en carrito), datos y «Cobrado con Local133» (gratuito).
  - En el carrito, fila **Total** al final de los productos (`data-receipt-total`).
  - Ícono `bi-check-circle-fill` a 4rem (`.receipt-check`).
  - Se conservan todos los `data-receipt-*` anteriores (pruebas de 2.8, 3.3 y 5.10).
- **Estilos** (`Styles/site.scss`, sección «Recibo tipo ticket»):
  - dientes arriba y abajo con una `mask` de dos `conic-gradient` en `repeat-x` (zigzag de 90°, `--receipt-tooth: .875rem`), sin imágenes;
  - la sombra (`filter: drop-shadow`) va en el contenedor: en el mismo elemento, `filter` se aplica antes que `mask` y no seguiría los dientes;
  - `tabular-nums` en todo el ticket; ancho máximo 26rem;
  - impresión: fondo blanco y la sombra cambia por un contorno de 1 px, para que los dientes se vean en papel. Ese `@media print` va **después** de las reglas de pantalla: en el bloque `@media print` de arriba perdía por orden (misma especificidad), y así lo detectó la revisión en navegador.

### Pruebas
- `ReceiptTests` (integración):
  - sin sesión: trae `receipt-ticket` y `receipt-check`, y **no** `data-receipt-folio`;
  - otro vendedor con sesión ve el folio;
  - nueva `El_admin_con_sesion_ve_el_folio`;
  - carrito: `data-receipt-total>$70.00<`.
- `dotnet test --filter "Receipt|ValueVisible|Branded|Storefront"`: 49 ✅.

### Revisión en navegador (Playwright, Edge)
Sitio en Debug en `http://localhost:5231` con `Jobs__Enabled=false Pey__ApiKey= Resend__ApiKey=`, sin sesión, con ventas pagadas que ya estaban en la base de desarrollo:
- **375**: carrito con logo (3 productos y Total): dientes arriba y abajo, sin folio, sin scroll horizontal (`scrollWidth` = `clientWidth`).
- **768**: monto directo con logo.
- **1280**: gratuito con «Cobrado con Local133» dentro del ticket.
- **Impresión** (`emulate_media print`): sin barra, pie ni botón; fondo blanco y dientes con contorno. Antes del arreglo seguía el fondo rosa.
- Consola sin errores ni advertencias.
- El folio con sesión lo cubren las pruebas de integración. En el navegador solo se revisó sin sesión.

### Pendientes
- Ninguno del punto 9.

## 10. Ventas: resumen del día y paginación ✅

### Qué se hizo
- **Resumen del día** (`Areas/Merchant/Views/Sales/Index.cshtml`):
  - las tres tarjetas en `col-4`, también a 375 px;
  - etiquetas en dos renglones (`Cobrado<br>hoy`, `Pagadas<br>hoy`, `Esperando<br>pago`, `lh-sm`);
  - relleno `px-2 px-sm-3`, para ganar ancho en el celular.
- **Monto**: `.day-summary-amount` (`clamp(1rem, 4.5vw, 1.5rem)`, `text-nowrap`). Si el texto pasa de 10 caracteres (más de «$99,999.99»), la vista agrega `.day-summary-amount-long` (`clamp(.8125rem, 3.4vw, 1.25rem)`). Los conteos siguen en `fs-4`. Se conservan todos los `data-day-*` con el texto pegado (`data-day-collected>$…<`). La tarjeta de Inicio no cambió.
- **Paginador** (parcial compartido `Views/Shared/_Pager.cshtml`, así que cambia en las 9 pantallas):
  - «Anterior» y «Siguiente» son `btn btn-outline-primary rounded-pill pager-btn`, de 44 px de alto como mínimo;
  - los deshabilitados son `<span class="… disabled" aria-disabled="true">` sin `data-pager-*`;
  - el estado «Página X de Y» va en `fw-semibold`, centrado y con `text-wrap: balance`;
  - `<nav class="pager">` y `<ul class="pager-list">` sustituyen a `.pagination`;
  - `data-pager`, `data-page`, `data-total-pages`, `data-pager-previous|next|status` y el orden `href` → `rel` no cambian. Por eso `product-search.js` y `admin-customer-search.js` siguen igual.
- **Estilos** (`Styles/site.scss`, sección «Ventas y paginación»):
  - `.pager-btn` baja `--bs-btn-padding-x/y` a `1rem`/`.5rem`: con el relleno de marca (1.5rem), el estado quedaba con una palabra por renglón a 375 px. Se hizo con las variables del propio botón, sin pisar `.btn`;
  - `.pager` es un contenedor (`container-type: inline-size`). Con `@container (max-width: 20rem)`, el estado sube a su propio renglón y los botones quedan debajo, a los lados. Así pasa en el catálogo del carrito a 375 px, donde no cabía ni en dos renglones.

### Pruebas
- `SalesHistoryTests`:
  - `Pagina_de_20_en_20`: `pager-btn`, el deshabilitado como `<span … disabled aria-disabled>` y sin `data-pager-previous` en la página 1;
  - el resumen trae las etiquetas en dos renglones;
  - nueva `Un_monto_del_dia_largo_usa_la_letra_mas_chica` ($1,200,000.00).
- Siguen pasando las pruebas del paginador que ya existían (`ProductCrudTests` busca «Página 1 de 3», admin y tienda).
- `dotnet test` con filtro de Ventas, Productos, carrito, admin y tienda: 142 ✅. `dotnet test` completo con la versión final: 702 unitarias + 61 de la portada + 874 de integración ✅.

### Revisión en navegador (Playwright, Edge)
Sitio en Debug en `http://localhost:5231` con `Jobs__Enabled=false Pey__ApiKey= Resend__ApiKey=`, con vendedores y un admin de prueba de la base de desarrollo:
- **Ventas**:
  - a 375, 768 y 1280 px, las tres tarjetas en una fila y sin scroll horizontal;
  - con pagos de prueba de hoy insertados por SQL (y borrados al terminar): «$99,999.99» cabe a 375 px (texto de 85 px en una caja de 87), y «$1,099,999.99» usa la letra chica (13 px, 83 px en 87).
- **Paginador**:
  - en Ventas a 375 px, el estado se parte en dos renglones entre los botones. Antes del ajuste del relleno quedaba con una palabra por renglón. A 768 y 1280 px va en una línea;
  - en el catálogo del carrito a 375 px, el estado va arriba en su propio renglón (container query) y, a 1280 px, entre los botones. «Siguiente» sigue cargando el fragmento con `product-search.js`;
  - en Productos a 375 px y en la tienda pública a 375 px (catálogo publicado por SQL solo para la revisión y regresado después);
  - en Clientes del admin a 1280 px («Siguiente» con `admin-customer-search.js`) y en Transacciones a 375 px.
  - Con el teclado, el botón enfocado se rellena. El deshabilitado se ve tenue y no es liga.
- Consola sin errores ni advertencias. Ningún 404 en el log.

### Incidente durante la revisión
- El primer arranque fue desde PowerShell con `$env:Resend__ApiKey=''`. En PowerShell, asignar `''` **borra** la variable en vez de dejarla vacía, así que el sitio tomó las llaves de Resend y de Pey de los user-secrets.
- Salió de verdad por Resend un correo de «Restablece tu contraseña» a `plan55@prueba.mx` (cuenta de prueba de la base de desarrollo; liga a `localhost`). No hubo llamadas a Pey.
- Se detuvo el sitio y se arrancó desde Bash (`Resend__ApiKey= Pey__ApiKey= dotnet …`), donde la variable sí queda vacía. Quedó anotado en CLAUDE.md.

### Pendientes
- Ninguno del punto 10.

## 11. «Más» con tarjetas
Pendiente.
