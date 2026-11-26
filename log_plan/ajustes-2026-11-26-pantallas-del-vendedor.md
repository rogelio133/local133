# Ajustes 2026-11-26 — Bloque 3: pantallas del vendedor

**Estado:** 🚧 en curso (punto 9 ✅ 2026-11-26) · [← Plan](../PLAN_DESARROLLO.md) · [Documento de los ajustes](../actualizaciones/ajustes_2026_11_26.md)

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

## 10. Ventas: resumen del día y paginación
Pendiente.

## 11. «Más» con tarjetas
Pendiente.
