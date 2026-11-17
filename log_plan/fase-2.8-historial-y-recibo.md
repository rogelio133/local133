# Fase 2.8 — Historial de ventas, resumen del día y recibo compartible (+ 3.3)

**Estado:** ✅ hecho 2026-11-14 · [← Plan](../PLAN_DESARROLLO.md#fase-2--ventas-núcleo-25-semanas)

## Alcance (del plan)
- 2.8: **Historial de ventas** + resumen del día; **recibo compartible** (URL pública por token, botón WhatsApp).
- 3.3 (cerrada aquí): detalle de venta con productos en historial y recibo. Los `SaleItems` ya existían desde la 3.2.

## Decisiones
- La 3.3 se cerró junto con la 2.8.
- El resumen del día va completo en Ventas y compacto en Inicio («Hoy: $X · N ventas pagadas · Ver ventas»). En Inicio solo aparece si el vendedor ya cobra (activo, suspendido o dado de baja), no durante el onboarding.
- Solo una venta **pagada** tiene recibo. Con cualquier otro estatus la liga responde 404 y no revela nada.
- El token del recibo se genera al **crear** la venta, no al compartirla. Así la pantalla del QR trae el botón oculto y `sale-qr.js` lo muestra al llegar el pago, sin recargar.
- El recibo nunca muestra comisión, celular, CLABE ni ids de Pey.
- En un cobro por celular, el botón principal manda el recibo al chat de ese cliente. El número no puede quedar escrito en la página, porque la regla del proyecto dice que el celular solo se muestra con `PhoneNumber.Mask` (la prueba `PushSaleTests` lo exige). Por eso el botón apunta a `GET /app/ventas/{id}/whatsapp`, que redirige a `wa.me/52…`. «Enviar a otro contacto» abre el selector de WhatsApp.

## Qué se hizo
- Dominio:
  - `Domain/Common/MexicoTime`: `Zone`, `ToLocal`, `DateOf`, `Today` y `DayRange(desde, hasta)`, con el fin exclusivo y en UTC. Lo reutilizará el corte semanal (Fase 5). `SaleQrViewModel.FormatPaidAt` ya lo usa.
  - `Sale.ReceiptToken` (32 hex = 16 bytes de `RandomNumberGenerator`) se asigna en `StartDirect`/`StartPush`. También `Sale.NewReceiptToken`, `IsValidReceiptToken` (solo minúsculas) y `HasReceipt` (= `Paid`).
  - `Sale.ConceptTextOf(concepto, negocio)` es el inverso de `BuildConcept`: el historial, el detalle y el recibo muestran el concepto sin el nombre del negocio. Si el negocio cambió de nombre, se muestra completo.
  - **Para la 2.7**: el constructor de ventas `OpenAmount` también debe asignar `ReceiptToken`.
- Migración `SaleReceiptToken`:
  - agrega `ReceiptToken` `char(32)`;
  - rellena las ventas existentes con `LOWER(CONVERT(char(32), CRYPT_GEN_RANDOM(16), 2))`. En la base de desarrollo quedaron 24 tokens, todos distintos;
  - después vuelve la columna NOT NULL y crea un índice único;
  - agrega el índice `(MerchantId, CreatedAt)` para el historial.
- Web, área del vendedor:
  - `SalesController` (`/app/ventas`) reemplaza a `HomeController.Sales` y su `_ComingSoon`. Lo usan todos los vendedores, también suspendidos o dados de baja (solo lectura).
  - Historial (`Services/SalesHistoryQuery`):
    - chips de estatus `?estado=pagadas|pendientes|vencidas|canceladas` sobre el **estatus efectivo**: una pendiente con el QR vencido cuenta como vencida;
    - fechas `?desde=&hasta=` (AAAA-MM-DD, hora de México). Las ilegibles se ignoran y las invertidas se intercambian. Atajos «Hoy» y «Últimos 7 días» que conservan el estatus;
    - páginas de 20 con `PageInfo` + `_Pager`, agrupadas por día («Hoy», «Ayer», «12 de noviembre»). Cada fila muestra el ícono del método (QR, celular o carrito).
  - Resumen del día (`DaySummaryQuery`):
    - cobrado = suma de `Payments.Amount` con `PaidAt` hoy en México;
    - ventas pagadas = ventas distintas de esos pagos;
    - esperando pago = pendientes vigentes.
  - Detalle `/app/ventas/{id}` (la venta ajena da 404):
    - estatus efectivo, monto, concepto, fechas y método (con el celular enmascarado);
    - **productos** (3.3) y **pagos**, con clave de rastreo y comisión (`CommissionRate`/`CommissionAmount` del pago);
    - «Ver QR» o «Ver cobro» si la venta sigue esperando pago;
    - si está pagada, el bloque «Recibo para tu cliente».
  - Parcial `_ReceiptShare`: WhatsApp, «Ver recibo», y «Copiar liga» y «Compartir» (Web Share API). Estos dos los muestra `wwwroot/js/receipt-share.js` solo si el navegador los soporta.
  - Pantalla de la venta: el bloque `data-paid-only` trae el recibo y «Ver detalle de la venta». `sale-qr.js` no cambió.
  - `_MerchantLayout`: la pestaña «Ventas» queda activa en la lista y en el detalle.
  - Fechas: `SaleFormats`. La hora y el «3 %» llevan espacios de no separación para no partirse entre renglones (Razor los escribe como `&#xA0;`).
- Recibo público `ReceiptsController` (`GET /recibo/{token}`, `[AllowAnonymous]`):
  - un token con formato inválido da 404 sin consultar la base;
  - la consulta usa `IgnoreQueryFilters()`: quien abre la liga puede tener sesión de otro vendedor;
  - encabezados `X-Robots-Tag: noindex, nofollow`, `Referrer-Policy: no-referrer` y `Cache-Control: private, no-store`. `_Head` agrega `<meta name="robots">` con `ViewData["NoIndex"]`;
  - muestra negocio, monto pagado, concepto, productos, folio, fecha, «CoDi (transferencia)» y las claves de rastreo. Tiene botón «Imprimir o guardar PDF»: el `_Layout` público oculta la barra y el pie con `d-print-none`, y en `@media print` el fondo queda blanco.
- `ReceiptLinks` (singleton, `Web/Models`):
  - URL absoluta: usa `App:PublicBaseUrl` (`PublicSiteOptions`) o, si no está, el esquema y el host de la petición;
  - mensaje «Recibo de pago de {negocio} / Venta N por $X / liga»;
  - `wa.me/?text=…` o, en la redirección, `wa.me/52{celular}?text=…`.

## Pruebas
312 unitarias y 434 de integración: 746/746 verdes en local, 449 en el filtro de CI. Build Release sin advertencias y `has-pending-model-changes` limpio.
- Unitarias:
  - `MexicoTimeTests`: bordes del día en UTC−6, rango inclusivo, cambio de año y rango invertido;
  - `SaleReceiptTests`: token distinto por venta y en push, formatos inválidos, `HasReceipt` por estatus, `ConceptTextOf`.
- `SalesHistoryTests`:
  - lista vacía, solo las ventas propias y en orden;
  - estatus efectivo, fechas en hora de México, paginación;
  - resumen (pagos de hoy contra anteriores, pago adicional, ajenos) en Ventas y en Inicio; sin resumen en el onboarding; vendedor suspendido;
  - detalle con carrito, pagos y comisión; pendiente con «Ver QR»;
  - push: celular nunca escrito en la página y redirección a WhatsApp, que exige venta propia, pagada y con celular;
  - detalle ajeno da 404; sin sesión redirige al login; recibo oculto en el QR hasta el pago.
- `ReceiptTests`:
  - sin sesión: datos correctos, nada interno, encabezados noindex;
  - carrito, push sin celular, otro vendedor con sesión;
  - pendiente, vencida y cancelada dan 404; token inválido, inexistente o en mayúsculas da 404.
- `ReceiptLinksTests` (sin base, corren en CI): dominio configurado o el de la petición, mensaje, chat del cliente sin número.

## Revisión en navegador
Con el MCP de Playwright (Edge) a 375, 768 y 1280 px, sin llamar a Pey. El sitio se arrancó sin `Pey:ApiKey`, sin jobs y con un secreto de webhook de prueba. Se registró un vendedor nuevo; su confirmación y sus 26 ventas (pagadas, pendiente, vencida, cancelada, carrito y celular, de varios días) se insertaron por SQL.
- `/app/ventas`: resumen, chips, filtro de fechas («Últimos 7 días» + «Pagadas» ⇒ 17 ventas, «Quitar fechas», «Ver todas»), grupos por día y paginación.
- Detalles:
  - carrito: productos, pagos con comisión y recibo;
  - celular: «Enviar recibo al •••• 5678» + «Enviar a otro contacto». El HTML no contiene el número completo;
  - pendiente: «Ver QR».
- «Copiar liga» ⇒ «Liga copiada.».
- Pantalla del QR de una pendiente + webhook firmado a mano ⇒ «Pago recibido» con los botones del recibo, sin recargar.
- Recibo sin sesión y con sesión, y vista de impresión.
- Inicio con la tarjeta «Hoy».
- Sin scroll horizontal, «Ventas» activa, consola sin errores ni advertencias y sin respuestas ≥ 400.
- Se corrigió:
  - el resumen de Inicio partía «· 1 / esperando pago»;
  - la hora se partía entre «p.» y «m.» en el detalle y el recibo;
  - el recibo recortaba con «…» el nombre de los productos. Ahora se muestra completo; se volvió a revisar a 375 px.

## Ajuste 2026-11-14: WhatsApp de escritorio
- **Problema** (reportado al probar en Windows): el botón abría `wa.me`, cuya página intermedia no detectó la app de WhatsApp de la Microsoft Store, aunque el protocolo `whatsapp:` sí está registrado en el equipo.
- **Solución**: en escritorio (`(hover: hover) and (pointer: fine)`, el mismo criterio que oculta «Tomar foto»), `receipt-share.js` cambia cada botón a su `data-desktop-href`:
  - «a otro contacto» usa `whatsapp://send?text=…` (`ReceiptLinks.WhatsAppAppUrl`);
  - «al cliente» usa `/app/ventas/{id}/whatsapp?destino=app`, que redirige a `whatsapp://send?phone=52…&text=…`;
  - quita el `target="_blank"`, porque un protocolo externo en pestaña nueva deja una pestaña vacía;
  - muestra el respaldo «¿No se abre la app de WhatsApp? Usa WhatsApp Web» (`web.whatsapp.com/send`, o `?destino=web` para el cliente).
- En el celular se sigue usando `wa.me`.
- El celular completo sigue sin escribirse en la página.
- Pruebas: `ReceiptLinksTests` (ligas a la app y a la web, `WhatsAppUrlFor`) y redirecciones `?destino=app|web` en `SalesHistoryTests`. 751/751 verdes.
- Revisado con Playwright a 1280 y 375: ligas cambiadas, respaldo visible, número ausente y consola limpia. **Falta confirmar** que el clic abra la app instalada: Playwright no puede comprobar la apertura de un protocolo externo.

## Pendientes
- confirmar en Windows que «Enviar a otro contacto» y «Enviar recibo al •••• 5678» abren la app de WhatsApp de escritorio, incluida la redirección del sitio a `whatsapp://`;
- abrir el recibo y los botones de WhatsApp y «Compartir» en un celular real (Android e iPhone), y ver cómo previsualiza WhatsApp la liga;
- en producción, fijar `App:PublicBaseUrl` con el dominio público (ver `azure.md`).
