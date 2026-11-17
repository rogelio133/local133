# Fase 2.6 — Cobro por celular

**Estado:** ✅ hecho 2026-11-13 · [← Plan](../PLAN_DESARROLLO.md#fase-2--ventas-núcleo-25-semanas)

## Alcance (del plan)
**Cobro por celular**: `POST /codi/pn` + webhook `codi.pn.payment_received`.

## Qué se hizo
- Formato real (docs.pey.mx, revisado el 2026-11-13):
  - `POST /codi/pn` recibe `{amount (>0, ≤1,000,000), phoneNumber ^[0-9]{10}$, concept, expiration ≥360, numRef, bankAccountId, metadata}` y devuelve `{transactionId, codiId (20 dígitos)}`;
  - el webhook `codi.pn.payment_received` es como el de QR pero **sin `codiQrId`**: `{type, amount, apiKeyId, bankAccountId?, concept, datePaid, metadata{…, trackingKey, externalMetadata}, numRef (de Banxico), transactionId}`. La venta se encuentra por el `transactionId` que devolvió el POST.
  - No hay webhook de rechazo ni de expiración: la venta expira por tiempo, igual que el QR.
- Dominio:
  - `Domain/Sales/PhoneNumber`: `Normalize` quita espacios, `-`, `( )`, `.` y la lada `+52`; `IsValid` exige 10 dígitos ASCII; `Mask` ⇒ `•••• 5678`.
  - `Sale.StartPush` (`Method = PushNotification`, `Source = DirectAmount`; comparte con `StartDirect` la validación de monto, concepto y folio) y `AttachPushNotification(transactionId, codiId)`.
  - Columnas `Sales.PeyTransactionId` (índice único filtrado), `PeyCodiId` y `PhoneNumber` (migración `SalePushNotification`). `HasPaymentRequest`: la venta ya tiene QR o push enviado.
- Caso de uso: el mismo `ISaleCharging.CreateDirectSaleAsync`. `CreateDirectSaleCommand` suma `Method` (QR/push) y `PhoneNumber`; el validador exige el celular solo con push (`SalePhoneMessages`). La expiración es la misma de `SystemSettings.SaleQrExpirationSeconds` (600): Pey exige ≥ 360 en push, y con "0 = no expira" el push lanza una excepción.
- Fallas de Pey en el push:
  - una validación ⇒ se borra la venta («Revisa el monto y el celular, o cobra con QR»);
  - configuración u otra falla ⇒ se borra la venta y se muestra el mensaje genérico;
  - **`Unavailable` (timeout/5xx, resultado incierto) ⇒ la venta no se borra: queda `Cancelled`**, porque la notificación pudo llegarle al cliente. El controlador redirige a la venta, que lo explica («no se pudo confirmar…»). Si el cliente paga, el webhook la encuentra por la `saleId` de la metadata, le asigna el `transactionId` y `MarkPaid` gana. `GatewayFailed` lleva un `SaleId?` para este caso.
  - El celular solo se loguea con `PhoneNumber.Mask`.
- Pagos: `IPaymentRecorder.RecordCodiQrPaymentAsync` se renombró a `RecordCodiPaymentAsync`, y `ReceivedCodiPayment.Channel` vale `Qr`/`PushNotification`.
  - Con push se busca por `Sales.PeyTransactionId` y luego por la `saleId` de la metadata; la venta debe ser un cobro por celular sin transacción o con la misma.
  - Con QR, una venta push encontrada por metadata ⇒ `Unmatched`.
  - La idempotencia, la comisión y el aviso por SignalR no cambian.
- Webhook: `PeyCodiPnPaymentReceived` + `CodiPnPaymentAsync`, que comparte con el de QR la conversión y el mapeo de resultados.
- Web:
  - `/app/cobrar` lleva el selector **QR | Celular** (radios `btn-check`). Con Celular aparece «Celular de tu cliente» (`type=tel inputmode=numeric`, ayuda «Tu cliente debe tener CoDi activo…») y el botón dice «Enviar cobro».
  - `[MexicanPhone]` con adaptador `data-val-phone` en `validation.js`; obligatorio con `data-val-required` en el input y revisión en el controlador solo con push.
  - `wwwroot/js/charge-form.js` muestra/oculta y habilita/deshabilita el campo (con QR no se envía ni se valida), cambia el botón, filtra a 10 dígitos (al pegar `+52 (55) 1234-5678` ⇒ `5512345678`) y revalida al pegar si el campo ya tenía error. Sin JS el campo del celular no aparece; el QR ya requería JS.
  - Vista de la venta: con push, en lugar del QR muestra un teléfono, «Enviamos el cobro al celular •••• 5678» y «Pídele a tu cliente que acepte la solicitud…», con la misma cuenta regresiva, la espera y la cancelación. Los textos de vencida, cancelada y el modal dicen «solicitud». `sale-qr.js` funciona igual sin QR.

## Pruebas
- 229 unitarias (`PhoneNumberTests`; push en `SaleTests`) y 299 de integración (`PushSaleTests`: formulario, cuerpo enviado, celular inválido sin llamar a Pey, QR sin celular, método inválido, bloqueo, fallas de Pey, resultado incierto + pago posterior, webhook, idempotencia, transacción desconocida, cruces QR↔push, metadata de otro vendedor, aislamiento, cancelar y vencida). 528/528 verdes en local (322 en CI).

## Revisión en navegador
Con el MCP de Playwright (Edge) a 375, 768 y 1280 px, sin llamar a Pey: el sitio se arrancó sin `Pey:ApiKey` y con un secreto de webhook de prueba.
- Selector QR ↔ Celular y errores en cliente sin POST (vacío, 9 dígitos, 3 dígitos); letras filtradas al teclear y pegado con lada; al volver a QR se limpian los errores.
- Envío con push ⇒ mensaje genérico, conservando el método y el celular.
- Ventas push insertadas por SQL: pendiente (celular enmascarado, cuenta regresiva, modal), vencida y sin confirmar.
- Webhooks `codi.pn.payment_received` firmados desde PowerShell ⇒ «Pago recibido» sin recargar, por SignalR. La venta sin confirmar también se pagó, por metadata.
- Sin scroll horizontal, nada tapado por la barra, pestaña «Cobrar» activa, consola sin errores y sin respuestas ≥ 400.
- Se corrigió: el error del celular se quedaba al pegar con el mouse (⇒ revalidar en `input`).

## Pendientes
- cobro real de $0.01 por push a un celular con CoDi activo y su webhook por el túnel (requiere confirmación);
- confirmar con Pey qué responde si el celular no tiene CoDi (¿400 o 200 sin que llegue nada?) y si hay aviso de rechazo del pagador.
