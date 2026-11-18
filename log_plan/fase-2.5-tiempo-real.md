# Fase 2.5 — Confirmación en tiempo real, expiración y cancelación

**Estado:** ✅ hecho 2026-11-12 · [← Plan](../PLAN_DESARROLLO.md#fase-2--ventas-núcleo-25-semanas)

## Alcance (del plan)
**Confirmación en tiempo real**: `codi.qr.payment_received` → crea `Payment` (con comisión calculada) → `Sale.Paid` → SignalR → pantalla de "Pago recibido". Expiración y cancelación de venta.

## Qué se hizo
- Dominio:
  - `Domain/Payments/Commission`: `EffectiveRate(override, global)` y `Calculate`, que redondea a centavos con el punto medio hacia arriba. Ejemplos: $150.50 × 3 % = $4.52; $0.01 ⇒ $0.00.
  - `Payment`: `PeyTransactionId` único, monto, `PaidAt`, clave de rastreo, banco del pagador, `CommissionRate`, `CommissionAmount` y `StatementId` (sin FK hasta la Fase 5).
  - En `Sale`: `PaidAt`, `CancelledAt`, `MarkPaid`, `Cancel`, `ExpireIfDue` y `EffectiveStatus(now)`. **El pago manda**: una venta vencida o cancelada que recibe un pago queda `Paid`, porque el dinero ya llegó y CoDi no permite anular un QR. Un segundo pago se registra y la venta sigue pagada (con warning en el log).
- `IPaymentRecorder` (Infrastructure/Payments/PaymentRecorder), reutilizable por la 2.9:
  - es idempotente por `transactionId`;
  - busca la venta por `PeyQrId` y luego por `saleId` de la metadata; si `merchantId` o `saleId` contradicen a la venta ⇒ `Failed`; `kind` `statement`/`static` ⇒ `Ignored` (Fase 5 / 2.7);
  - guarda el `Payment` y la venta pagada en un solo `SaveChanges` y, después, avisa con `ISaleNotifier`.
- Tiempo real:
  - hub `/hubs/ventas` (`SalesHub`, rol Merchant, grupo `merchant-{id}` por el claim) con el evento `PaymentReceived {saleId}`; `/hubs` sin sesión responde 401;
  - `GET /api/sales/{id}` devuelve el estatus efectivo y `paidAtText`, formateado por el servidor en hora de CDMX. Responde 404 con la venta de otro vendedor y 401 sin sesión;
  - `sale-qr.js` escucha SignalR (`@microsoft/signalr@9.0.19` vía LibMan) y confirma con la API. Si SignalR no está conectado hace polling cada 3 s, hasta 2 min después de la expiración, y se detiene con 404/401/403. Al pagarse oculta el QR, la cuenta regresiva y los avisos, muestra «Pago recibido» y vibra.
- Expiración: job `expirar-ventas` cada minuto (`ISaleExpiration`, `ExecuteUpdateAsync` condicionado a `Pending`). El índice `(Status, ExpiresAt)` lo cubre.
- Cancelación: botón «Cancelar cobro» con confirmación (modal) que avisa que el QR podría seguir funcionando hasta que expire. `POST /app/cobrar/venta/{id}/cancelar` (antiforgery) ⇒ `ISaleCharging.CancelSaleAsync`, con un UPDATE condicionado que nunca pisa un pago que llega en paralelo. Una venta pagada, vencida o ya cancelada recibe un aviso; la de otro vendedor da 404.
- Vista de la venta: estados pendiente, pagada, vencida y cancelada. Las vencidas y canceladas siguen escuchando, por si llega un pago tardío.
- Migración `WebhooksAndPayments`: `Payments`, `WebhookEvents`, `Sales.PaidAt`/`CancelledAt` e índice `(Status, ExpiresAt)`.

## Pruebas
- unitarias: `CommissionTests`, `SaleLifecycleTests`, `WebhookEventTests`;
- integración: `PeyWebhookTests`, `SaleLifecycleTests`, `BankAccountPollingJobTests` y `SalesHubTests` (cliente SignalR real por long polling: el dueño recibe el evento y otro vendedor no);
- 456/456 verdes en local (282 en CI).

## Revisión en navegador
Con el MCP de Playwright (Edge) a 375, 768 y 1280 px, sin llamar a Pey: ventas insertadas por SQL y webhooks firmados a mano desde PowerShell.
- Con SignalR conectado, el pago cambia la pantalla a «Pago recibido» sin recargar y con una sola consulta de confirmación.
- Con `/hubs` bloqueado, el polling cada 3 s detecta el pago.
- Modal de cancelar y vista cancelada; un pago tardío sobre una venta cancelada también cambia a «Pago recibido».
- El job de expiración vence la venta y la cuenta regresiva expira en el cliente.
- En todos los tamaños: sin scroll horizontal, nada tapado por la barra y pestaña «Cobrar» activa. Consola sin errores y sin respuestas ≥ 400.
- Hangfire registró los dos jobs en `HangFire.Hash` con zona `America/Mexico_City`.
- Se corrigió: doble punto tras la hora del pago y formato distinto entre JS y servidor (⇒ `paidAtText`); aviso «Cancelaste el cobro» duplicado con el bloque de cancelado; polling que seguía tras un 404.

## Pendientes
- pago real de $0.01 escaneando el QR con una app bancaria, con el webhook registrado en portal.mipey.mx (túnel HTTPS);
- confirmar con Pey la tolerancia del timestamp, la política de reintentos del webhook y si un QR con monto acepta varios pagos.

## Corrección (2026-11-13): primer pago real por el túnel
Pey cobró, pero el webhook no llegó. Hubo dos causas. Primero, el túnel no estaba hospedado (`devtunnel host`). Segundo, con el perfil `https` el sitio respondía 307 a `https://localhost:7180`. Se agregó `UseForwardedHeaders` (`Web/Hosting/ForwardedHeadersSetup`, solo loopback por defecto) y `ForwardedHeadersTests`. La venta 24 ($0.01) quedó pendiente de conciliar.
