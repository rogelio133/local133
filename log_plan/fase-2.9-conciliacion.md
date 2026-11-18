# Fase 2.9 — Job de conciliación de pagos

**Estado:** ✅ hecho 2026-11-18 · [← Plan](../PLAN_DESARROLLO.md#fase-2--ventas-núcleo-25-semanas)

## Alcance (del plan)
Job de conciliación (`GET /transaction/codi/qr/{idc}`) para ventas pendientes.

Se decidió:
- incluir también los cobros por celular, con `GET /transaction/{id}`;
- hacerlo solo como job, sin botón «Verificar pago» en la UI;
- dejar para después el barrido nocturno con `GET /transaction/bank-account/{id}` (ver Pendientes).

## Qué se hizo
- **Calendario** (`Domain/Sales/PaymentReconciliationSchedule`): `DueAt(now)` es una expresión que EF traduce a SQL, y `IsDue` la evalúa en memoria para las pruebas. Se consulta una venta que:
  - no está pagada: pendiente, vencida o cancelada, porque cancelar es local y el QR seguía cobrable (`MarkPaid` gana);
  - tiene a quién preguntar: QR dinámico con `PeyQrId`, o push con `PeyTransactionId`. El push sin confirmar queda fuera;
  - tiene `ExpiresAt`, que venció hace menos de 24 h (`MaxLookback`);
  - se creó hace al menos 2 min (`FirstCheckDelay`, para darle tiempo al webhook);
  - y está en uno de dos momentos:
    - vigente: cada 3 min (`RetryInterval`);
    - vencida: una consulta final 10 min después del vencimiento (`FinalCheckDelay`). Después ya no se consulta.

  Con una venta de 600 s salen unas 4 consultas (min 2, 5, 8 y 20).
- `Sale.PaymentCheckedAt` es la última consulta. Migración `SalePaymentCheck`: columna nullable, sin relleno.
- **Caso de uso** `IPaymentReconciliation.ReconcileSaleAsync(saleId)` (Application/Payments; Infrastructure/Payments/PaymentReconciliation):
  - QR: recorre las páginas de `GET /transaction/codi/qr/{idc}` hasta 10. Push: `GET /transaction/{PeyTransactionId}`.
  - Cada transacción `PAID` se convierte en `ReceivedCodiPayment` y pasa por el mismo `IPaymentRecorder` del webhook. Así hereda la idempotencia por `transactionId`, la búsqueda y validación de la venta por IDC/transacción y metadata, la comisión vigente y el aviso por SignalR.
  - Si Pey no trae `datePaid`, se usa `dateCreated` y, si tampoco viene, la hora actual.
  - Si Pey respondió, sella `PaymentCheckedAt` con `ExecuteUpdateAsync`. `NOT_FOUND` también sella, con un warning.
  - Si Pey no está disponible, devuelve cualquier otro error o falta la API key, no sella: la siguiente corrida reintenta.
  - Un pago que no se puede asociar a la venta (`Unmatched`) se loguea como `ERR`. Un pago recuperado (no llegó por webhook), como warning. No se loguean bodies ni la CLABE del pagador.
- **Job** `Infrastructure/Jobs/PaymentReconciliationJob`, `conciliar-pagos`:
  - corre cada minuto (`* * * * *`, en hora de CDMX), programado en `Web/Jobs/JobsSetup`;
  - toma hasta 50 ventas por corrida, las de vencimiento más próximo primero;
  - usa un scope por venta, y si una falla sigue con la siguiente;
  - sin ventas por consultar solo hace una consulta a la base, que usa el índice `(Status, ExpiresAt)`.
- Pruebas: `FakePaymentGateway` simula `GET /transaction/codi/qr/{idc}` (páginas configurables) y `GET /transaction/{id}` (`NOT_FOUND` si no está), con excepciones por recurso.

## Pruebas
- **Unitarias**: `PaymentReconciliationScheduleTests` (15 casos):
  - recién creada;
  - intervalo;
  - espera hasta la consulta final, la consulta final y nada después;
  - límite de 24 h;
  - pagada, cancelada o vencida;
  - sin QR;
  - push con y sin transacción;
  - sin vencimiento.
- **Integración**: `PaymentReconciliationJobTests` (15 casos):
  - pago de QR sin webhook: `Payment` con comisión 3 %, venta `Paid`, aviso, consulta sellada;
  - consulta final de una venta vencida o cancelada;
  - sin pagos: no repite la consulta hasta la final, y después ya no;
  - paginación;
  - pago `PENDING` ignorado;
  - push pagado, pendiente y `NOT_FOUND`;
  - push sin transacción no consultado;
  - webhook posterior al mismo pago ⇒ `Processed` sin duplicar;
  - venta pagada por webhook no consultada;
  - Pey falla en una venta y sigue con la otra, y la reintenta en la siguiente corrida;
  - metadata de otro vendedor ⇒ sin pago ni aviso;
  - recién creadas, sin vencimiento o de hace más de un día no se consultan.
- 327 unitarias y 469 de integración: 796/796 verdes en local. `dotnet ef migrations has-pending-model-changes`: sin cambios.

## Revisión en navegador
No aplica: no se tocaron vistas, layouts, CSS ni JS. Cuando el job registra un pago, la pantalla del QR lo muestra por el mismo camino del webhook (`ISaleNotifier` → SignalR, con respaldo de polling), ya revisado en la 2.5.

## Conciliación real contra Pey (2026-11-18, confirmada por el usuario)
Se hizo una consulta única con `ReconcileSaleAsync`, desde un programa desechable que usó el DI de Infrastructure, la base de desarrollo y los user-secrets. Un decorador de la pasarela solo permitía los dos GET de conciliación.
- Se consultaron las 15 ventas reales de Pey vencidas hace más de 24 h: 14 QR y 1 push, todas del vendedor 4 y de $0.01. Las 7 insertadas a mano para la revisión en navegador no se consultaron.
- **Se recuperaron 3 pagos que nunca llegaron por webhook**:
  - las ventas 7, 24 (la pendiente de la 2.5) y 25 pasaron de `Expired` a `Paid`, con la hora real del pago;
  - comisión de 3 % ⇒ $0.00 sobre $0.01.
- 11 QR sin transacciones: Pey devolvió una lista vacía.
- El push 34 sigue `PENDING` en Pey (`datePaid` null): nunca se pagó.
- Todas quedaron con `PaymentCheckedAt`. No hubo `NOT_FOUND` ni errores.
- **Formato real** confirmado:
  - `GET /transaction/codi/qr/{idc}` devuelve `status: PAID`, `paymentMethod: CODI`, `datePaid` (igual a `dateCreated`), `trackingKey`, `bankCode` y `externalMetadata` como texto (`kind`, `merchantId`, `saleId`);
  - **`payableType` viene vacío** en las transacciones de QR. El código no depende de él;
  - `GET /transaction/{id}` de un push trae `payableType: API_CODI_PN`, y `PENDING` mientras no se paga.

## Pendientes
- **Ojo en desarrollo**: con el sitio arriba y `Pey:ApiKey` configurada, el job llama a Pey real para las ventas sin pagar de la base de desarrollo de las últimas 24 h. Sin API key, registra un warning por venta en cada corrida mientras le toque.
- **Barrido nocturno** con `GET /transaction/bank-account/{id}`: pagos sin venta pendiente (pagos adicionales a una venta ya pagada, push sin transacción confirmada, ventas de hace más de 24 h). Se pospuso; conviene junto con la bitácora de webhooks y el reproceso del admin (Fase 6).
- Confirmar con Pey los límites de peticiones (unas 4 consultas por venta no pagada).
