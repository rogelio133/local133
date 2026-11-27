# Fase 5.13 — Prueba al primer cobro

**Estado:** ✅ hecho 2026-11-27, sin publicar: sale junto con la 5.12 y la 5.14 a la 5.18 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [diseño](../actualizaciones/planes_basico_pro_2026_11_26.md#513-prueba-al-primer-cobro)

## Alcance (del plan)
- 15 días de Pro desde el primer pago, disparados por `PaymentRecorder`, con respaldo en el job diario;
- antes de la prueba no se publica el catálogo en línea.

**Demostrable:** un vendedor con la cuenta activa ve «Tu prueba de 15 días de Pro empieza con tu primer cobro»; al pagarse su primer cobro, Inicio muestra «Prueba: te quedan 15 días».

## Decisiones (propias)
- **`ITrialStarter` guarda en su propia transacción** (antes, `TrialStarter` agregaba al contexto y guardaba quien llamaba). Así `PaymentRecorder` lo llama después de guardar el pago y una falla no deshace nada.
- **Candado**: la misma consulta `UPDLOCK, HOLDLOCK` sobre la fila de `Subscriptions` que usan `SubscriptionCrediting.LockAsync` y `ReferralRewarder.QualifyAsync`, con una búsqueda previa sin transacción (camino rápido) y otra dentro. Además de evitar dos suscripciones, serializa con el premio de un referido que califica a la vez: antes, ese premio podía quedarse pendiente.
- **Dado de baja ⇒ sin prueba.** Un vendedor suspendido sí la recibe si le pagan un cobro generado antes de suspenderlo. Los premios pendientes se deciden con el estatus real (antes iba `Active` fijo).
- **`PaidAt` en el futuro** (reloj de Pey adelantado) ⇒ la prueba empieza ahora.
- **Orden en `PaymentRecorder`**: guardar → prueba → aviso SignalR → límites del mes. Así Inicio ya muestra la prueba si el vendedor recarga al ver el pago, y los límites ven el plan nuevo.
- **Respaldo**: solo vendedores `Active` (como dice el diseño), con el `MIN(Payments.PaidAt)`, antes de los recordatorios. Tope `MaxMerchantsPerRun`.
- **Catálogo en línea en el onboarding**: el mismo `_ProRequired`, con el texto «Se activa con tu prueba…», y `PublicCatalogMessages.TrialRequired` si intenta publicar. `PublicCatalogSettings` no cambió: ya respondía `PlanRequired` porque la 5.12 dejó `CanPublishCatalog` en falso en el onboarding.
- **«Invita y gana»**: `IsOnboarding` pasa a ser «sin suscripción» (antes, por el estatus de la cuenta bancaria), porque un vendedor activo que no ha cobrado tampoco recibe aún sus premios.
- **Portada**: sus textos («…la espera de Banxico no te quita días») se quedan para la 5.18, junto con el resto de la portada y sus pruebas (`PublishedTrialDays = 30`).
- **Pruebas**: `SeedPlanAsync` ahora reemplaza la suscripción que ya exista, porque un pago por webhook en la preparación de la prueba ya crea la prueba de 15 días.

## Qué se hizo
- **Application**: `Subscriptions/ITrialStarter` (`StartAsync(merchantId, startsAt)`); `PublicCatalogMessages.TrialRequired`.
- **Infrastructure**:
  - `Subscriptions/TrialStarter`: servicio scoped (antes, clase estática);
  - `Payments/PaymentRecorder`: el disparador, en un `try` que solo registra el error;
  - `Jobs/SubscriptionDailyJob`: `StartMissingTrialsAsync` y `SubscriptionDailyRunResult.TrialsStarted`;
  - `BankAccountStatusSync` y `BankAccountRegistration`: ya no empiezan la prueba;
  - `MerchantEmails`: `FirstChargeTrialText`, el párrafo y el preheader del correo de cuenta activa (`BankAccountActiveBody(..., trialDays)`, que `BankAccountActivationEmailNotifier` llena si no tiene suscripción) y el texto del premio pendiente;
  - `AdminPlanActions`: el mensaje de «Dar premios pendientes» sin suscripción.
- **Web**:
  - Inicio: el último paso del checklist dice «Haz tu primer cobro: ese día empiezan tus 15 días de Pro.», y la tarjeta «Ya puedes cobrar» de un activo sin prueba lleva `data-trial-first-charge` (`MerchantHomeViewModel.TrialStartsOnFirstCharge`/`FirstChargeTrialText`);
  - «Mi plan» en el onboarding: «Tu prueba de 15 días de Pro empieza con tu primer cobro. Después podrás elegir tu plan.»;
  - «Catálogo en línea» e «Invita y gana» en el onboarding;
  - admin, pestaña Plan: «La prueba de Pro empieza con su primer cobro pagado.».

## Pruebas
- **Unitarias**: 752, en verde (sin cambios).
- **Integración**: 905, en verde; `dotnet build` sin advertencias; `has-pending-model-changes` sin cambios.
  - `TrialTests` reescrito:
    - activar la cuenta (webhook, sondeo o botón, y alta que Pey activa) ya no crea la prueba, e Inicio y «Mi plan» dicen que empieza con el primer cobro;
    - el primer pago por webhook QR, por push y por conciliación empieza la prueba con el `PaidAt` del pago; un segundo pago no la cambia;
    - dos pagos simultáneos crean una sola suscripción;
    - quien ya tuvo prueba no recibe otra;
    - con `Factory.TrialFaults` el webhook responde 200, la venta queda pagada y avisada, y el job diario empieza la prueba (una sola vez);
    - el respaldo toma el primer pago y deja fuera a los suspendidos, a los que no cobraron y a los que ya tienen suscripción;
    - aislamiento entre vendedores.
  - `ReferralTests`: el premio pendiente de quien invita sale con su primer cobro, encadenado después de la prueba, y «Invita y gana» deja de mostrar el aviso de onboarding.
  - `StorefrontTests`: antes del primer cobro no se publica y dice que se activa con la prueba.
  - `EmailLayoutTests`: el correo de cuenta activa con y sin la prueba.
  - Al día: `MerchantHomeTests`, `CustomerPlanTests`; `BrandedReceiptTests` pasa con el `SeedPlanAsync` que reemplaza.
- **Portada**: 61, en verde.

## Revisión en navegador
- Playwright a 375, 768 y 1280 px, con el sitio arrancado desde Bash (`Resend__ApiKey=`, `Pey__ApiKey=`, `Jobs__Enabled=false`) contra la base local `Local133` (`localhost\SQLEXPRESS`), sin llamar a Pey.
- Vendedor de prueba: `cobro513@prueba.mx` (Merchant 53), registrado en el sitio y confirmado por SQL. Primero en revisión de Banxico y luego activo, sin suscripción.
- Revisado:
  - Inicio en revisión: el último paso dice «Haz tu primer cobro: ese día empiezan tus 15 días de Pro.»;
  - Inicio activo: «Tu prueba de 15 días de Pro empieza con tu primer cobro.» en la tarjeta «Ya puedes cobrar»;
  - «Mi plan», «Catálogo en línea» e «Invita y gana» con sus textos de onboarding.
- En todas: sin scroll horizontal, consola sin errores y ningún recurso con 400 o más.
- Capturas en `.playwright-mcp/fase513-*.png` (fuera de git).

## Pendientes
- **Con Pey real** (requiere confirmación; es el punto 1 de la verificación de extremo a extremo del diseño, §16): que el primer cobro real de $0.01 empiece la prueba.
- En la base local quedan el usuario `cobro513@prueba.mx` (Merchant 53, activo, sin cuenta bancaria registrada).
- Textos de la portada sobre la prueba: 5.18.
