# Pagos, webhooks, tiempo real y jobs

Notas de implementación que estaban en `CLAUDE.md` (movidas sin cambios el 2026-11-26). Léelas antes de tocar el webhook de Pey, el registro de pagos, las transiciones de `Sale`, SignalR, Hangfire, la conciliación o el túnel local, y actualízalas aquí si cambias algo de lo que describen.

- Webhook de Pey (`POST /api/webhooks/pey`, `Web/Controllers/PeyWebhooksController`):
  - firma verificada con `Infrastructure/Webhooks/StandardWebhookVerifier` (propio, tolerancia de 5 min; vector oficial en sus pruebas). Sin firma válida ⇒ 401; sin `Pey:WebhookSecret` ⇒ 500.
  - `IPeyWebhookProcessor` (Application/Webhooks; Infrastructure/Webhooks/PeyWebhookProcessor) guarda en `WebhookEvents` (único por `webhook-id`) y **procesa en la misma petición**. Respuestas:
    - procesado, repetido, ignorado o fallido permanente ⇒ 200;
    - falla transitoria ⇒ 500 y el evento queda `Received`, así que el reintento de Pey con el mismo id lo reprocesa;
    - un id ya `Processed`/`Ignored` no se repite.
  - Despacho por `type`:
    - `codi.qr.payment_received` ⇒ `IPaymentRecorder` (canal `Qr`);
    - `codi.pn.payment_received` ⇒ `IPaymentRecorder` (canal `PushNotification`; el payload no trae `codiQrId`);
    - `bank.account.updated` ⇒ `ApplyWebhookAsync`;
    - `payment_link.status_updated` ⇒ `ISubscriptionPaymentRecorder` (pago de la suscripción, 5.6; contrato `PeyPaymentLinkStatusUpdated`, metadata en `transactions[].metadata.externalMetadata`, estatus con `PeyJson.ParsePaymentLinkStatus`). Sin `paymentLinkId`, estatus desconocido o sin cargo ⇒ `Failed`.
  - Payloads en `Infrastructure/Pey/PeyWebhookContracts.cs` (`datePaid` en segundos Unix). Nunca loguear el body (trae la CLABE del pagador).
- Pagos: `IPaymentRecorder.RecordCodiPaymentAsync(ReceivedCodiPayment)` (Application/Payments; Infrastructure/Payments/PaymentRecorder) lo usan el webhook y la conciliación. `ReceivedCodiPayment.Channel` vale `Qr` o `PushNotification`.
  - Es idempotente por `Payments.PeyTransactionId` (índice único).
  - Con `Qr` busca la venta por `Sales.PeyQrId` y luego por `saleId` de la metadata. Con `PushNotification` la busca por `Sales.PeyTransactionId` y luego por `saleId`; en el respaldo la venta debe ser un push sin transacción o con la misma, y si no tenía se le asigna.
  - Si el canal no coincide con `Sale.Method` o la metadata contradice a la venta ⇒ `Unmatched`. `kind` `static` ⇒ `Ignored` (QR fijo, 2.7).
  - Crea un `Payment` (`Payment.Record`, sin comisión) y llama `Sale.MarkPaid`, en un solo `SaveChanges`. Después avisa con `ISaleNotifier`.
- Transiciones de `Sale`:
  - `MarkPaid`: el pago manda, también desde `Expired` y `Cancelled`.
  - `Cancel`: solo `Pending`. Cancelar es local, porque CoDi no anula el QR.
  - `ExpireIfDue` y `EffectiveStatus(now)`: una pendiente vencida se reporta `Expired`.
  - En Infrastructure, la cancelación (`ISaleCharging.CancelSaleAsync`) y la expiración (`ISaleExpiration`) usan `ExecuteUpdateAsync` condicionado a `Status == Pending`: nunca pisan un pago que llegó en paralelo.
- Tiempo real:
  - hub `Web/Hubs/SalesHub` en `/hubs/ventas` (`[Authorize(Roles=Merchant)]`, grupo `merchant-{id}` por el claim). Evento `ISaleNotifier.PaymentReceivedEvent` con `{saleId}`;
  - `SignalRSaleNotifier` en el sitio; `NullSaleNotifier` fuera de él;
  - `GET /api/sales/{id}` (`SalesApiController`) devuelve `{id, status (efectivo), paidAt, paidAtText}`;
  - `wwwroot/js/sale-qr.js` escucha SignalR (`wwwroot/lib/microsoft-signalr/dist/browser/signalr.min.js`) y confirma con la API. Si SignalR no está conectado, hace polling cada 3 s, hasta 2 min después de la expiración, y se detiene con 404/401/403;
  - `/hubs` sin sesión responde 401 (no redirige), como `/api`.
