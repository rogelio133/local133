# Fase 5.12 — Planes y vigencia v2 (Básico y Pro)

**Estado:** ✅ hecho 2026-11-27, sin publicar: sale junto con la 5.13 a la 5.18 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [diseño](../actualizaciones/planes_basico_pro_2026_11_26.md#512-planes-y-vigencia-v2)

## Alcance (del plan)
- `SubscriptionPlan` en periodos y cargos, y estados `Basic` y `Expired` en lugar de `Free`;
- funciones por estado, precios por plan, fundador y anual congelado por plan;
- conversión de Básico a Pro al subir de plan;
- migración con la transición de los vendedores existentes.

**Demostrable:** Inicio y «Mi plan» muestran Básico, Pro, Vencido o «Pro hasta el 9 de diciembre, después Básico hasta el 8 de enero de 2027».

## Decisiones (propias, para no adelantar otras subfases)
- **Lo del gratuito se queda hasta la 5.14.** Las columnas `Free*` de `SystemSettings` siguen, y la 5.14 las borra con su propia migración. Las banderas `CanShareReceipt`, `HasFullHistory` y `HasMonthlyChargeLimit` también siguen:
  - el **vencido** conserva los límites del gratuito, que es lo que hoy aplica el servidor;
  - los demás estados, Básico incluido, los tienen libres.

  Así las pruebas de los límites siguen en verde, y la 5.14 retira todo junto: código, vistas, pruebas y el bloqueo con `CanCharge`.
- **`CanCharge`** existe en `PlanEntitlements`, pero el bloqueo del vencido en el servidor es de la 5.14.
- **El abono** usa el plan del cargo (`AddPaidPeriod(charge.Plan, …)` y `LockAnnualPrice(charge.Plan, …)`). `ConvertBasicToPro` se conecta a `SubscriptionCrediting` en la 5.15, con sus pruebas de webhook. Antes de la 5.15 no se puede crear un cargo de Básico, porque «Mi plan» cotiza y ofrece solo Pro.
- **`ConvertBasicToPro`**:
  - un Básico en curso se corta en `now` y lo que le quedaba pasa a un periodo `Conversion` nuevo, sin cargo ni referido;
  - un Básico que no ha empezado se vuelve `Conversion` en su lugar y conserva su `SubscriptionChargeId` o su `ReferralId`. Así no se pierde de dónde salió, y el admin todavía puede anular el premio: `RemoveUnusedDays` acepta un `Conversion` con referido;
  - las duraciones encogidas se redondean hacia abajo al segundo, con un mínimo de un segundo.
- **Vista previa**: `NextPeriodStartFor(plan, …)` simula la conversión sobre una copia de los periodos, así que siempre coincide con el abono real. `SubscriptionOffers.For(…, plan, …)` la usa para el «hasta el …» de Pro.
- **`PlanStatus`** gana `Plan`, `PlanEndsAt`, `NextPlan` y `NextPlanEndsAt` como escalares, para que el record conserve la igualdad por valor. También expone `LastPlanDay`, `NextPlanStartDay` y `NextPlanLastDay`.
- **`LastPlan`** es el plan del **último pago**, junto con `LastPackage`. El plan del final de la cadena se calcula con `Subscription.ChainEndPlan`, y con él se dan:
  - el premio de referidos (regla de §8);
  - el regalo del admin, hasta que la 5.17 ponga el selector.
- **Pagar otro plan suelta el anual congelado** (`AddPaidPeriod`), y el congelado solo aplica si `LockedAnnualPlan` es el plan que se cotiza.
- **Onboarding**: `CanPublishCatalog` está en falso desde ya (matriz de §3). El texto «Se activa con tu prueba» es de la 5.13.
- **Admin, solo para que compile y no diga cosas falsas**:
  - etiquetas «Básico», «Vencido», «Conversión a Pro», «Transición» y «Transición a Básico» (bitácora);
  - el filtro `plan=gratuito` pasa a `plan=vencido`;
  - el filtro «pro» incluye a Básico hasta `WithPlan` v2 (5.17);
  - los precios de Pro solo cambian de nombre en Parámetros, y los de Básico entran a la pantalla en la 5.17. Por eso `SystemSettingsRules.CopyValues` todavía no copia los de Básico: el formulario los pisaría con los valores por defecto.
- **Pruebas de la portada y de los Términos**: comparan los días de prueba con la constante `PublishedTrialDays = 30` hasta que la 5.18 actualice los textos.
- **Transición en la migración**: usa la regla de `CustomerListQuery.WithPlan`. Al que está en prueba con un premio o un regalo encadenado, la transición se le encadena al final de su cadena. El renglón de `AuditLogs` lleva `ActorUserId`/`ActorEmail` = `sistema`.

## Qué se hizo
- **Dominio** (`Domain/Subscriptions`):
  - enums nuevos: `PlanTier` v2, `SubscriptionPlan` con `Label()`, y los kinds `Conversion` y `Transition`;
  - `SubscriptionPeriod.Plan`;
  - `PlanStatus` v2, con `IsCurrent` y `LastActiveDay`;
  - `PlanEntitlements.For(PlanStatus)` y `ForPlan(plan)`, con `CanCharge`;
  - `SubscriptionPricing.Quote(plan, …)`, `MonthlyPrice`/`AnnualPrice(plan, …)` y `ConversionRatio`, y `SubscriptionQuote.Plan`;
  - en `Subscription`:
    - `LastPlan`, `LockedAnnualPlan` y `ChainEndPlan`;
    - `AddPaidPeriod(plan, …)`, `AddDaysPeriod(kind, plan, …)` (con `Transition`) y `LockAnnualPrice(plan, …)`;
    - `ConvertBasicToPro`, `BasicTimeAhead` y `NextPeriodStartFor`;
    - `RefreshTail`, que comparte con `RemoveUnusedDays`;
  - `SubscriptionOffers.For(…, plan, …)` (`Until` y `LastDay`);
  - `SubscriptionCharge.Plan`, `TermsVersion` y `TermsAcceptedAt`; la reutilización compara el plan;
  - `SubscriptionReminders` con `Expired` y `Basic`;
  - `SystemSettings` con los ocho precios, `FairUseMonthlyPaid` y `TrialDays = 15`;
  - `AuditAction.PlanTransition`.
- **Infraestructura**:
  - configuraciones de EF;
  - migración **`BasicAndProPlans`**:
    - renombra columnas, agrega los precios de Básico con sus valores y pone `Plan = 'Pro'` en lo existente;
    - llena `LastPlan` y `LockedAnnualPlan`;
    - crea la transición y su bitácora;
    - el `Down` quita la transición;
  - `PlanAccess`, `SubscriptionCrediting`, `SubscriptionCheckout` (Pro fijo), `ReferralRewarder`, `AdminPlanActions` y `MonthlySummaryNotifier`.
- **Web**:
  - `PlanSummaryViewModel` arma la tarjeta de Inicio y de «Mi plan» con Básico, Pro y vencido, el tramo que sigue, la gracia con el nombre del plan, e `IconClass`/`BorderClass`;
  - el banner de gracia dice el plan;
  - `OffersTitle`/`OffersNote` contemplan Básico y vencido;
  - en el admin, etiquetas, filtros y renombres.

## Pruebas
- **Unitarias** (752, en verde):
  - nuevas:
    - `PlanStatusPlansTests`: Básico, la gracia de Básico y la de Pro, vencido después de la prueba, de los días regalados de Básico y de una conversión, Básico pagado en la prueba (sigue en `Trial`), Pro pagado en la prueba, «Pro, después Básico» y `ChainEndPlan`;
    - `ConvertBasicToProTests`:
      - el ejemplo de §3: 20 días ⇒ 9 d 22:47:38, fin el 10/dic a las 08:47:38 y el anual hasta el 10/dic/2027;
      - fundador (9 d 06:16:54);
      - varios tramos, Básico después de Pro y recorrido sin hueco;
      - sin Básico y en la gracia de Básico, sin cambios;
      - fin de mes y medianoche de México;
      - la vista previa coincide y no cambia nada;
      - ofertas de los dos planes ($396 más por el anual, 2 meses de regalo);
      - anular un premio convertido;
      - validación de la proporción;
  - al día:
    - `PlanEntitlementsTests`, con la matriz completa;
    - `SubscriptionPricingTests`: la tabla de §2, bienvenida de $29.70, $20.70, $59.70 y $44.70, IVA de $99 y de $69, `ConversionRatio` y valores por defecto;
    - `SubscriptionChargeTests`: plan en el cargo, reutilización por plan, congelado por plan y que se suelta al pagar otro plan;
    - con renombres: `SubscriptionPeriodChainTests`, `ReferralTests`, `AdminPlanRulesTests`, `SubscriptionRemindersTests`, `MonthlySummaryTests` y `MerchantPublicCatalogTests`.
- **Integración** (900, en verde; `dotnet build` sin advertencias):
  - `MerchantHomeTests`: Básico, «Pro, después Básico» con las fechas, vencido, la gracia de Básico con el banner, y el aviso de onboarding con 15 días;
  - `TrialTests` con 15 días;
  - `CustomersTests` con el filtro `vencido` (Básico fuera del «cubre todos los estados» hasta la 5.17);
  - `FreePlanLimitsTests`: la etiqueta `Expired`;
  - helpers `SeedPlanAsync` con el plan por periodo y `SeedBasicPlanAsync`.
- **Portada**: 61 pruebas, en verde.
- `dotnet ef migrations has-pending-model-changes`: sin cambios.

## Migración en una base local
La conexión de los user-secrets (`192.168.100.11`) no respondió, así que la migración se aplicó con `--connection` a la copia `Local133` del SQL Server local de este equipo (`localhost\SQLEXPRESS`). Resultado:

| Estado al migrar | Vendedores | Qué recibieron |
|---|---|---|
| En prueba | 15, 16, 17, 20, 21, 39 y 43 (este con un regalo encadenado) | `Transition` de Básico de 15 días al terminar su cadena |
| Gratuito | 33, 44 y 45 | `Transition` de 15 días desde la migración |
| Gratuito, con la gracia de un pago ya terminada | 35 | `Transition` de 15 días desde la migración |
| Pro pagado o regalo vigente | 4, 32 y 34 | nada |

- Cada transición dejó su renglón `PlanTransition` en `AuditLogs`, con las fechas en hora de México y el detalle en JSON.
- `SystemSettings` quedó con Básico 99/990/69/690, Pro 199/1990/1490 y `TrialDays = 15`. El fundador mensual de Pro conserva el $0.01 que ya tenía esa base, por las pruebas con Pey.
- Los 5 cargos existentes quedaron en Pro.

## Revisión en navegador
- Playwright a 375, 768 y 1280 px, con el sitio arrancado desde Bash (`Resend__ApiKey=`, `Pey__ApiKey=`, `Jobs__Enabled=false`) contra la base local, sin llamar a Pey.
- Vendedor de prueba: `plan512@prueba.mx` (Merchant 52). Su estado se cambió por SQL entre revisiones.
- Inicio y «Mi plan» en cuatro estados:
  - **Básico**: «Plan Básico · Vigente hasta el 17 de diciembre.»;
  - **Pro, después Básico**: «Plan Pro · Pro hasta el 9 de diciembre, después Básico hasta el 8 de enero de 2027.», en dos líneas a 375 px;
  - **vencido**: «Tu plan venció el 13 de octubre · Elige un plan para seguir cobrando.», con borde e ícono rojos;
  - **gracia de Básico**: «Tienes hasta el 30 de noviembre para pagar sin perder Básico.», y el banner «Tu plan Básico venció el 25 de noviembre…».
- En todos: sin scroll horizontal, la pestaña activa correcta («Inicio» o «Más»), la consola sin errores y ningún recurso con 400 o más.
- Las capturas de la tarjeta están en `.playwright-mcp/fase512-*.png` (fuera de git).

## Pendientes
- En la base de desarrollo remota (`192.168.100.11`) la migración no se aplicó, porque no respondía. Se aplica sola al arrancar el sitio con esa conexión.
- En la base local quedan el usuario `plan512@prueba.mx` (Merchant 52) y los periodos de transición de la migración.
- Lo demás está asignado a sus subfases: prueba al primer cobro (5.13); bloqueo del vencido y retiro del gratuito (5.14); checkout por plan y conversión en el abono (5.15); textos de recordatorios, resumen y anuncio (5.16); admin v2 (5.17); portada y Términos (5.18).
