# Fase 5.5 — Límites del gratuito

**Estado:** ✅ hecho 2026-11-19 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas)

## Alcance (del plan)
Aplicar en el servidor los límites del plan gratuito ([diseño §5 y §10](../actualizaciones/suscripciones_2026_11_18.md#55-límites-del-gratuito)):
- catálogo de solo lectura, carrito bloqueado, recibo sin WhatsApp e historial de 30 días;
- 100 cobros concretados al mes, con contador visible y avisos al 80 % y al 100 % (los 5 cobros de cortesía del diseño original se quitaron el 2026-11-20; ver abajo);
- tope de 300 cobros generados.

**Demostrable:** un vendedor en el gratuito ve «82 de 100 cobros este mes» en Inicio, en Cobrar y en «Mi plan». Con 100 concretados ya no genera cobros (ni QR ni celular), y el carrito, la edición del catálogo, el historial viejo y WhatsApp muestran el aviso «Disponible con Pro».

## Decisiones
- **La 5.4 no existe todavía** (confirmado por el usuario el 2026-11-19). Los avisos de 80 y 100 tienen dos piezas:
  - `IChargeLimitAlerts` es el detector: `PaymentRecorder` lo llama después de guardar el pago;
  - `IChargeLimitNotifier` es el destino. En la 5.5 es `LoggingChargeLimitNotifier`, que solo escribe en el log; la 5.4 lo reemplaza por el aviso en el sitio y el correo, con `DedupKey` `charge-limit-{umbral}-{AAAA-MM}`.

  El estado del límite en pantalla (contador, aviso, bloqueo) se calcula en cada petición y no necesita guardarse.
- **`/app/plan` provisional** (confirmado por el usuario): tarjeta del plan, contador y tabla de lo que incluye cada plan, con `_ComingSoon` «Pagar Pro, Fase 5.6». Todos los botones «Conoce Pro» llevan ahí. La 5.6 lo reemplaza.
- Valores por defecto del diseño (§11):
  - recibo del gratuito: solo «Copiar liga» y «Ver recibo», con la liga visible en un campo de solo lectura (sirve aunque el navegador no permita el portapapeles);
  - historial del gratuito: conserva el filtro de estatus; el rango de fechas es de Pro, así que `desde`/`hasta` se ignoran.
- **Cobros concretados** = ventas con `Sales.PaidAt` en el mes calendario de México (`MexicoTime.MonthRange`). Un pago adicional a una venta ya pagada no cuenta otra vez. **Generados** = ventas con `CreatedAt` en el mes, se paguen o no; las que se borran porque Pey falló no cuentan.
- Bloqueo con `paid ≥ límite` (100) o `generated ≥ 300`. Si las dos condiciones se cumplen, manda el límite de concretados. Se revisa antes de insertar la venta y de llamar a Pey. Dos cobros simultáneos en el borde pueden pasar los dos; se acepta, porque el tope es un freno y no un saldo.
- **Historial del gratuito:** últimos 30 días contando hoy, desde la medianoche de México de `hoy − 29` (`MerchantPlan.HistoryStartUtc`). La pantalla del QR de una venta más vieja redirige a su detalle, que responde 200 con el aviso de Pro (no 404).
- El aviso se dispara solo cuando la venta pasa a `Paid` y su `PaidAt` cae en el mes actual. Un pago tardío del mes anterior no avisa.
- En Pro no hay contador. Al pasar el uso justo (5,000 al mes) se escribe una advertencia en el log, sin bloquear; la alerta en el admin queda para la Fase 6.
- `ComingSoonViewModel.Phase` pasó de `int` a `string` para poder decir «5.6».

## Qué se hizo
- **Dominio**:
  - `MexicoTime.MonthStart`, `NextMonthStart` y `MonthRange`;
  - `Subscriptions/MonthlyChargeAllowance`, con `ChargeAllowanceState` (`Normal`, `NearLimit`, `Courtesy`, `Exhausted`, `GenerationCapReached`), `HardLimit`, `WarningThreshold` (80 % redondeado hacia arriba), `CourtesyLeft`, `CanGenerate`, `ThresholdsCrossed(prev, curr)` y `ProFairUseCrossed`.
- **Application**:
  - `Subscriptions/ChargeUsage.cs`: `IChargeUsage`, `MonthlyChargeUsage`, `IChargeLimitAlerts` e `IChargeLimitNotifier`;
  - `MerchantPlan` suma `FreeHistoryDays` y `HistoryStartUtc(now)`;
  - `CreateSaleResult.PlanRequired`, `MonthlyLimitReached(ResetsOn)` y `GenerationLimitReached(ResetsOn)`;
  - `SaveProductResult.PlanRequired` y `DeleteProductResult.PlanRequired`, con `ProductMessages.PlanRequired`.
- **Infrastructure**:
  - `Subscriptions/ChargeUsage`: dos conteos por `MerchantId` sin filtro;
  - `Subscriptions/ChargeLimitAlerts` y `LoggingChargeLimitNotifier`;
  - `SaleCharging.PlanBlockAsync`: el carrito sin `CanChargeWithCart` ⇒ `PlanRequired`, y en el gratuito el límite y el tope;
  - `ProductCatalog` revisa `CanEditCatalog` después de `CanManageProducts` (el estatus manda);
  - `PaymentRecorder` llama al detector cuando la venta pasa a pagada, dentro de un try/catch;
  - migración **`SalesPaidAtIndex`**: índice `(MerchantId, PaidAt)`.
- **Web** (área Merchant):
  - parciales `_ProRequired` (insignia «Disponible con Pro» + «Conoce Pro»), `_ComingSoonBadge` y `_ChargeUsage` (contador con barra, aviso al 80 % y bloqueo); `PlanViewModels.cs` (`PlanUrls`, `ProRequiredViewModel`, `ChargeUsageViewModel`, `PlanBlockedNotices`, `MyPlanViewModel`);
  - **Cobrar**: contador arriba del formulario; aviso de bloqueo con liga a `/app/plan`. El carrito del gratuito muestra `_ProRequired` con «Tu catálogo de N productos te espera» y «Cobrar por monto», y su fragmento de catálogo responde 403. Los tres resultados nuevos regresan al formulario, que pinta el aviso;
  - **Inicio**: la tarjeta del plan lleva a `/app/plan` y el contador va debajo (solo vendedores activos con límite);
  - **Productos**: `ProductListViewModel.ReadOnlyReason` (`Disabled` | `Plan`). Formularios y POST del gratuito regresan a la lista con el aviso. `ProductListQuery.BuildAsync` recibe `canManage`;
  - **Ventas**: `SalesHistoryQuery` recorta a 30 días e ignora las fechas, y `HiddenOlderCount` alimenta «Tienes N ventas de hace más de 30 días» + `_ProRequired`. El filtro de fechas se sustituye por «Últimos 30 días… con Pro». Detalle viejo ⇒ `DetailLocked`. `/whatsapp` del gratuito regresa al detalle con aviso;
  - **Recibo**: `ReceiptLinks.ShareFor(..., canShare)` y `ReceiptShare.CanShare`. `_ReceiptShare` limitado (`data-receipt-share-limited`), y `receipt-share.js` selecciona la liga al enfocar el campo;
  - **`/app/plan`** (`PlanController`, `Views/Plan/Index`), con la tabla de funciones armada con los números de `SystemSettings`;
  - **«Más»**: «Mi plan» y «QR de tu puesto» con la insignia «Próximamente».

## Pruebas
- **Unitarias**:
  - `MonthlyChargeAllowanceTests`: 0/79/80/99/100/104/105/130 concretados; 299/300 generados; el agotamiento manda sobre el tope; ajustes personalizados; redondeo del aviso; umbrales cruzados (uno, varios, ninguno; sin cortesía); uso justo; negativos;
  - `MexicoTimeTests`: `MonthRange` de noviembre y de diciembre a enero, y 30/11 23:59 contra 01/12 00:00 en México.
- **Integración** (`Subscriptions/FreePlanLimitsTests`; el helper `SeedPlanAsync` pasó de `MerchantHomeTests` a `WebTestBase.Plans.cs`, con `SeedFreePlanAsync` y `SeedTrialAsync`):
  - con 104 concretados cobra con cortesía; con 105 no genera QR ni push y no llama a Pey; con 300 generados tampoco;
  - borde de mes: los del mes anterior no cuentan y los del día 1 sí;
  - aislamiento entre vendedores;
  - en la prueba no hay límite ni contador;
  - contador en Inicio y en Cobrar;
  - carrito del gratuito: aviso, 403 del catálogo y POST sin venta ni Pey. Una venta con carrito anterior se sigue pagando por webhook;
  - historial: 30 días, conteo de las ocultas, fechas ignoradas; detalle viejo en 200 con aviso y QR viejo que redirige; en la prueba el historial es completo;
  - recibo: el gratuito solo copia la liga, `/whatsapp` regresa al detalle y `/recibo/{token}` sigue en 200; en la prueba va por WhatsApp;
  - umbrales: el pago 80 avisa del 80; un pago adicional no repite; si el aviso falla, el pago queda guardado; en la prueba no avisa;
  - «Mi plan» y «Más»; acceso solo con rol `Merchant`.
- `ProductCrudTests`: el gratuito deja el catálogo en solo lectura (GET y POST de alta, edición y baja); en la prueba sí se edita.
- **Resultados**:
  - `dotnet build` (Debug y Release) sin advertencias;
  - `dotnet ef migrations has-pending-model-changes`: sin cambios;
  - `dotnet test`: 390 unitarias, 12 de la portada y 559 de integración, todas en verde.

## Revisión en navegador
Playwright (Edge), con el sitio en `http://localhost:5231` (`Jobs__Enabled=false`, `Resend__ApiKey=` vacío: sin Pey ni correo real). El vendedor `plan55@prueba.mx` (Merchant 33) se registró por la UI y se confirmó por SQL. Por SQL también se dejó activo, con cuenta bancaria activa falsa (`FAKE-PLAN55`), en el gratuito (prueba terminada hace 10 días), con 3 productos, 3 ventas de hace 45 días y 82 pagadas en el mes.
- **375, 768 y 1280 px**, en Inicio, Cobrar (monto y productos), Productos, Ventas, detalle viejo, detalle con recibo, «Mi plan» y «Más»: sin scroll horizontal, pestaña activa correcta, consola sin errores ni advertencias y ningún recurso con 4xx.
- Estados del contador: 82 (aviso al 80 %), 101 («Te quedan 4 cobros de cortesía») y 105 (formulario sustituido por «Llegaste a tus cobros del mes… el 1 de diciembre», en los tres tamaños).
- `/app/productos/nuevo` regresa a la lista con el aviso de solo lectura. «Copiar liga» copia (muestra «Liga copiada.»). `/app/ventas/{id}/whatsapp` regresa al detalle con el aviso.
- **Corregido** en la revisión: el contador de «Mi plan» llevaba una liga a la misma página (ahora se oculta ahí), y la tabla de «Mi plan» quedaba pegada al borde de la tarjeta (`ps-3`/`pe-3`).
- Capturas en `.playwright-mcp/fase55-*.png` (fuera de git).

Notas:
- En la base de desarrollo quedan el usuario `plan55@prueba.mx` (Merchant 33, `Active`, gratuito, con 105 cobros concretados en noviembre) y su cuenta bancaria falsa con la CLABE de prueba. La 5.2 (CLABE única) va a pedir depurarla.
- Al arrancar, el sitio aplicó la migración `SalesPaidAtIndex` en la base local `Local133`.

## Pendientes
- Avisos en el sitio y correos de 80 y 100 cobros: la 5.4 reemplaza `LoggingChargeLimitNotifier`.
- Alerta en el admin cuando un vendedor Pro pasa el uso justo (Fase 6). Hoy solo va al log.
- «Mi plan» con paquetes y pago (5.6).
- «Lo que te pierdes» completo y recibo con «Cobrado con Local133» (5.10).

## Cambio 2026-11-20 — Sin cobros de cortesía
A pedido del usuario se quitaron los 5 cobros de cortesía: el gratuito deja de generar cobros al llegar a `FreeMonthlyPaidLimit` (100) concretados en el mes.
- **Dominio**:
  - `SystemSettings` pierde `FreeCourtesyCharges`.
  - `MonthlyChargeAllowance` pierde `CourtesyCharges`, `HardLimit` y `CourtesyLeft`, y gana `Remaining`.
  - `ChargeAllowanceState` pierde `Courtesy`: `Exhausted` = llegó al límite.
  - `ThresholdsCrossed` devuelve solo el aviso (80 %) y el límite.
- **Migración `RemoveFreeCourtesyCharges`**: quita la columna y vuelve a crear `CK_SystemSettings_Limits` sin ella.
- **Textos**:
  - contador: «Te quedan N cobros este mes» o «Te queda 1 cobro» desde el 80 %, y al llegar a 100, «Llegaste al límite del mes. No puedes generar cobros hasta el 1 de diciembre o hasta que pagues Pro.»;
  - bloqueo: «Usaste los 100 cobros del mes del plan gratuito…»;
  - la tabla de «Mi plan» dice solo «100».
- **Pruebas**: unitarias de 99/100 (bloquea) y de los dos umbrales. En integración, con 99 se cobra, con 100 se bloquea sin llamar a Pey, el pago 100 avisa del límite y ningún texto dice «cortesía». `dotnet test`: 387 unitarias, 12 de la portada y 560 de integración, todas en verde.
- Las pruebas y la revisión de arriba (del 2026-11-19: 101 con «Te quedan 4 cobros de cortesía» y bloqueo en 105) describen el comportamiento anterior. La revisión en navegador del cambio está abajo.
- **Revisión en navegador** (Playwright, Edge, 375, 768 y 1280 px; sitio sin Pey ni correo real). Al arrancar se aplicó `RemoveFreeCourtesyCharges` en la base local, y la columna ya no existe.
  - Con `plan55@prueba.mx` en 100 concretados, Inicio, Cobrar y «Mi plan» muestran «100 de 100 cobros este mes · Llegaste al límite del mes…». Cobrar cambia el formulario por «Usaste los 100 cobros del mes del plan gratuito…», y la tabla de «Mi plan» dice «100».
  - Con 99, Cobrar dice «Te queda 1 cobro este mes» y deja cobrar.
  - En ninguna pantalla aparece «cortesía». Sin scroll horizontal, sin errores de consola y sin 4xx.
  - Capturas en `.playwright-mcp/fase55-sincortesia-*.png`.
  - En la base de desarrollo, `plan55@prueba.mx` quedó con 99 cobros concretados en noviembre.
