# Fase 5.7 — Recordatorios, gracia y baja al gratuito

**Estado:** ✅ hecho 2026-11-21 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas)

## Alcance (del plan)
Job diario `suscripciones-diario` a las 9:00 de México ([diseño §4 «Recordatorios» y §10 5.7](../actualizaciones/suscripciones_2026_11_18.md#57-recordatorios-gracia-y-baja-al-gratuito)):
- links de renovación y recordatorios: 15 días antes en el anual, 3 días antes, el día del vencimiento y el día 3 de la gracia;
- aviso del fin de la prueba y de la baja al gratuito;
- el día 1, el resumen mensual (su contenido llega en la 5.10).

Además, el banner de gracia con el link y la fecha límite. La baja es un cálculo: el job solo avisa.

**Demostrable:**
- un vendedor cuyo mensual vence en 3 días recibe el correo «Tu plan Pro vence en 3 días»;
- en «Mi plan» ve «Tu link de renovación» con «Pagar ahora»;
- al vencer, el portal muestra el banner «Tu plan Pro venció el … Paga a más tardar el … para no perder Pro», con el mismo link;
- al terminar la gracia recibe «Pasaste al plan gratuito».

## Decisiones
- **Confirmadas por el usuario (2026-11-21):**
  - La 5.4 aún no existía. La 5.7 construye **solo su base**:
    - tabla `Notifications` con `DedupKey` único por vendedor;
    - `INotificationService.NotifyAsync`, con correo opcional.

    La campana, `/app/avisos`, el banner genérico y los avisos de 80/100 y de pago abonado se quedan para la 5.4. Los avisos que guarda el job ya quedan en la tabla y la 5.4 los mostrará.
  - El botón de los correos lleva a **«Mi plan»**, donde está el link de renovación. El correo nunca trae la liga de pago de Pey.
- **Decisiones de implementación:**
  - **Ventanas, no días exactos** (`Domain/Subscriptions/SubscriptionReminders`): si el job no corre un día, el aviso sale al siguiente con los días reales («te quedan 2 días»), y pasada la ventana no se manda nada atrasado. Las ventanas no se enciman: a lo más un aviso por día.

    | Etapa | Ventana (L = último día con Pro, G = último de la gracia) |
    |---|---|
    | `AnnualUpcoming` | anual pagado, de L−15 a L−4 |
    | `Upcoming` | de L−3 a L−1; en la prueba y en los días regalados, hasta L, porque no tienen aviso del día |
    | `DueToday` | L, solo con pago. Si venció hoy antes de las 9:00 ya está en la gracia, pero el aviso es este |
    | `Grace` | de L+3 a G |
    | `Downgraded` | del día de la baja a 2 días después |
  - **`DedupKey` = `sub:{etapa}:{ProUntil en UTC}`**: un pago mueve `ProUntil` y empieza otro ciclo de avisos. Por eso, quien pagó durante la prueba no recibe «tu prueba termina», porque su último periodo ya es el pagado (lo que prometió la 5.6).
  - **Link de renovación**:
    - con el paquete del último pago (`LastPackage`), si existe, y nunca en la baja: el link viejo, que puede traer el anual congelado, ya no se ofrece;
    - el job lo asegura **cada día de la ventana** con `ISubscriptionCheckout.StartAsync(..., Origin: Renewal)`. Reutiliza el pendiente del mismo paquete y monto, aunque lo haya creado el vendedor: si Pey falla un día, al siguiente se reintenta sin repetir el aviso;
    - para los días regalados después de un pago también se crea, con el paquete de ese pago;
    - sin base HTTPS (local sin túnel), no hay link y el aviso sale igual.
  - `StartCheckoutCommand` ganó `Origin` (por defecto `Merchant`). Nada más cambió en el checkout.
  - **Correo**: a lo más uno por aviso. Se manda después de guardar el aviso; si falla, queda `EmailSentAt` nulo y no se reintenta, como el correo de cuenta activa. Un error de un vendedor no detiene a los demás (un scope por vendedor).
  - **Resumen mensual**: `IMonthlySummaryNotifier`. Hoy es `LoggingMonthlySummaryNotifier` (solo log, `TryAdd`) y la 5.10 lo reemplaza; debe ser idempotente por mes.
  - **Prefiltro del job**: `ProUntil` entre `now − (GraceDays + 4) días` y `now + 17 días`, con el índice nuevo `IX_Subscriptions_ProUntil`. No se revisan vendedores dados de baja. Un suspendido sí recibe recordatorios, porque puede pagar (5.6).
  - **Banner de gracia** (`GraceBannerViewComponent` en `_MerchantLayout`):
    - solo en `Grace`, y no en «Mi plan», porque la tarjeta ya lo dice;
    - «Pagar ahora» va al link pendiente del paquete del último pago (`RenewalLinkQuery`); sin link, «Ver mi plan»;
    - mismo ancho que el contenido (`col-md-10 col-lg-8`).
  - **«Mi plan»**: tarjeta con el link pendiente del paquete del último pago, mientras el estado es prueba, Pro o gracia. Dice «Tu link de renovación» si lo creó el job y «Tienes un pago pendiente» si lo generó el vendedor. En el gratuito se oculta (§4: los links se muestran hasta que termina la gracia; se siguen abonando si alguien los paga).

## Qué se hizo
- **Dominio**:
  - `Notifications/Notification` + `NotificationType`;
  - `Subscriptions/SubscriptionReminders`, `SubscriptionReminder` y `SubscriptionReminderStage`.
- **Application**:
  - `Notifications/INotificationService` (`NotificationRequest`, `NotificationEmail`, `NotificationResult`);
  - `Subscriptions/IMonthlySummaryNotifier`;
  - `StartCheckoutCommand.Origin`.
- **Infrastructure**:
  - `Notifications/NotificationService`;
  - `Jobs/SubscriptionDailyJob` (`suscripciones-diario`);
  - `Subscriptions/LoggingMonthlySummaryNotifier`;
  - `MerchantEmails.SubscriptionReminder` (textos del aviso y del correo de cada etapa);
  - migración **`Notifications`** (no destructiva): tabla con índice único `(MerchantId, DedupKey)`, índice `(MerchantId, CreatedAt)`, FK a `Merchants`, filtro multi-tenant, e `IX_Subscriptions_ProUntil`.
- **Web**:
  - `JobsSetup` programa el job;
  - `Areas/Merchant/ViewComponents/GraceBannerViewComponent` + `Views/Shared/Components/GraceBanner/Default.cshtml` (`GraceBannerViewModel`);
  - `Areas/Merchant/Services/RenewalLinkQuery`;
  - `MyPlanViewModel.RenewalLink` (`RenewalLinkViewModel`) y su tarjeta en `Plan/Index`.

## Pruebas
- **Unitarias** (`SubscriptionRemindersTests`, 28): onboarding; mensual a 15, 4, 3, 2 y 1 días; día del vencimiento (también vencido antes de las 9:00); días 1 a 5 de la gracia; baja y su ventana; anual a 16, 15, 4 y 3 días; prueba (3 días, último día y baja sin gracia); prueba con un pago por delante; días regalados; medianoche de México contra UTC; llaves por etapa y por ciclo.
- **Integración**:
  - `SubscriptionDailyJobTests` (10), con el reloj congelado (`Local133WebFactory.Clock`, `TestClock.FreezeAtMexicoNine`):
    - anual a 15 días: link `Renewal` con el precio congelado, aviso y correo a «Mi plan», sin repetir nada en la segunda corrida;
    - mensual a lo largo del ciclo con un solo link;
    - fuera de las ventanas;
    - prueba y baja;
    - prueba con un pago por delante;
    - Pey falla y se reintenta al día siguiente sin repetir el aviso;
    - sin HTTPS;
    - dados de baja y onboarding;
    - la falla de un vendedor no detiene a otro;
    - resumen del día 1.
  - `NotificationServiceTests` (5): idempotencia, misma llave en dos vendedores, falla del correo, aviso sin correo y aislamiento multi-tenant.
  - `GraceBannerTests` (4): banner con el link en Inicio, Cobrar y Ventas, pero no en «Mi plan»; sin link, a «Mi plan»; Pro con pago pendiente; en el gratuito, ni banner ni link viejo.
- **Resultados**:
  - `dotnet test -c Release`: **1074 en verde** (455 unitarias, 12 de la portada y 607 de integración);
  - `dotnet build -c Release` sin advertencias;
  - `dotnet ef migrations has-pending-model-changes`: sin cambios.

## Revisión en navegador (Playwright, Edge)
- **Arranque**: sitio en `http://localhost:5231` (Debug) con `Jobs__Enabled=false`, `Pey__ApiKey=` vacío, `Pey__BaseUrl` a un puerto cerrado y `Resend__ApiKey=` vacío. **No se llamó a Pey.** Al arrancar aplicó la migración `Notifications` en la base de desarrollo.
- **Datos**: vendedor `gracia57@prueba.mx`, registrado por la UI. Por SQL se le pusieron el correo confirmado, estatus activo, cuenta bancaria, un mes pagado que venció el 20-nov y un cargo `Renewal` pendiente con el link falso `01REVISION57`.
- **Pantallas**: Inicio, Cobrar, Ventas y «Mi plan» a 375, 768 y 1280.
  - Banner en las tres primeras con «Pagar ahora» al link; en «Mi plan», sin banner y con la tarjeta «Tu link de renovación».
  - Sin scroll horizontal y pestaña activa correcta.
  - A 375 el banner queda arriba y la barra inferior no tapa nada (`main` con 76 px abajo).
  - Consola sin errores ni advertencias; recursos con 200/304.
- **Correos**: el HTML de las 6 etapas, generado con `MerchantEmails` y servido en local, se revisó a 375 y 1280: logo, título, párrafos, botón a «Mi plan» y liga de respaldo.
- **Corregido durante la revisión**: a 1280 el banner ocupaba todo el contenedor y quedaba más ancho que el contenido. Ahora usa el ancho de las pantallas (`col-md-10 col-lg-8`).
- **Capturas** en `.playwright-mcp/57-*.png`.
- **Datos que quedaron** en la base de desarrollo: el vendedor `gracia57@prueba.mx` en gracia hasta el 25-nov y luego en el gratuito. El cargo con link falso se borró para que `conciliar-links` no consulte a Pey.

## Pendientes
- **Job real en Hangfire a las 9:00** con la base de desarrollo (o en Azure) y un correo de recordatorio real en un buzón (Resend; requiere confirmación). El link de renovación real solo se crea con `App:PublicBaseUrl` HTTPS (túnel o producción) y llama a Pey `POST /payment-link/create`, que no cobra nada.
- **5.4**: campana, `/app/avisos` y banner genérico, que leerán `Notifications`; reemplazar `LoggingChargeLimitNotifier` y sumar el aviso del pago abonado con `INotificationService`.
- **5.10**: contenido del resumen mensual (reemplazar `LoggingMonthlySummaryNotifier`).
