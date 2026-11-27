# Fase 5.16 — Recordatorios, referidos y avisos

**Estado:** ✅ hecho 2026-11-27, sin publicar: sale junto con la 5.12 a la 5.15 y la 5.17 a la 5.18 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [diseño](../actualizaciones/planes_basico_pro_2026_11_26.md#516-recordatorios-referidos-y-avisos)

## Alcance (del plan)
- Etapas del vencido (`Expired`) y su seguimiento del día 7 (`ExpiredFollowUp`), con los botones al checkout.
- Premio de referidos con el plan de la cadena de quien invita, y descuento de bienvenida por plan.
- Resumen mensual por plan y anuncio del cambio de planes (`PlanChangeAnnouncement`).

**Demostrable:**
- Un vendedor con Básico a 3 días de vencer recibe «Tu plan Básico vence en 3 días». Su botón lleva a `/app/plan/checkout/basico/1-mes`, nunca a Pey.
- Un vendedor vencido recibe «Tu plan venció: ya no puedes cobrar» y, el día 7, «Tus clientes te siguen buscando».
- Quien invita con Básico gana «10 días de Básico».
- Al publicar, los vendedores existentes reciben una sola vez «Nuevos planes: Básico y Pro», con su transición.

## Decisiones (propias)
- **Seguimiento del vencido**: es una ventana de los días 7 a 9 desde el vencimiento, como las demás etapas (si el job no corre el 7, sale el 8 o el 9). Sale una sola vez por la llave `sub:ExpiredFollowUp:{ProUntil}`; después no hay más recordatorios. El «vencimiento» es el fin de la gracia si lo último fue pagado; si no, el fin del periodo.
- **Renombres de `NotificationType`**, que se guarda como texto:
  - `DowngradedToFree` → `PlanExpired`;
  - `ProDaysEnding` → `PlanDaysEnding` (ahora también cubre los días de Básico y la transición);
  - tipos nuevos: `PlanExpiredFollowUp` y `PlanChangeAnnouncement`.

  La migración `PlanChangeAnnouncement` actualiza los renglones que ya existen.
- **Corte del anuncio**: la misma migración inserta un renglón global `AuditAction.PlanChange` en `AuditLogs` (actor `sistema`), con la hora del servidor. Así el corte queda registrado y no hace falta una columna ni un App Setting.
  - El job manda el anuncio a los vendedores con `CreatedAt` antes del corte y no dados de baja, durante 30 días (`PlanChangeAnnouncement.SendDays`).
  - En una base nueva, el renglón queda con la hora de creación y no hay vendedores anteriores: no se manda nada.
  - En las pruebas, Respawn borra `AuditLogs`, así que el anuncio solo sale si la prueba siembra el corte.
- **Contenido del anuncio** según la situación (`PlanChangeSituation.For`):
  - transición de Básico («te regalamos días de Básico hasta el …»);
  - prueba seguida de la transición;
  - plan vigente que sigue igual;
  - vencido;
  - onboarding («tu prueba de 15 días de Pro empieza con tu primer cobro»).

  En todos los casos van los precios de Básico y Pro (de fundador si lo es) y el botón «Ver los planes» al paso 1 del checkout.
- **Botones de los recordatorios**: `SubscriptionPaths.ChoosePlanFor(LastPlan, LastPackage)` (la lógica de `PlanUrls.ChoosePlanFor` pasó a Application para que la usen los correos). Llevan al resumen de su último pedido, a la sección del plan si fue de 3 o 6 meses, o al paso 1 si nunca pagó. El aviso guarda la misma ruta.
- **Textos por plan**: el recordatorio lleva `EndingPlan` (`Subscription.ChainEndPlan`, el plan que vence al final de la cadena). «Pro hasta el 9 de diciembre y después Básico» avisa cuando vence Básico. Los días regalados y la transición tienen su propio nombre («Tus días de Básico de transición terminan…»). La prueba, el vencido y el seguimiento mencionan «Básico cuesta $99 al mes y Pro $199 al mes».
- **Resumen mensual del vencido**: «Tu plan venció: con Básico pagarías $99 al mes…», con «Elegir mi plan» a `/app/plan/checkout#basico`, porque el texto habla de Básico. Con plan vigente: «Tu plan Básico/Pro cuesta $X al mes» (la prueba y el onboarding cuentan como Pro).
- **Premio de referidos**:
  - el plan ya se elegía con `ChainEndPlan` desde la 5.12. Ahora el correo lo dice («¡Ganaste 10 días de Básico!»);
  - si el plan de quien invita estaba vencido, el correo dice «Ya puedes volver a cobrar: tienes {plan} hasta el …»;
  - el pendiente (antes de la prueba) sigue siendo de Pro.
- **El descuento de bienvenida por plan** ya estaba desde la 5.12 (`SubscriptionPricing.Quote`). Aquí solo se agregaron pruebas de integración.
- **«Invita y gana», «Más» y el registro** ya no dicen «días de Pro» ni «primer pago de Pro»: ahora dicen «días de tu plan, Básico o Pro» y «primer pago».
- **El regalo del admin** (`ProDaysGranted`, «Te regalamos 10 días de Pro») no se tocó: el selector de plan es de la 5.17.

## Qué se hizo
- **Dominio**:
  - `SubscriptionReminders`:
    - etapas `Expired` y `ExpiredFollowUp`;
    - `ExpiredNoticeWindowDays`, `ExpiredFollowUpDay`, `ExpiredFollowUpWindowDays` y `LastExpiredNoticeDay`;
    - `For` y `RenewalNoticeFor` con `endingPlan`;
    - el record con `EndingPlan` y `LastActiveDay` (antes `LastProDay`);
  - `NotificationType` con los renombres y los tipos nuevos, y `AuditAction.PlanChange`;
  - `MonthlySummary.For(…, currentPlan, isExpired)`, con `Plan`, `PlanMonthlyPrice` e `IsExpired`;
  - `PlanChangeAnnouncement` (`DedupKey`, `SendDays`, `IsDue`, `Applies`) y `PlanChangeSituation`.
- **Application**: `SubscriptionPaths.ChoosePlanFor(plan, paquete)`.
- **Infraestructura**:
  - `MerchantEmails`:
    - `SubscriptionReminder(…, isFounder, actionPath)` por plan;
    - `ReferralReward(…, plan, reactivated, …)`;
    - `MonthlySummary` por plan;
    - `PlanChangeAnnouncement` nuevo;
  - `SubscriptionDailyJob`:
    - el prefiltro llega hasta el día 9 después de la gracia;
    - plan y ruta del checkout en los recordatorios;
    - `SendPlanChangeAnnouncementAsync`;
    - `SubscriptionDailyRunResult.AnnouncementsSent`;
  - `ReferralRewarder`: `ReferralQualification.Plan` y `Reactivated`;
  - `MonthlySummaryNotifier` por plan;
  - migración `PlanChangeAnnouncement`, solo con datos.
- **Web**:
  - `PlanController` pasa el plan del final de la cadena al aviso de renovar;
  - `PlanUrls.ChoosePlanFor` delega en Application;
  - textos de «Invita y gana», «Más» y `_ReferralCodeField`;
  - etiqueta «Cambio de planes» en la bitácora del admin.

## Pruebas
- **Unitarias** (786, en verde):
  - `SubscriptionRemindersTests`:
    - vencido en los días 0 a 2 y nada del 3 al 6;
    - seguimiento del día 7 al 9 y nada el 10;
    - lo mismo después de la prueba, de un premio, de una conversión y de la transición (sin gracia);
    - `EndingPlan` de Básico y de Pro;
    - Pro seguido de Básico avisa cuando vence Básico;
    - el aviso de «Mi plan» no sale con el plan vencido;
  - `MonthlySummaryTests`: Básico, Pro, fundador de cada uno, el vencido con Básico y el onboarding con Pro;
  - `PlanChangeAnnouncementTests`: los bordes de `IsDue` y `Applies`, y cada situación.
- **Integración** (939, en verde):
  - `SubscriptionDailyJobTests`:
    - ruta del checkout en el aviso y en el correo (resumen del último pedido, o el paso 1 si nunca pagó);
    - Básico dice «Básico»;
    - el vencido y su seguimiento una sola vez, y el seguimiento al día siguiente si el job no corrió el 7;
    - el nombre de los días de transición;
    - el anuncio: una vez por vendedor, no a los registrados después ni a los dados de baja, nada sin el corte ni pasados 30 días, y el texto de la transición y del onboarding;
  - `ReferralTests`:
    - descuento de Básico ($20.70 de fundador) y de Pro Anual ($44.70);
    - premio de Pro en la prueba;
    - premio de Básico encadenado al final de su Básico;
    - premio de Pro con Pro pagado;
    - vencido que vuelve a cobrar, con el texto de reactivado;
  - `MonthlySummaryNotifierTests`: el vencido con Básico y el botón a `#basico`; Básico vigente.
- **Portada**: 61, en verde.
- `dotnet build` sin advertencias. `has-pending-model-changes`: «No changes have been made to the model since the last migration».

## Revisión en navegador
- **Montaje**: Playwright a 375, 768 y 1280 px. El sitio corrió desde Bash con `Resend__ApiKey=`, `Pey__ApiKey=` y `Jobs__Enabled=false`, sin llamar a Pey. Al arrancar se aplicó la migración en la base local (renglón `PlanChange` 17).
- **Vendedor**: `plan514@prueba.mx` (Merchant 55, Básico).
- **Lo revisado**:
  - «Invita y gana» con los textos nuevos;
  - la tarjeta de «Más»;
  - el texto del código en Crear cuenta.
- **Comprobaciones**: en los tres tamaños, sin scroll horizontal, con la pestaña «Más» activa y nada tapado por la barra inferior. La consola quedó sin errores; el único error fue un `fetch` mío a `/app/avisos`.
- **Avisos en el sitio**: `/app/avisos` y la campana todavía no existen (son de la 5.4), así que los avisos nuevos solo se ven por correo. El contenido de los correos se comprobó en las pruebas de integración. Los dos avisos de ejemplo que se insertaron en la base local se borraron.
- Capturas en `.playwright-mcp/fase516-*.png` (fuera de git).

## Pendientes
- **Prueba real del anuncio**: se publica en el horario acordado y sale a las 9:00 del día siguiente. Con Resend configurado, son correos reales a todos los vendedores existentes.
- **Base local**: quedan el renglón `PlanChange` de `AuditLogs` (id 17) y los tipos renombrados. Si `Jobs__Enabled` se enciende en local con Resend, el anuncio saldría a los vendedores de prueba registrados antes de ese renglón.
- Lo demás está asignado a su subfase:
  - regalo del admin por plan (texto «días de Pro») (5.17);
  - Términos y portada (5.18).