- Jobs (Hangfire 1.8, `Web/Jobs/JobsSetup`):
  - almacenamiento en la misma base, esquema `HangFire` (lo crea Hangfire, no EF), sin reintentos automáticos;
  - jobs recurrentes en `America/Mexico_City`: `sondeo-cuentas-bancarias` (`*/30 * * * *`), `expirar-ventas` (cada minuto), `conciliar-pagos` (cada minuto), `conciliar-links` (`*/10 * * * *`, links de la suscripción) y `suscripciones-diario` (`0 9 * * *`, recordatorios de la suscripción, 5.7). Las clases van en `Infrastructure/Jobs` y se registran en DI;
  - `Jobs:Enabled` vale `true` por defecto. Las pruebas lo apagan con `UseSetting` y llaman a los jobs directo. **Ojo**: con el sitio arriba, el sondeo llama a Pey real (`GET /bank-account/{id}`) para las cuentas en revisión de la base de desarrollo, y la conciliación también lo hace (`GET /transaction/…`) para las ventas sin pagar de las últimas 24 h, igual que `conciliar-links` (`GET /payment-link/{id}`) para los cargos de la suscripción sin abonar de los últimos 60 días. `suscripciones-diario` no llama a Pey. Para revisar en navegador sin Pey: `Jobs__Enabled=false` y `Pey__ApiKey=` vacío.
- **Conciliación de pagos** (respaldo del webhook, `Infrastructure/Jobs/PaymentReconciliationJob`):
  - Calendario en `Domain/Sales/PaymentReconciliationSchedule.DueAt(now)`, una expresión que EF traduce (`IsDue` es para las pruebas). Se consulta una venta no pagada (también vencida o cancelada) con QR dinámico + `PeyQrId` o con push + `PeyTransactionId`:
    - la primera vez 2 min después de crearse;
    - cada 3 min mientras está vigente;
    - una consulta final 10 min después de vencer;
    - nunca si venció hace más de 24 h.

    `Sales.PaymentCheckedAt` guarda la última consulta. Toma hasta 50 ventas por corrida, con un scope por venta.
  - `IPaymentReconciliation.ReconcileSaleAsync(saleId)` (Application/Payments; Infrastructure/Payments/PaymentReconciliation):
    - QR: `GET /transaction/codi/qr/{idc}`, con hasta 10 páginas. Push: `GET /transaction/{id}`;
    - cada transacción `PAID` pasa por `IPaymentRecorder`, así que es idempotente frente al webhook;
    - si Pey respondió, sella la consulta; un `NOT_FOUND` también sella;
    - si falla (no disponible, otro error o falta la API key), no sella y reintenta en la siguiente corrida.
  - El push sin transacción confirmada no se puede conciliar (falta el barrido por cuenta bancaria, pendiente).
  - En pruebas, `FakePaymentGateway.CodiQrTransactions`/`Transactions` (+ `Transaction(...)`, `CodiQrPageSize`, `TransactionLookupExceptions`).
- Webhook en local: túnel HTTPS (dev tunnels: `devtunnel host local133-pey`, puerto 5231 por HTTP; URL `https://local133-pey-5231.usw3.devtunnels.ms/api/webhooks/pey`) y revisar "Webhook Logs" en portal.mipey.mx. El sitio respeta `X-Forwarded-Proto`/`X-Forwarded-For` (`Web/Hosting/ForwardedHeadersSetup`, `UseForwardedHeaders` primero en el pipeline): sin eso, con el perfil `https` el túnel recibía un 307 a `https://localhost:7180`. Solo se confía en loopback; un proxy de producción en otra máquina va en `ForwardedHeaders:KnownProxies` (IPs) o `ForwardedHeaders:KnownNetworks` (CIDR). Para la revisión en navegador sin Pey: arrancar con `Pey__WebhookSecret=<secreto de prueba>` y mandar un `codi.qr.payment_received` firmado a mano (HMAC-SHA256 de `"{id}.{ts}.{body}"`, igual que `StandardWebhookVerifier.ComputeSignature`), con las ventas insertadas por SQL (`sqlcmd -I`: los índices filtrados exigen `QUOTED_IDENTIFIER`).
- Peticiones abandonadas por el cliente (cancelación de la base con `RequestAborted` cancelado): `Web/Hosting/ClientDisconnectedExceptionFilter` responde 499 sin página de error ni log `ERR`.
