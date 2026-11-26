# Fase 6.4 — Tablero, transacciones globales y bitácora de webhooks (admin)

**Estado:** ✅ hecho 2026-11-25 · [← Plan](../PLAN_DESARROLLO.md#fase-6--portal-administrador-15-semanas)

## Alcance (del plan)
Tablero (GMV, ingresos por suscripción, vendedores por plan, conversión de prueba a pago, lugares de fundador), transacciones globales, bitácora de webhooks con reproceso.

**Demostrable:**
1. El admin entra a `/admin/tablero` y ve el GMV de hoy, del mes y del anterior, los ingresos por suscripción con el IVA desglosado, la tabla de 6 meses, los vendedores por plan y por estatus, la conversión y los lugares de fundador.
2. En `/admin/transacciones` busca una venta por folio, cliente o clave de rastreo y abre su detalle, con los pagos y los webhooks que la mencionan. En la pestaña «Suscripciones» ve los pagos que confirmó Pey.
3. En `/admin/webhooks` filtra los fallidos, abre uno, ve el payload con la CLABE enmascarada y lo reprocesa con un motivo, que queda en el historial.

La 6.3 (cobranza) no está hecha: se hizo la 6.4 primero, por pedido del usuario.

## Decisiones
Del usuario, el 2026-11-25:
- **Transacciones = ventas de todos los vendedores + pagos de la suscripción**, en dos pestañas. La cobranza (cargos `Partial`, links pendientes o pagados sin abonar) sigue siendo de la 6.3.
- **Payload como JSON formateado y enmascarado**: el body crudo nunca va al HTML.
- **Se reprocesan los fallidos, los sin procesar y los ignorados** (todo salvo `Processed`). Pide motivo, queda en `AuditLogs` y no llama a Pey.
- **Sin botón «Conciliar con Pey»**: la conciliación sigue siendo el job.

Propias:
- **GMV** = suma de `Payments.Amount` por `PaidAt` en hora de México, igual que el resumen del día y el resumen mensual. Las ventas se cuentan distintas (una venta con dos pagos cuenta una vez).
- **Ingresos por suscripción** = cargos `Paid` por `PaidAt`, con su `Amount` (el precio con IVA incluido). Incluye los abonados a mano, que cuentan igual que un pago de Pey (6.2). El IVA se desglosa con `SubscriptionPricing.VatBreakdown` sobre la suma.
- **Conversión de prueba a pago**: de los vendedores con un periodo `Trial` que ya terminó, los que tienen al menos un cargo `Paid` (antes o después de terminar la prueba, o abonado a mano). Se muestra el histórico y el de las pruebas que terminaron en los últimos 30 días. Quien sigue en la prueba no cuenta, aunque ya haya pagado.
- **Vendedores por plan con el filtro SQL del listado de clientes** (`CustomerListQuery.WithPlan`, ya comparado con `PlanStatus.For` en la 6.1): cada número coincide con lo que muestra `/admin/clientes?plan=…`, y enlaza ahí.
- **Uso justo de Pro en el tablero**: vendedores con `ProFairUseMonthlyPaid` cobros concretados o más en el mes (los 20 que más cobran). Cierra el pendiente de la 5.5. Es solo una alerta, igual que antes.
- **Webhooks «atorados»**: `Received` desde hace más de 10 minutos (`WebhookLog.StuckAfter`). Una entrega en curso no cuenta.
- **Sin librería de gráficas**: tarjetas, listas y barras `progress` de Bootstrap. No se agregó nada a LibMan.
- **Filtros por GET normal**, sin búsqueda asíncrona: son pantallas de consulta del admin.
- **Búsqueda en el payload** desde 6 caracteres (`LIKE` escapado sobre `nvarchar(max)`, sin índice). Con menos solo se busca el `webhook-id` exacto, y la pantalla lo dice.
- **Auditoría del reproceso después de procesar**, en otro guardado: los manejadores (`PaymentRecorder`, `SubscriptionPaymentRecorder`, `BankAccountStatusSync`) hacen sus propias transacciones. La entrada registra el estatus antes y después, el resultado y el motivo del manejador. `AuditLogs.WebhookEventId` liga la entrada al evento (sin llave foránea).
- **Reproceso concurrente** con un reintento de Pey del mismo evento: no se toma candado. Los manejadores ya son idempotentes (`PeyTransactionId` único en `Payments` y `SubscriptionPayments`, candado de la suscripción al abonar), así que lo peor es un intento de más en el contador.
- El modal de confirmación `_PlanConfirmModal` pasó a `Areas/Admin/Views/Shared` porque ahora lo usan Clientes y Webhooks.
- El parcial `_ComingSoon` se queda sin usos, pero se conserva: es la convención para pantallas pendientes.

## Qué se hizo
- **Domain:**
  - `Webhooks/WebhookPayloadMask.Format`: JSON con sangría, con las llaves `clabe`, `taxId`/`rfc`/`curp` y `phone`/`phoneNumber`… enmascaradas, y cualquier serie de 16 dígitos o más. Si no es JSON, enmascara las series de 10 o más.
  - `Webhooks/WebhookEventText`: textos en español del tipo y el estatus.
  - `WebhookEvent.CanReprocess` y `StartReprocess`.
  - `AuditAction.WebhookReprocessed` y `AuditLog.WebhookEventId`.
  - `Subscriptions/TrialConversion` (`Percent`).
- **Application:**
  - `IPeyWebhookProcessor.ReprocessAsync` ⇒ `WebhookReprocessResult` (`NotFound`, `NotAllowed`, `Done`).
  - `Admin/IAdminWebhookActions`.
- **Infrastructure:**
  - `PeyWebhookProcessor`: se extrajo `ProcessAsync` (despacho, marca y guardado), que comparten `HandleAsync` (sin cambio de comportamiento) y `ReprocessAsync`.
  - `Admin/AdminWebhookActions`: valida el motivo, reprocesa y guarda la auditoría.
  - Migración `AdminDashboard`: la columna y los índices (ver `azure.md`).
- **Web / Admin:**
  - `HomeController.Index` = tablero (`Services/DashboardQuery`, `Models/DashboardViewModels.cs`, `Views/Home/Index.cshtml`).
  - `TransactionsController` (`/admin/transacciones`, `/ventas/{id}`, `/suscripciones`), `Services/TransactionListQuery`, `Models/TransactionViewModels.cs` y vistas `Transactions/*` con el parcial `_TransactionsTabs`. Se borró `Views/Home/Transactions.cshtml`.
  - `WebhooksController` (`/admin/webhooks`, `/{id}`, `POST /{id}/reprocesar`), `Services/WebhookLogQuery` (con las relaciones del payload: venta, cargo de la suscripción o cuenta bancaria), `Models/WebhookViewModels.cs` y vistas `Webhooks/*`.
  - `CustomerListQuery.CountByPlanAsync` (misma proyección y mismo `WithPlan` del listado).
  - `Models/AdminDates.cs` (`AdminDates`, `AdminQueryString`).
  - `_AdminLayout`: «Transacciones» por controlador y «Webhooks» nuevo en el menú.
  - `site.scss`: `.admin-sidebar` con `flex-shrink: 0` y `.admin-main` con `min-width: 0` (ver la revisión).

## Pruebas
- **Unitarias** (corren en CI):
  - `Webhooks/WebhookPayloadMaskTests` (8): CLABE en la metadata y de primer nivel, RFC, celular, mayúsculas, números largos sueltos en cadenas, números y arreglos, texto que no es JSON, vacíos y valores no numéricos.
  - `Webhooks/WebhookEventReprocessTests` (3).
  - `Subscriptions/TrialConversionTests` (6).
- **Integración** (`tests/Local133.IntegrationTests/Admin`, base `AdminTestBase`):
  - `DashboardTests` (9): sin datos, GMV de hoy, del mes y del anterior en los bordes de la medianoche de México con dos vendedores y una venta con dos pagos, ingresos con abono a mano e IVA (sin contar pendientes ni `Partial`), los cinco estados del plan y los estatus con sus ligas, conversión histórica y reciente, fundadores, alerta de webhooks fallidos y atorados, uso justo, y acceso de un vendedor.
  - `TransactionsTests` (8): acceso por rol en las tres rutas, ventas de dos vendedores con cada filtro (estatus efectivo, método, cliente, fechas) y búsqueda (negocio con acentos, transacción del push, clave de rastreo, id del QR, `%` escapado), folio en todos los vendedores, paginación de 20, celular enmascarado, detalle con productos, pagos y webhooks relacionados, 404, y pagos de la suscripción con sus filtros.
  - `WebhookLogTests` (8): un pago fallido que se reprocesa después de existir su venta (pago creado, `Attempts` 2, auditoría, historial y liga a la venta), un ignorado que sigue ignorado, uno procesado que se rechaza, sin motivo, sin token antiforgery, el payload con la CLABE enmascarada (en la base sigue cruda), y los filtros de estatus, tipo, atorados y búsqueda.
  - `AreaAccessTests`: `/admin/transacciones`, `/suscripciones`, `/admin/webhooks` y `/admin/webhooks/1` sin sesión.
- `dotnet build`: sin advertencias.
- `dotnet test`: 675 unitarias, 17 de la portada y 807 de integración, todas correctas (la suite de integración completa, dos veces seguidas). `dotnet build -c Release`: sin advertencias.
- **Falla destapada en la fábrica de pruebas (corregida):** con las clases nuevas, `SalesHubTests.El_vendedor_recibe_PaymentReceived_…` fallaba siempre en la suite completa (no llegaba el aviso de SignalR en 10 s) y pasaba sola. En el commit base, la suite pasaba (778).
  - **Causa:** `RecordingSaleNotifier.Inner` era uno solo y lo asignaba cada sitio al resolver `ISaleNotifier` por primera vez. `PeyWebhookTests` arma un sitio derivado (`WithWebHostBuilder`) que lo sobrescribía con su propio SignalR, y después se desechaba.
  - Antes, el sitio de la fábrica lo resolvía después y lo recuperaba. Las pruebas nuevas mandan webhooks al principio de la corrida, así que el sitio de la fábrica lo resolvió primero y se quedó sin avisos.
  - **Arreglo:** `RecordingSaleNotifier.ForHost(inner)`. Cada sitio registra en la misma cola y reenvía a su propio SignalR. El código del sitio no cambió.
- `dotnet ef migrations has-pending-model-changes`: sin cambios.

## Revisión en navegador
Hecha el 2026-11-25 con el MCP de Playwright y Edge, a 375, 768 y 1280 px. El sitio corrió sin Pey, Resend ni jobs, contra la base de desarrollo, que recibió la migración `AdminDashboard` al arrancar.
- **Sesión:** con un admin temporal `revision64.admin@local133.test`, que creó el seeder con variables de entorno y se **borró** de la base al terminar. Su entrada de auditoría del reproceso se queda (guarda el correo).
- **Datos:** un webhook `codi.qr.payment_received` firmado a mano con un secreto de prueba, de un QR que no existe y con la CLABE de prueba en la metadata. Quedó `Failed` (#19).
- **Revisado:**
  - tablero con los datos reales de desarrollo (46 ventas este mes, 3 cargos pagados, 37 vendedores) y la alerta «1 fallido»;
  - transacciones (las dos pestañas) y el detalle de dos ventas: una insertada por SQL sin webhooks, y la venta 22, cuyo webhook #18 aparece y enlaza de ida y vuelta;
  - bitácora con 19 eventos y el detalle de #19: payload con `•••• 5713`, sin la CLABE completa, desplazándose dentro de su caja;
  - «Reprocesar» sin motivo ⇒ «Escribe el motivo.» sin enviar nada. Con motivo ⇒ «El webhook #19 sigue fallando: …», 2 intentos y la entrada en «Reprocesos del admin»;
  - no se reprocesó el #14 (link de la suscripción ignorado antes de la 5.6): hoy lo procesaría el manejador de links y podría abonar un cargo en la base de desarrollo;
  - menú activo correcto en cada pantalla; consola sin errores ni advertencias; sin scroll horizontal en ninguna pantalla y tamaño.
- **Corregido:** a 1280 px la barra lateral se encogía (la tabla de 6 meses empujaba) y «Transacciones» y «Parámetros» se partían en dos renglones. `.admin-sidebar` ahora no se encoge y `.admin-main` puede hacerlo. Se volvió a medir en las cinco pantallas del admin: barra de 240 px, ningún enlace en dos renglones y sin desbordes.

## Ajuste 2026-11-25: menos carga en la base y Clientes como entrada
Pedido del usuario, después de preguntar si el tablero podía alentar a los vendedores. No los bloquea: la base tiene `READ_COMMITTED_SNAPSHOT` activo (revisado en `Local133` y `Local133_Tests`; Azure SQL lo trae por defecto). El riesgo es el consumo de DTU en Basic/S0.
- **Caché del tablero** (`DashboardCache`, singleton): 5 minutos, con una sola petición calculando a la vez. «Datos de las 12:20 p. m. · se renuevan cada 5 min» y el botón «Actualizar» (`?actualizar=true`, que redirige a la URL limpia).
- **6 meses en 2 consultas** en lugar de 18: pagos y cargos agrupados por un `CASE` con los inicios de mes en UTC. Unas 20 consultas por cálculo, antes unas 36. En la base de desarrollo dio las mismas cifras que la versión anterior.
- **Entrada del admin = Clientes**: `/admin` redirige a `/admin/clientes`, el inicio de sesión de un admin lleva ahí y Clientes va primero en el menú. El tablero pasó a `/admin/tablero` (vista `Home/Dashboard`).
- **Pruebas:** `DashboardTests` suma la caché (5 min con reloj congelado y «Actualizar»), la fila de hace 5 meses y la exclusión de hace 6, y la entrada a Clientes. Se ajustaron `AreaAccessTests`, `AdminSeederTests` y `ExternalLoginTests` (inicio de sesión ⇒ `/admin/clientes`). Visual Studio tenía el sitio corriendo y bloqueaba `bin/Debug`, así que se compiló y probó desde una carpeta aparte:
  - Admin + Identity: 149 correctas;
  - integración completa: 809 correctas y 1 falla, `EmailLayoutTests.El_logo_existe_en_wwwroot`, que busca el repositorio subiendo desde su carpeta de salida (falla solo fuera de `bin/`; pasaba en las corridas anteriores);
  - `dotnet build -c Release`: sin advertencias.
- **Navegador** (Playwright, puerto 5299 con un admin temporal que se borró al terminar): el inicio de sesión y `/admin` llevan a Clientes, recargar usa la caché, «Actualizar» la renueva, «Tablero» activo, sin scroll horizontal a 375, 768 y 1280, y consola sin errores.

## Pendientes
- Correr la suite completa desde `bin/` cuando Visual Studio no tenga el sitio abierto, para confirmar `EmailLayoutTests` en su lugar.
- La caché es por proceso: con varias instancias del App Service, cada una calcula el suyo (a lo más una vez cada 5 min por instancia).
- 6.3: alerta y lista de cargos `Partial` y de links pagados sin abonar; el tablero todavía no los cuenta.
- El barrido nocturno con `GET /transaction/bank-account/{id}` (2.9) sigue pospuesto. El reproceso de la bitácora no lo sustituye: solo reprocesa lo que sí llegó.
- La búsqueda dentro del payload no tiene índice. Con muchos eventos puede ser lenta; si pasa, guardar los ids de Pey en columnas propias de `WebhookEvents`.
