# Fase 3.2 — Cobro con productos (carrito)

**Estado:** ✅ hecho 2026-11-14 · [← Plan](../PLAN_DESARROLLO.md#fase-3--productos-y-carrito-15-semanas)

## Alcance (del plan)
Pantalla de venta con catálogo: búsqueda, agregar/quitar, cantidades, total → mismo flujo de QR. `SaleItems` guarda snapshot de precio.

## Decisiones
- página propia `/app/cobrar/productos` con el selector «Monto | Productos» arriba de las dos páginas de cobro;
- concepto **automático y editable**;
- cobro con **QR o Celular** (mismo selector y mismas reglas que el cobro por monto).

## Qué se hizo
- Dominio:
  - `Domain/Sales/SaleItem` copia nombre, precio unitario, cantidad (1–999, `SaleItem.MaxQuantity`) e importe del producto (`FromProduct`; un producto dado de baja ⇒ excepción). Lleva `MerchantId` y `ProductId` (referencia).
  - `SaleCart`: `MaxLines = 50` productos distintos y `Total`.
  - `Sale.Items`; `StartDirect` y `StartPush` aceptan `items` opcionales: con productos, `Source = Cart`, el monto debe ser la suma, como máximo 50 productos y todos del mismo vendedor.
- Tabla `SaleItems` (migración `SaleItems`): checks de cantidad y precio, FK a `Sales` en cascada (si Pey rechaza, la venta se borra con sus productos), FK `Restrict` a `Products` y filtro multi-tenant. Sin FK a `Merchants`, para evitar dos rutas de cascada.
- Aplicación:
  - `CreateDirectSaleResult` se renombró a `CreateSaleResult`.
  - `CreateCartSaleCommand(UserId, Lines, ExpectedTotal, Concept, Method, PhoneNumber)` con su validador. Las reglas de concepto, método y celular se comparten (`SaleChargeRules`). Mensajes en `SaleCartMessages`.
  - `ISaleCharging.CreateCartSaleAsync`.
- Infraestructura (`SaleCharging`):
  - el tramo común (vendedor + `CanCharge` → expiración → folio → QR o push) se extrajo y lo usan los dos casos de uso;
  - el carrito toma nombres y precios del catálogo vigente del vendedor (`IgnoreQueryFilters` + `MerchantId` explícito). Producto ajeno o dado de baja ⇒ `ProductUnavailable`; total > $999,999.99 ⇒ `TotalTooHigh`; total distinto del que vio el vendedor (`ExpectedTotal`) ⇒ `PricesChanged`. En los tres casos no se llama a Pey;
  - al reintentar por folio repetido también se desconectan los productos del intento fallido.
- Web:
  - `ChargeController`: `GET/POST app/cobrar/productos` y `GET app/cobrar/productos/catalogo` (fragmento `_CartCatalogResults`, `no-store`). La pestaña «Cobrar» queda activa sin tocar el layout.
  - La consulta de la lista paginada salió de `ProductsController` a `Areas/Merchant/Services/ProductListQuery` (la usan productos y carrito; `ProductListViewModel.ListPath` arma la paginación de cada página). `_ProductRowContent` se movió a `Areas/Merchant/Views/Shared`.
  - Parciales `_ChargeModeNav`, `_ChargeMethodSelector` y `_PhoneField` (este con `<partial for="Input">`), compartidos por las dos pantallas de cobro. Base `ChargeOptionsInput` (concepto, método, celular) para `ChargeInput` y `CartChargeInput`.
  - Pantalla del carrito: buscador con `product-search.js` (sin cambios); cada producto es un botón que lo agrega, y su «+» muestra la cantidad que ya lleva. La venta va en un `offcanvas-lg offcanvas-bottom`: en móvil y tablet es un panel inferior que se abre con la barra fija «N productos · $total · Ver venta» (sobre la barra de pestañas); en escritorio es una columna fija con su propio scroll.
  - `wwwroot/js/cart.js`:
    - agregar, −/+, cantidad tecleada (solo dígitos, 1–999), quitar; máximo 50 productos y aviso si el total pasa del máximo;
    - campos ocultos `Input.Lines[i].ProductId/Quantity` + `Input.ExpectedTotal`;
    - concepto «2 Taco al pastor, Refresco» (con « y N más» si pasa de 100 caracteres) mientras el vendedor no lo edite, y «Usar texto automático»;
    - `sessionStorage` por vendedor (sobrevive a recargar; se limpia al enviar). Si el POST vuelve con errores, el servidor manda los productos vigentes con precio actual (`InitialLines`) y se abre el panel.
  - Vista de la venta: `<details>` cerrado «Ver N productos» (para que el QR se vea sin desplazarse). En ventas con carrito, «Nuevo cobro» vuelve al catálogo.

## Pruebas
285 unitarias (`SaleCartTests`) y 397 de integración. `CartSaleTests`:
- página, catálogo vacío, bloqueos, fragmento con búsqueda y paginación, sin bajas ni productos ajenos;
- QR y push con copia de productos y concepto editado;
- errores sin llamar a Pey: producto ajeno o dado de baja, precio cambiado, cantidades 0/1000/texto, sin productos, repetido, > 50, total > máximo, total ilegible, concepto inválido;
- Pey rechaza ⇒ venta y productos borrados; push sin respuesta ⇒ cancelada con productos;
- la copia no cambia al editar o borrar el producto; webhook ⇒ pagada; antiforgery; aislamiento; sin sesión.
- 682/682 verdes en local (411 en CI).

## Revisión en navegador
Con el MCP de Playwright (Edge) a 375, 768 y 1280 px, sin llamar a Pey. El sitio se arrancó con `Pey:ApiKey` en blanco, sin jobs y con un secreto de webhook de prueba, con un vendedor y 25 productos insertados por SQL.
- Agregar, contador en el catálogo, −/+, cantidad tecleada (`12a` ⇒ `12`), total y campos ocultos correctos; concepto automático, editado y «Usar texto automático»; concepto con `@` y celular vacío ⇒ errores en cliente sin POST.
- Búsqueda y paginación sin recargar. Recargar conserva el carrito.
- Envío ⇒ «No pudimos generar el QR…» (el log confirma que no salió la llamada) con el carrito rearmado y el panel abierto.
- Venta con carrito insertada por SQL: QR visible sin desplazarse, lista de productos; webhook firmado ⇒ «Pago recibido» sin recargar.
- El cobro por monto sigue igual con los parciales.
- Sin scroll horizontal, «Cobrar» activa, consola sin errores y sin respuestas ≥ 400.
- Se corrigió:
  - la barra «Ver venta» se partía en varias líneas a 375 px;
  - el campo de cantidad quedaba aplastado entre los botones (27 px);
  - la barra tapaba la paginación al final de la página (alto real de 84 px);
  - en escritorio el botón de cobrar quedaba fuera de la pantalla con varios productos (la columna ahora tiene su propio scroll).

## Pendientes
- cobro real de $0.01 con carrito (QR y celular) y su webhook por el túnel (requiere confirmación);
- ~~historial y recibo con productos (3.3)~~ → hecho en la [2.8](fase-2.8-historial-y-recibo.md) (2026-11-14).
