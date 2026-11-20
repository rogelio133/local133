# Fase 5.3 — Planes y vigencia

**Estado:** ✅ hecho 2026-11-19 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas)

## Alcance (del plan)
Base de la suscripción ([diseño §3, §9 y §10](../actualizaciones/suscripciones_2026_11_18.md#53-planes-y-vigencia)):
- estados `Onboarding`, `Trial`, `Pro`, `Grace` y `Free`, calculados con la historia de periodos;
- prueba de 30 días al activarse la cuenta bancaria;
- precios y parámetros en `SystemSettings`;
- migración con la prueba para los vendedores activos.

**Demostrable:** Inicio muestra «Prueba: te quedan 23 días» o el estado que corresponda. Todavía no se aplica ningún bloqueo del gratuito: eso es la 5.5.

La 5.2 (CLABE única) sigue pendiente; la 5.3 no depende de ella.

## Decisiones
- **Periodos al instante exacto** (confirmado por el usuario el 2026-11-19). La prueba empieza al activarse la cuenta, por ejemplo el 19/11 a las 15:00, y termina el 19/12 a las 15:00. Meses y días se suman **en hora de México** con `MexicoTime.AddMonths`/`AddDays`, y del 31 de enero un mes llega al 28 o 29 de febrero. En pantalla solo se muestra la fecha: el último día con Pro es el del instante anterior al fin (`PlanStatus.LastProDay`).
- **Solo dos tablas**, `Subscriptions` y `SubscriptionPeriods`. `SubscriptionCharges`, `SubscriptionPayments`, `Referrals` y `Notifications` llegan en la 5.6, la 5.8 y la 5.4, junto con las columnas `SubscriptionChargeId` y `ReferralId` de los periodos y sus llaves foráneas. Los campos de fundador y de anual congelado (`IsFounder`, `FounderNumber`, `LockedAnnualPrice`) sí se crean ya, porque `SubscriptionPricing` los usa.
- Todos los parámetros nuevos de `SystemSettings` (§9) entran en esta migración, aunque varios se usen hasta la 5.5–5.10, para no migrar la fila semilla en cada subfase.
- `Onboarding` = sin periodo `Trial`. Un vendedor activo sin periodos, que solo existe en datos de prueba, también se ve como `Onboarding` y no muestra la tarjeta del plan.
- `PlanEntitlements`: `Onboarding`, `Trial`, `Pro` y `Grace` tienen todo Pro. En el onboarding el catálogo es editable y el cobro lo frena `MerchantStatus`.
- La prueba se crea en el **mismo guardado** que el cambio de estatus del vendedor. Si dos activaciones llegan a la vez (webhook y sondeo), el índice único de `Subscriptions.MerchantId` deja pasar una y la otra falla al guardar. El webhook responde 500 y Pey lo reintenta, el sondeo lo repite en la siguiente corrida y el botón muestra el error genérico. En el reintento el vendedor ya está activo y no se crea otra prueba.
- El backfill incluye a los vendedores `Active`, `Suspended` y `Disabled`, porque `ApplyBankAccountStatus` ya no los vuelve a pasar a activo y nunca recibirían la prueba.
- El periodo nuevo se agrega con dos métodos en lugar de un solo `AddPeriod`: `AddPaidPeriod(package, …)` (meses del paquete) y `AddDaysPeriod(kind, days, …)` (premio o regalo).

## Qué se hizo
- **Dominio** (`Domain/Subscriptions`):
  - `SubscriptionEnums`: `PlanTier`, `SubscriptionPackage` (con `Months()`), `SubscriptionPeriodKind`, `SubscriptionChargeStatus` y `SubscriptionDiscountReason`.
  - `SubscriptionPeriod` con `[StartsAt, EndsAt)` y `Covers(instante)`.
  - `Subscription`:
    - `StartTrial(merchantId, now, trialDays)`;
    - `AddPaidPeriod` y `AddDaysPeriod`, que encadenan con `PlanStatus.NextPeriodStart` y actualizan `ProUntil`, `LastPeriodKind` y `LastPackage` (solo con un pago);
    - `StatusAt`.
  - `PlanStatus.For(periodos, ahora, ajustes | graceDays)` da el estado, `ProUntil` y `GraceEndsAt` (solo tras un periodo pagado). También ofrece `IsProLike`, `LastProDay`, `LastGraceDay`, `DaysLeft(now)` (calendario de México; 0 = termina hoy) y `NextPeriodStart`.
  - `PlanEntitlements.For(tier)`.
  - `SubscriptionPricing.Quote(...)` para los paquetes, el fundador, el anual congelado (el menor entre el congelado y el vigente) y el descuento de bienvenida (30 % del mes que le toca). `VatBreakdown(...)` desglosa el IVA incluido.
  - `MexicoTime.AddMonths`/`AddDays`. `StartOfDayUtc` ahora usa el mismo `FromLocal`.
  - `SystemSettings`: 15 campos nuevos con sus `Default*`.
- **Persistencia**:
  - `SubscriptionConfiguration`/`SubscriptionPeriodConfiguration`:
    - `Subscriptions.MerchantId` como llave alterna (única) con FK a `Merchants`;
    - periodos ligados por `MerchantId` a esa llave;
    - enums como string;
    - índice único filtrado de `FounderNumber` e índice `(MerchantId, EndsAt)`;
    - checks de rango.
  - `SystemSettingsConfiguration`: precisiones, cuatro checks nuevos y la fila semilla.
  - `AppDbContext`: los `DbSet` y los filtros multi-tenant.
  - Migración **`Subscriptions`**: columnas de `SystemSettings` con sus valores, las dos tablas y un `Sql` que crea la suscripción y el periodo `Trial` (desde `SYSUTCDATETIME()` y por `TrialDays`) a los vendedores que ya pasaron el onboarding.
- **Prueba al activarse**: `Infrastructure/Subscriptions/TrialStarter.EnsureTrialAsync` la agrega al contexto sin guardar, y solo si el vendedor no tiene suscripción. La llaman `BankAccountStatusSync.ApplyAsync` (webhook, sondeo y botón) y `BankAccountRegistration` (alta que Pey activa de inmediato), antes del `SaveChangesAsync` que ya tenían.
- **`IPlanAccess`** (Application/Subscriptions; `Infrastructure/Subscriptions/PlanAccess`, scoped):
  - `GetCurrentAsync` usa el tenant de la sesión y lanza una excepción sin vendedor;
  - `GetForMerchantAsync(id)` no depende de la sesión;
  - guarda en caché por scope el resultado y `SystemSettings`;
  - devuelve `MerchantPlan(Status, Entitlements, TrialDays)`.
- **Inicio** (`/app`):
  - `PlanSummaryViewModel` arma la tarjeta según el estado:
    - prueba: «Prueba: te quedan N días», «te queda 1 día» o «termina hoy», con «Tienes todo Pro hasta el …»;
    - Pro: «Plan Pro», con «Vigente hasta el …»;
    - gracia: «Tu plan venció el …», con «Tienes hasta el … para pagar sin perder Pro», borde e ícono de aviso;
    - gratuito: «Plan gratuito».
  - Va debajo de la tarjeta de estatus, para vendedores activos y suspendidos. No se muestra en el onboarding ni a un vendedor dado de baja.
  - En el onboarding, el paso «¡Listo para cobrar con CoDi!» dice «Al activarse empiezan tus 30 días de prueba de Pro, sin costo.» (`data-trial-notice`).
  - Solo clases de Bootstrap, sin CSS nuevo.

## Pruebas
- **Unitarias** (`tests/Local133.UnitTests/Subscriptions`, corren en CI):
  - `PlanStatusTests`:
    - onboarding;
    - prueba de 30 días y días restantes;
    - último día y fin de la prueba sin gracia;
    - Pro;
    - gracia día 5 contra día 6;
    - sin días de gracia;
    - fin de un premio o regalo sin gracia;
    - un premio después de un pago quita la gracia;
    - medianoche de México contra UTC;
    - gracia negativa.
  - `SubscriptionPeriodChainTests`:
    - prueba;
    - pago en la prueba;
    - pago en la gracia (vence el 10 y paga el 13 ⇒ hasta el 10 del mes siguiente);
    - pago después de la gracia;
    - anual sobre mensual;
    - paquetes de 3 y 6 meses;
    - premios encadenados;
    - 31/01 ⇒ 28/02 y 29/02 en bisiesto;
    - mes pagado a las 11 p. m. de México;
    - validaciones.
  - `SubscriptionPricingTests`: paquetes, fundador, anual congelado (sube y baja), descuento de $59.70 y $44.70, IVA de $199 ($171.55 + $27.45) y valores por defecto.
  - `PlanEntitlementsTests`, y en `MexicoTimeTests` los casos de `AddMonths`/`AddDays`.
- **Integración**:
  - `Subscriptions/TrialTests`:
    - la prueba empieza con el webhook `bank.account.updated` (una sola vez, aunque llegue otro aviso), con la consulta del sondeo o del botón y con el alta que Pey activa de inmediato; en este último caso Inicio muestra «Prueba: te quedan 30 días»;
    - sin prueba con `PENDING`, `PROCESSING` ni `ERROR`;
    - un vendedor que ya tuvo su prueba no recibe otra;
    - aislamiento: filtro de EF e `IPlanAccess` (prueba, gratuito, onboarding, y `GetCurrentAsync` sin vendedor).
  - `MerchantHomeTests`: aviso del onboarding, prueba (23 días), Pro, gracia, gratuito, dado de baja sin tarjeta, y el plan de otro vendedor no se mezcla.
- **Resultados**:
  - `dotnet build` sin advertencias;
  - `dotnet ef migrations has-pending-model-changes`: sin cambios;
  - `dotnet test`: 361 unitarias, 12 de la portada y 537 de integración, todas en verde.
- **Migración en la base local `Local133`**: los 6 vendedores `Active` (4, 15, 16, 17, 20, 21) tienen su suscripción con un periodo `Trial` del 19/11/2026 22:38 UTC al 19/12/2026 22:38 UTC. `SystemSettings` queda con `MonthlyPrice 199`, `AnnualPrice 1990`, `TrialDays 30`, `GraceDays 5` y `ReferralWelcomeDiscountRate .30`.

## Revisión en navegador
Playwright (Edge) a 375, 768 y 1280 px, con el sitio en `http://localhost:5231`, `Jobs__Enabled=false` (sin llamadas a Pey) y, después del primer arranque, `Resend__ApiKey=` vacío. El vendedor de prueba es `plan53@prueba.mx` (Merchant 32). Su estado se cambió por SQL entre capturas:
- **onboarding** (en revisión): checklist con el aviso de la prueba en el último paso;
- **prueba**: «Prueba: te quedan 23 días · Tienes todo Pro hasta el 12 de diciembre.»;
- **Pro**: «Plan Pro · Vigente hasta el 29 de noviembre.»;
- **gracia**: «Tu plan venció el 17 de noviembre · Tienes hasta el 22 de noviembre para pagar sin perder Pro.», con borde e ícono de aviso;
- **gratuito**: «Plan gratuito».

En todos: sin scroll horizontal, nada tapado por la barra inferior, pestaña «Inicio» activa, consola sin errores ni advertencias y ningún recurso con 404. Las capturas están en `.playwright-mcp/fase53-*.png` (fuera de git).

Notas:
- En el primer arranque `Resend:ApiKey` estaba en los user-secrets, así que el registro de `plan53@prueba.mx` mandó un **correo real** de confirmación por Resend. La dirección es inventada. Después el sitio se reinició sin la llave y el correo se confirmó por SQL.
- En la base de desarrollo quedan el usuario `plan53@prueba.mx` (Merchant 32, `Active`, sin cuenta bancaria) y su suscripción de prueba en estado Pro.

## Pendientes
- Nada propio de la 5.3. La tarjeta del plan todavía no lleva a «Mi plan» ni al link de pago, que llegan en la 5.6. Los bloqueos del gratuito son de la 5.5.
