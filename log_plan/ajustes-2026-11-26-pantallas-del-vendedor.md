# Ajustes 2026-11-26 — Bloque 3: pantallas del vendedor

**Estado:** ✅ terminado 2026-11-26 (puntos 9, 10 y 11) · [← Plan](../PLAN_DESARROLLO.md) · [Documento de los ajustes](../actualizaciones/ajustes_2026_11_26.md)

Puntos 9 (recibo tipo ticket), 10 (Ventas: resumen del día y paginación) y 11 («Más» con tarjetas).

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

## 11. «Más» con tarjetas ✅

### Qué se hizo
- **Vista** (`Areas/Merchant/Views/Home/More.cshtml`):
  - el título «Más» (`h1.h4`);
  - `ul.row.row-cols-2.row-cols-md-3.g-3` (`data-more-cards`) con una tarjeta por `li`, en lugar del `list-group`;
  - cada tarjeta es la liga completa: título, una línea de descripción y un ícono grande y tenue en la esquina (ver el ajuste de abajo).
- **Tarjetas**: Mi información, Mi plan, Productos, Catálogo en línea, Cuenta bancaria, Invita y gana, **Contraseña y acceso** (nueva, punto 4) y QR de tu puesto.
  - «Contraseña y acceso» va a `/Identity/Account/Manage/ChangePassword` (`data-more-account`). Si la cuenta no tiene contraseña, esa página redirige a `SetPassword`, y su menú trae «Cuentas externas».
  - QR de tu puesto sigue apagada: un `div.more-card.is-disabled` con la insignia «Próximamente».
  - Se conservan todos los `data-more-*`. «Cuenta bancaria» estrena `data-more-bank-account`.
- **Estilos** (`Styles/site.scss`, sección «"Más" con tarjetas»). Es un componente propio con las proporciones de `.step-card` de la portada, sin copiar su SCSS:
  - fondo `$rosa-papel` y radio de 1.5rem;
  - `height: 100%`, para que las alturas queden parejas por fila;
  - hover un poco más rosa y anillo de foco de Bootstrap;
  - transición solo sin «reducir movimiento».
- **Colores**: el mapa `$more-card-colors` genera `.more-card-{clave}` con `--more-card-color`: rosa mexicano, cempasúchil, azul lona, verde nopal, morado `#6F42C1`, naranja `#C2410C` y turquesa `#0E7C86`.
- **Fuera de este punto**: «Instalar app» entra con la PWA (punto 12) y la liga a «Categorías», con el punto 14.

### Pruebas
- `Merchants/MoreTests` (nueva):
  - cada `data-more-*` liga a su ruta (7 casos);
  - la rejilla es `row-cols-2 row-cols-md-3` y ya no hay `list-group`;
  - el QR del puesto es un `div` con `data-coming-soon`;
  - `/Identity/Account/Manage/ChangePassword` con sesión de vendedor ⇒ 200.
- Las que ya revisaban `/app/mas` siguen pasando: `ProfileTests`, `StorefrontTests`, `FreePlanLimitsTests` y `ReferralTests` (99 ✅ con el filtro).
- `dotnet test` completo: 702 unitarias + 61 de la portada + 884 de integración ✅.

### Revisión en navegador (Playwright, Edge)
Sitio en Debug en `http://localhost:5231`, arrancado desde Bash con `Jobs__Enabled=false Pey__ApiKey= Resend__ApiKey=`, con un vendedor nuevo (`mas11@prueba.mx`) confirmado con el correo de `App_Data/emails`:
- **375 px**:
  - 2 columnas de 168 px con alturas parejas por fila;
  - «Contraseña y acceso» y «Catálogo en línea» parten el título en dos renglones;
  - sin scroll horizontal (`scrollWidth` = `clientWidth` = 375);
  - al final de la página, la última fila queda 32 px arriba de la `.tabbar`;
  - pestaña «Más» activa.
- **768 px**: 3 columnas, con las pestañas en la barra superior y «Más» subrayada. Con Tab, el anillo de foco se ve en la tarjeta.
- **1280 px**: 3 columnas.
- **Contraste** del ícono contra su cuadro (primera versión; el cuadro se quitó después), calculado en la página: de 4.58:1 (rosa mexicano) a 9.05:1 (azul lona); todos pasan 3:1. El QR apagado da 4.69:1.
- «Contraseña y acceso» abre «Mi cuenta › Cambiar contraseña».
- Consola sin errores ni advertencias. Ningún 404, error ni advertencia en el log del sitio.

### Ajuste pedido después de la revisión (2026-11-26)
- Borde rosa (`$rosa-mexicano`, 2 px) en cada tarjeta.
- Sin el cuadro del ícono arriba (`.more-card-icon` se quitó): el color de cada tarjeta queda solo en el ícono de la esquina. `$more-card-colors` ahora es un color por clave.
- Título más grande (1.25rem; 1.375rem desde `md`) y centrado. La descripción sigue alineada a la izquierda.
- El ícono de la esquina crece (`scale(1.25)` desde la esquina inferior derecha) y pasa de `.12` a `.3` de opacidad al pasar el mouse o con el foco del teclado. La transición (.25 s) solo va sin «reducir movimiento»; con él, el cambio es inmediato. La tarjeta apagada no tiene el efecto.
- Revisión en Playwright:
  - 375 px: título de 20 px centrado y sin scroll horizontal;
  - 768 px: 3 columnas y título de 22 px;
  - 1280 px: con hover en Productos, opacidad .3 y `matrix(1.25…)`; las demás tarjetas siguen en .12;
  - consola sin errores y log sin 404;
  - `MoreTests` 10 ✅.

### Segundo ajuste pedido (2026-11-26)
- **Borde** de 4 px.
- **Esquinas**: solo la superior derecha y la inferior izquierda van redondeadas (`border-radius: 0 1.5rem`).
- **Ícono de la esquina**: siempre con opacidad .3, la que antes solo tenía al pasar el mouse. Al pasar el mouse (o con el foco) ya solo crece, y la transición es solo de `transform`.
- **Título más grande**. Se ajusta al ancho de la tarjeta:
  - `.more-card` es contenedor (`container-type: inline-size`) y el título usa `min(1.75rem, 17cqi)`, con 1.375rem de respaldo;
  - la palabra más larga («información») mide ~5.6em, así que ocupa a lo más el 96 % del ancho;
  - un `clamp` con `vw` no bastaba: en Edge, la barra de desplazamiento deja 345 px a una ventana de 360, y «información» se salía (118 px en 113).
- El relleno horizontal en el celular bajó a 1rem.
- **Revisión** en Playwright:
  - con 345 px útiles: título de 19 px, la línea más ancha en el 96.4 % y sin scroll horizontal;
  - 375 px: 20.5 px, con las esquinas como se pidieron;
  - 768 px: 28 px, en el tope;
  - 1280 px: 28 px, y con hover en «Invita y gana» el ícono crece;
  - consola sin errores y log sin 404.
- **Nota**: con opacidad .3, el ícono de la esquina queda debajo del final de algunas descripciones en el celular («tu logo.», «tus cobros.»). Se leen bien, pero queda a la vista del usuario.

### Pendientes
- Ninguno del punto 11.
