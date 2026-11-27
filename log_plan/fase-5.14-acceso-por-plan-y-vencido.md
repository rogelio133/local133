# Fase 5.14 — Acceso por plan y vencido

**Estado:** ✅ hecho 2026-11-27, sin publicar: sale junto con la 5.12, la 5.13 y la 5.15 a la 5.18 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [diseño](../actualizaciones/planes_basico_pro_2026_11_26.md#514-acceso-por-plan-y-vencido)

## Alcance (del plan)
- El vencido no genera cobros (se valida en el servidor) y ve el banner y la tarjeta para elegir plan.
- Básico no tiene carrito, catálogo editable, Excel ni catálogo en línea.
- Se retira todo lo del gratuito: límite mensual, historial de 30 días y recibo limitado.

**Demostrable:** con el plan vencido, Cobrar muestra «Tu plan venció el 17 de noviembre · Elige un plan para seguir cobrando» en lugar del formulario, y un POST directo no crea la venta ni llama a Pey. En Básico, Cobrar por monto funciona, Ventas busca y filtra por fecha, y el corte abre.

## Decisiones (propias)
- **«Elegir plan»** va a `PlanUrls.ChoosePlan`, que hoy es «Mi plan». La 5.15 lo apunta al checkout cambiando una sola constante.
- **Correos de recordatorio**: usaban `FreeMonthlyPaidLimit` y `FreeHistoryDays`, que se borraron. Solo cambió el texto que prometía el gratuito: ahora dice que con el plan vencido no se generan cobros y que el vendedor conserva su catálogo, su historial y sus datos. El asunto de la baja es «Tu plan venció · Local133» y su botón dice «Elegir mi plan». Lo mismo en el aviso de renovar de «Mi plan» (`RenewalNoticeViewModel`). Quedan para la 5.16:
  - las etapas `Expired` y `ExpiredFollowUp`;
  - los botones al checkout;
  - el resumen mensual por plan;
  - renombrar `NotificationType.DowngradedToFree`. Se guarda como texto en `Notifications`, así que no se cambió.
- **Banner del vencido**: no sale en Inicio (`Home/Index`) ni en Cobrar (monto y carrito), que ya muestran el aviso completo. Tampoco sale en «Mi plan» ni a un vendedor dado de baja. Sí sale en «Más» (`Home/More`).
- **Inicio del vencido** (activo): la tarjeta «Tu plan venció» va en lugar de «Ya puedes cobrar», y la tarjeta del plan de abajo no se repite (`MerchantHomeViewModel.PlanCard`).
- **Corte del vencido**: `Sales/CashCutLocked` recibe un `PlanExpiredViewModel` con su propio texto. En la lista, la liga del corte lleva un candado en lugar de la insignia «Pro», porque Básico también tiene corte.
- **«Tu catálogo de N productos te espera en Pro.»** (`WaitingCatalogText`): se usa en Cobrar (Básico), el carrito, Productos y «Catálogo en línea». En Cobrar, la liga que sigue al texto dice «Conoce Pro».
- **Tabla de «Mi plan»**: ya es la matriz de §2 (Básico | Pro), porque la anterior usaba los parámetros borrados. El rediseño de «Mi plan» sigue en la 5.15.
- **Admin**:
  - Parámetros sin los tres campos del gratuito, con la tarjeta «Uso justo (Básico y Pro)»;
  - en la pestaña Ventas del cliente, los cobros del mes salen de su propia consulta y sin el «de 100».
- **Portada y Términos**: siguen hablando del gratuito hasta la 5.18. Sus pruebas comparan contra las constantes locales `PublishedFreeMonthlyPaidLimit` (100), `PublishedFreeMonthlyGeneratedLimit` (300) y `PublishedFreeHistoryDays` (30).

## Qué se hizo
- **Dominio**:
  - `PlanEntitlements` sin `CanShareReceipt`, `HasFullHistory` ni `HasMonthlyChargeLimit`; el vencido es Básico sin `CanCharge` ni corte;
  - `FairUse.Crossed` reemplaza a `MonthlyChargeAllowance`, que se borró;
  - `SystemSettings` y `SystemSettingsRules` sin `Free*` (`InTerms` queda con 11 campos).
- **Application**:
  - `CreateSaleResult.PlanExpired`, sin `MonthlyLimitReached` ni `GenerationLimitReached`;
  - `Subscriptions/ChargeLimitAlerts.cs` con `IChargeLimitAlerts` e `IChargeLimitNotifier.FairUseReachedAsync`. Se borraron `IChargeUsage` y `MonthlyChargeUsage`;
  - `MerchantPlan` sin `FreeHistoryDays` ni `HistoryStartUtc`.
- **Infraestructura**:
  - `SaleCharging.PlanBlockAsync`: `PlanExpired` y luego `PlanRequired` del carrito;
  - `ChargeLimitAlerts` cuenta él mismo los concretados del mes y avisa del uso justo en los dos planes. Se borró `ChargeUsage`;
  - migración **`RemoveFreePlanSettings`**;
  - los textos de `MerchantEmails.SubscriptionReminder`.
- **Web**:
  - parcial `_PlanExpired` y `PlanExpiredViewModel`;
  - Cobrar y el carrito con el aviso del vencido;
  - la tarjeta de Inicio;
  - `PlanBannerViewComponent` con las variantes de gracia y de vencido (reemplaza a `GraceBannerViewComponent`);
  - Ventas sin recorte: búsqueda y fechas en todos los planes, y se borró `DetailLocked`;
  - recibo sin modo limitado;
  - `CatalogReadOnlyReason { Disabled, Basic, Expired }` con `ProductListQuery.ReadOnlyReasonFor`;
  - textos sin «gratuito» (catálogo en línea, «Invita y gana», productos y admin);
  - se borraron `_ChargeUsage`, `ChargeUsageViewModel` y `PlanBlockedNotices`.

## Pruebas
- **Unitarias** (735, en verde):
  - `FairUseTests` reemplaza a `MonthlyChargeAllowanceTests`;
  - `PlanEntitlementsTests` sin las banderas del gratuito;
  - `AdminPlanRulesTests` sin los casos `Free*`.
- **Integración** (903, en verde; `dotnet build` sin advertencias; `has-pending-model-changes` sin cambios):
  - **`PlanAccessTests`** (reemplaza a `FreePlanLimitsTests`):
    - el vencido no genera QR, push ni carrito, y Pey no recibe nada;
    - Cobrar y el carrito muestran el aviso, y el catálogo del carrito da 403;
    - las ventas generadas antes de vencer (monto y carrito) se abonan por webhook;
    - el vencido ve todo su historial con búsqueda, el detalle de una venta de hace 60 días y el recibo con WhatsApp;
    - el vencido no ve el corte ni exporta, y su catálogo es de solo lectura;
    - Inicio del vencido muestra su tarjeta;
    - Básico cobra por monto y por celular, tiene historial completo, búsqueda, fechas y corte, comparte por WhatsApp, no cobra con carrito ni llama a Pey y ve su catálogo en solo lectura;
    - uso justo: avisa una sola vez al cruzarlo en Básico y en la prueba, y si el aviso falla, el pago queda guardado;
    - aislamiento entre vendedores;
    - la tabla de «Mi plan»;
    - «Mi plan» solo para vendedores.
  - **`PlanBannerTests`** (antes `GraceBannerTests`): la gracia como antes; el vencido en Ventas, Productos y «Más», pero no en Inicio, Cobrar, el carrito ni «Mi plan»; dado de baja sin banner.
  - **Al día**:
    - `StorefrontTests`, `ProductCrudTests`, `SalesHistoryTests` y `CashCutTests` con Básico, y `CashCutTests` también con el vencido;
    - `SalesExportTests` con Básico y vencido;
    - `MerchantHomeTests` con la tarjeta del vencido;
    - `TrialTests`: quien ya tuvo prueba recibe el pago de una venta generada antes, porque ya no puede generar una;
    - `SubscriptionDailyJobTests` con el asunto nuevo;
    - `SettingsTests` sin `Free*`;
    - `SeedFreePlanAsync` → `SeedExpiredPlanAsync`.
- **Portada**: 61, en verde.

## Migración en la base local
`RemoveFreePlanSettings` se aplicó con `--connection` a `Local133` en `localhost\SQLEXPRESS`. `SystemSettings` ya no tiene columnas `Free*`.

## Revisión en navegador
- Playwright a 375, 768 y 1280 px, con el sitio arrancado desde Bash (`Resend__ApiKey=`, `Pey__ApiKey=`, `Jobs__Enabled=false`) contra la base local, sin llamar a Pey.
- **Vendedor de prueba** `plan514@prueba.mx` (Merchant 55):
  - registrado en el sitio y confirmado por SQL;
  - por SQL: estatus activo, una cuenta bancaria activa con una CLABE ficticia (no se mandó a Pey), dos productos, dos ventas pagadas (una de hace 60 días) y el plan, cambiado entre revisiones.
- **Vencido**:
  - Cobrar y el carrito con el aviso y sin el formulario;
  - Inicio con la tarjeta «Tu plan venció»;
  - el banner en Ventas, el detalle, «Más» y «Catálogo en línea», y no en Inicio ni en Cobrar;
  - Ventas con búsqueda y fechas, y la liga del corte con candado;
  - el detalle con los botones de WhatsApp;
  - el corte bloqueado con su texto;
  - Productos en solo lectura con «Elegir plan».
- **Básico**: Cobrar con el formulario y «Tu catálogo de 2 productos te espera en Pro. Conoce Pro»; el carrito con el aviso de Pro; Ventas con la búsqueda «vieja» (solo la venta de hace 60 días); el corte abierto; Productos en solo lectura; la tabla Básico | Pro de «Mi plan»; Inicio con «Plan Básico».
- **Admin**: con un admin local de revisión (`revision514.admin@local133.test`, sembrado al arrancar con `Seed__Admin__*` por variable de entorno). Parámetros con «Uso justo (Básico y Pro)» y sin los campos del gratuito; la pestaña Ventas del cliente con «Cobros del mes».
- En todas: sin scroll horizontal, con la pestaña activa correcta, nada tapado por la barra inferior, la consola sin errores y ningún recurso con 400 o más.
- Capturas en `.playwright-mcp/fase514-*.png` (fuera de git).

## Pendientes
- En la base local quedan el vendedor `plan514@prueba.mx` (Merchant 55, con su cuenta bancaria ficticia, productos, ventas y Básico) y el admin `revision514.admin@local133.test`.
- La base de desarrollo remota (`192.168.100.11`) sigue sin la 5.12 ni la 5.14; se aplican solas al arrancar con esa conexión.
- Lo demás está asignado a su subfase: checkout y conversión (5.15); recordatorios `Expired`/`ExpiredFollowUp`, botones al checkout, resumen por plan y anuncio (5.16); admin v2 (5.17); portada y Términos sin el gratuito (5.18).
