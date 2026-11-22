# Fase 5.7 — Recordatorios, gracia y baja al gratuito

**Estado:** ✅ hecho 2026-11-21 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas)

## Alcance (del plan)
Job diario `suscripciones-diario` a las 9:00 de México ([diseño §4 «Recordatorios» y §10 5.7](../actualizaciones/suscripciones_2026_11_18.md#57-recordatorios-gracia-y-baja-al-gratuito)):
- recordatorios 15 días antes en el anual, 3 días antes, el día del vencimiento y el día 3 de la gracia;
- aviso del fin de la prueba y de la baja al gratuito;
- el día 1, el resumen mensual (su contenido llega en la 5.10).

Además, el banner de gracia con la fecha límite. La baja es un cálculo: el job solo avisa.

El diseño original decía que el job creaba los links de renovación; el usuario lo cambió el mismo día (ver «Ajuste»).

**Demostrable:**
- un vendedor cuyo mensual vence en 3 días recibe el correo «Tu plan Pro vence en 3 días», con el botón a «Mi plan»;
- en «Mi plan» ve el aviso «Tu plan Pro vence en 3 días… Elige tu paquete para renovar» y su paquete marcado como «Tu paquete». Al elegirlo se genera el link de Pey;
- al vencer, el portal muestra el banner «Tu plan Pro venció el … Paga a más tardar el … para no perder Pro», con «Renovar ahora» a «Mi plan»;
- al terminar la gracia recibe «Pasaste al plan gratuito».

## Decisiones
- **Confirmadas por el usuario (2026-11-21):**
  - La 5.4 aún no existía. La 5.7 construye **solo su base**:
    - tabla `Notifications` con `DedupKey` único por vendedor;
    - `INotificationService.NotifyAsync`, con correo opcional.

    La campana, `/app/avisos`, el banner genérico y los avisos de 80/100 y de pago abonado se quedan para la 5.4. Los avisos que guarda el job ya quedan en la tabla y la 5.4 los mostrará.
  - El botón de los correos lleva a **«Mi plan»**. El correo nunca trae la liga de pago de Pey.
  - **El job no crea links de Pey.** El vendedor recibe el correo, entra a «Mi plan», ve que toca renovar y elige el paquete; solo entonces se genera el link. Motivo: si no renueva, no se crea en vano un link de Pey, que puede tener costo.
  - **Un link que el vendedor genera y no paga no se muestra**, ni en «Mi plan» ni en el banner.
  - **Reutilización al dar «Pagar»**: un link `Pending` del mismo paquete y monto se reutiliza solo si se creó hace a lo más `GraceDays` días (`SystemSettings.GraceDays`, hoy 5). Si es más viejo, se crea otro.
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
  - **Aviso de renovar en «Mi plan»**: `SubscriptionReminders.RenewalNoticeFor` usa las mismas ventanas salvo la baja, porque la tarjeta del plan ya la dice. En la gracia cubre desde el día 1, aunque el correo salga desde el día 3. El aviso es la llamada a la acción («Renueva antes del … para no perder Pro»), con «Elegir paquete» a los paquetes. No repite lo que ya dicen la tarjeta del plan y la nota de los paquetes.
  - **Correo**: a lo más uno por aviso. Se manda después de guardar el aviso; si falla, queda `EmailSentAt` nulo y no se reintenta, como el correo de cuenta activa. Un error de un vendedor no detiene a los demás (un scope por vendedor).
  - **Resumen mensual**: `IMonthlySummaryNotifier`. Hoy es `LoggingMonthlySummaryNotifier` (solo log, `TryAdd`) y la 5.10 lo reemplaza; debe ser idempotente por mes.
  - **Prefiltro del job**: `ProUntil` entre `now − (GraceDays + 4) días` y `now + 17 días`, con el índice nuevo `IX_Subscriptions_ProUntil`. No se revisan vendedores dados de baja. Un suspendido sí recibe recordatorios, porque puede pagar (5.6).
  - **Banner de gracia** (`GraceBannerViewComponent` en `_MerchantLayout`):
    - solo en `Grace`, y no en «Mi plan», porque ahí está el aviso;
    - «Renovar ahora» siempre va a «Mi plan»;
    - mismo ancho que el contenido (`col-md-10 col-lg-8`).
  - `SubscriptionChargeOrigin.Renewal` queda reservado, sin uso; no se borró porque ya está en el esquema.

## Qué se hizo
- **Dominio**:
  - `Notifications/Notification` + `NotificationType`;
  - `Subscriptions/SubscriptionReminders` (`For`, `RenewalNoticeFor`), `SubscriptionReminder` y `SubscriptionReminderStage`;
  - `SubscriptionCharge.CanBeReusedFor(quote, now, maxAgeDays)` y `ReusableSince`.
- **Application**:
  - `Notifications/INotificationService` (`NotificationRequest`, `NotificationEmail`, `NotificationResult`);
  - `Subscriptions/IMonthlySummaryNotifier`.
- **Infrastructure**:
  - `Notifications/NotificationService`;
  - `Jobs/SubscriptionDailyJob` (`suscripciones-diario`): solo avisa y manda el resumen;
  - `Subscriptions/LoggingMonthlySummaryNotifier`;
  - `SubscriptionCheckout` reutiliza solo links con a lo más `GraceDays` días;
  - `MerchantEmails.SubscriptionReminder` (textos del aviso y del correo de cada etapa: «Entra a «Mi plan» y elige tu paquete para renovar»);
  - migración **`Notifications`** (no destructiva): tabla con índice único `(MerchantId, DedupKey)`, índice `(MerchantId, CreatedAt)`, FK a `Merchants`, filtro multi-tenant, e `IX_Subscriptions_ProUntil`.
- **Web**:
  - `JobsSetup` programa el job;
  - `Areas/Merchant/ViewComponents/GraceBannerViewComponent` + `Views/Shared/Components/GraceBanner/Default.cshtml` (`GraceBannerViewModel`);
  - `MyPlanViewModel.RenewalNotice` (`RenewalNoticeViewModel`, `data-renewal-notice`) y la insignia «Tu paquete» (`PlanOfferViewModel.IsCurrentPackage`, `data-plan-offer-current`) en `Plan/Index`.

## Pruebas
- **Unitarias**:
  - `SubscriptionRemindersTests` (33): onboarding; mensual a 15, 4, 3, 2 y 1 días; día del vencimiento (también vencido antes de las 9:00); días 1 a 5 de la gracia; baja y su ventana; anual a 16, 15, 4 y 3 días; prueba (3 días, último día y baja sin gracia); prueba con un pago por delante; días regalados; medianoche de México contra UTC; llaves por etapa y por ciclo. Del aviso de «Mi plan»: toda la gracia, las ventanas antes de vencer y nada tras la baja;
  - `SubscriptionChargeTests`: reutiliza con 5 días de edad, no con 5 días y 1 minuto, ni sin días de gracia.
- **Integración**:
  - `SubscriptionDailyJobTests` (8), con el reloj congelado (`Local133WebFactory.Clock`, `TestClock.FreezeAtMexicoNine`); en todas el job no llama a Pey ni crea cargos:
    - anual a 15 días: aviso y correo a «Mi plan», sin repetir nada en la segunda corrida;
    - mensual a lo largo del ciclo;
    - fuera de las ventanas;
    - prueba y baja;
    - prueba con un pago por delante;
    - dados de baja y onboarding;
    - la falla de un vendedor (servicio de avisos que lanza para uno) no detiene a otro;
    - resumen del día 1.
  - `NotificationServiceTests` (5): idempotencia, misma llave en dos vendedores, falla del correo, aviso sin correo y aislamiento multi-tenant.
  - `GraceBannerTests` (4):
    - en la gracia, «Renovar ahora» a «Mi plan» en Inicio, Cobrar y Ventas, sin la URL del link pendiente;
    - en «Mi plan», aviso e insignia, sin banner ni «pago pendiente»;
    - a 2 días de vencer, aviso;
    - con Pro lejos de vencer y en el gratuito, nada.
  - `MyPlanTests` (+2): un link pendiente de hace 6 días no se reutiliza (se crea otro), y uno generado y sin pagar no aparece en «Mi plan».
- **Resultados**:
  - `dotnet test -c Release`: **1080 en verde** (461 unitarias, 12 de la portada y 607 de integración);
  - `dotnet build -c Release` sin advertencias;
  - `dotnet ef migrations has-pending-model-changes`: sin cambios.

## Revisión en navegador (Playwright, Edge)
- **Arranque**: sitio en `http://localhost:5231` (Debug) con `Jobs__Enabled=false`, `Pey__ApiKey=` vacío, `Pey__BaseUrl` a un puerto cerrado y `Resend__ApiKey=` vacío. **No se llamó a Pey.** Al arrancar aplicó la migración `Notifications` en la base de desarrollo.
- **Datos**: vendedor `gracia57@prueba.mx`, registrado por la UI. Por SQL se le pusieron el correo confirmado, estatus activo, cuenta bancaria y un mes pagado que venció el 20-nov. Para el ajuste, también un cargo `Pending` del vendedor con el link falso `01REVISION57B`, que se borró al terminar.
- **Pantallas**: Inicio, Cobrar, Ventas y «Mi plan» a 375, 768 y 1280.
  - Banner en las tres primeras con «Renovar ahora» a «Mi plan» y sin el link pendiente.
  - En «Mi plan», sin banner, con el aviso «Renueva antes del 25 de noviembre para no perder Pro» e insignia «Tu paquete» en 1 mes. Ningún «pago pendiente».
  - Sin scroll horizontal y pestaña activa correcta. A 375 la barra inferior no tapa nada.
  - Consola sin errores ni advertencias; recursos con 200/304.
- **Correos**: el HTML de las 6 etapas, generado con `MerchantEmails` y servido en local, se revisó a 375 y 1280: logo, título, párrafos, botón a «Mi plan» y liga de respaldo. Era la versión que mencionaba el link de renovación; la nueva solo cambia ese párrafo por «elige tu paquete», y lo cubre `SubscriptionDailyJobTests`.
- **Corregido durante la revisión**:
  - a 1280 el banner ocupaba todo el contenedor; ahora usa el ancho de las pantallas (`col-md-10 col-lg-8`);
  - el aviso de «Mi plan» repetía la tarjeta del plan («venció el … tienes hasta el …»); ahora es solo la llamada a renovar.
- **Capturas** en `.playwright-mcp/57-*.png` y `.playwright-mcp/57b-*.png` (ajuste).

## Ajuste (2026-11-21): el job ya no crea links
- **Problema**: en la base de desarrollo, «Mi plan» de un vendedor con Pro pagado hasta el 19-ene mostraba «Tienes un pago pendiente». Era el link de $149 de la prueba real de la 5.6, que nunca se pagó: se pagó otro de $0.01. La primera versión de la 5.7 mostraba cualquier link `Pending` del último paquete como renovación.
- **Primer intento**: filtrar por el último pago abonado. El usuario lo reemplazó con las tres decisiones de arriba: el job no crea links, no se muestran links pendientes y la reutilización tiene una edad máxima de `GraceDays`.
- **Quitado**: `RenewalLinkQuery`, `RenewalLinkViewModel` y su tarjeta, el link del banner, `StartCheckoutCommand.Origin`, `SubscriptionReminder.RenewalPackage`/`NeedsRenewalLink`, la creación del link en el job y el monto del link en los correos.
- El link de $149 de la base de desarrollo sigue `Pending` en Pey. Ya no se muestra; si alguien lo paga, se abona.

## Pendientes
- **Job real en Hangfire a las 9:00** con la base de desarrollo (o en Azure) y un correo de recordatorio real en un buzón (Resend; requiere confirmación).
- **5.4**: campana, `/app/avisos` y banner genérico, que leerán `Notifications`; reemplazar `LoggingChargeLimitNotifier` y sumar el aviso del pago abonado con `INotificationService`.
- **5.10**: contenido del resumen mensual (reemplazar `LoggingMonthlySummaryNotifier`).
