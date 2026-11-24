# Modelo de suscripción — 2026-11-18

Cambia el ingreso de la plataforma. Antes era una **comisión del 3 % por venta** (más IVA), agrupada en estados de cuenta semanales que se pagaban con QR CoDi. Ahora el vendedor paga una **suscripción**: hay un plan gratuito con límites y un plan Pro mensual o anual, que se paga con el **link de pago de Pey**. Este documento **no implementa nada**: fija las reglas, los datos y el detalle de cada subfase. La lista de subfases está en la [Fase 5 del plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas).

Sale de la propuesta del usuario, de la revisión que hizo con Claude en la app y de las respuestas del 2026-11-18 (§1). Lo que quedó con un valor por defecto sin confirmar está en §11.

---

## 1. Decisiones

| Tema | Decisión |
|---|---|
| Prueba | 30 días de Pro completo, una sola vez por vendedor. Al terminar baja al gratuito. Sustituye las pruebas sueltas del gratuito (5 productos, carrito y WhatsApp el primer mes). |
| Inicio de la prueba | Cuando la cuenta bancaria queda activa, que es cuando ya puede cobrar. Mientras Banxico la revisa, el vendedor puede armar su catálogo. |
| Catálogo en el gratuito | Solo lectura: se conserva completo, pero no se agrega, edita ni cobra con él. |
| Límite del gratuito | 100 cobros concretados por mes calendario (desde el día 1, hora de México). Avisos al 80 % y al 100 %. Al llegar a 100 no genera cobros nuevos hasta el día 1 o hasta que pague (sin cobros de cortesía: se quitaron el 2026-11-20). |
| Precios | Pro: $199 al mes o $1,990 al año (2 meses de regalo), **IVA incluido**. |
| Pago adelantado | Paquetes de 1, 3 y 6 meses, y anual. |
| Factura (CFDI) | Fuera de esta implementación. Mientras tanto se factura fuera del sistema (factura global y las que se pidan). |
| Cobro | Link de pago de Pey, que deposita en la cuenta de la plataforma. Aviso 3 días antes por correo y en el sitio, con el link visible en el sitio. 5 días de gracia; después baja solo al gratuito. |
| Falta de pago | Baja al gratuito; ya no hay suspensión por falta de pago. Conserva su catálogo, su historial y sus datos. |
| CLABE | Única: dos vendedores no pueden registrar la misma. Hoy no existe esa regla. |
| Afiliados: premio | 10 días de Pro por cada referido que paga su primera suscripción, sin importar el plan. |
| Afiliados: referido | 30 % de descuento sobre el precio de un mes en su primer pago. |
| Afiliados: códigos | Un código por vendedor, con un máximo de 12 premios en 12 meses. |
| Afiliados: gratuitos | También pueden referir y ganar días de Pro. |
| Pro al lanzar | Exportar el historial y corte de caja diario. Vendedores adicionales, QR impreso del puesto, marco de QR por producto y reportes se muestran como «Próximamente». |
| Para que convenga pagar | Mostrar lo que se pierde (con un resumen mensual), recibo con marca, catálogo público con liga, precio de fundador y precio congelado del anual. |

## 2. Planes

| Función | Gratuito | Prueba y Pro |
|---|---|---|
| Cobro por monto con QR o por celular | ✓ | ✓ |
| Cobros concretados al mes | 100 | Ilimitados, con uso justo (§5) |
| Catálogo de productos | Solo lectura | ✓ |
| Cobro con carrito (QR o celular) | — | ✓ |
| Recibo | Liga para copiar, con «Cobrado con Local133» | Botones de WhatsApp y compartir, con el logo del negocio |
| Historial de ventas | Últimos 30 días, filtro por estatus | Completo, con rango de fechas, búsqueda y exportación a Excel |
| Corte de caja diario | — | ✓ |
| Catálogo público con liga | — | ✓ |
| Vendedores | 1 (el dueño) | El dueño y 3 adicionales (Próximamente) |
| QR impreso del puesto, marco de QR por producto, reportes | — | Próximamente |
| Código de afiliado | ✓ | ✓ |

Lo marcado «Próximamente» aparece con esa etiqueta en la página de precios y en el portal: nadie paga por algo que todavía no existe.

**Precios (IVA incluido)**

| Paquete | Precio | En pantalla |
|---|---|---|
| 1 mes | $199 | |
| 3 meses | $597 | |
| 6 meses | $1,194 | «Por $796 más llévate el año completo» |
| Anual | $1,990 | «Equivale a $165.83 al mes: 2 meses de regalo» |
| Fundador | $149 al mes, $1,490 al año | De por vida para los primeros 100 que paguen (§7) |

- Los precios se guardan en `SystemSettings`, y cada cargo guarda su propio monto. Cambiar un precio solo afecta los links nuevos.
- De los $199, $171.55 son el servicio y $27.45 el IVA (16 %). El desglose se calcula con `VatRate` y servirá para el CFDI.

## 3. Vigencia del plan

**Estados (`PlanTier`).** Se calculan con la fecha actual y la historia de periodos; no se guardan.

| Estado | Cuándo | Qué tiene |
|---|---|---|
| `Onboarding` | Aún no tiene la cuenta bancaria activa | Catálogo editable, como hoy. No puede cobrar |
| `Trial` | Los 30 días desde que la cuenta quedó activa, mientras no haya pagado | Todo Pro |
| `Pro` | Dentro de un periodo pagado o regalado (referido, admin), o en la prueba con un pago ya abonado (2026-11-20) | Todo Pro |
| `Grace` | Hasta 5 días después de que vence un periodo **pagado** | Todo Pro, con aviso y el link de pago |
| `Free` | Ninguno de los anteriores | Gratuito |

`MerchantStatus` sigue mandando sobre el plan: un vendedor suspendido o dado de baja no cobra en ningún plan. La falta de pago ya no suspende: `Suspended` queda solo para una decisión del admin o un error de la cuenta bancaria.

**Periodos.** Cada día de Pro tiene un registro en `SubscriptionPeriods` con su origen: prueba, pago, premio de referido o regalo del admin. Un periodo nuevo empieza:
- **donde termina el anterior**, si el vendedor sigue con Pro o está en gracia. Pagar durante la gracia no regala días: cuenta desde la fecha de vencimiento original. Ejemplo: vence el 10 y paga un mes el 13 ⇒ Pro hasta el 10 del mes siguiente;
- **en ese momento**, si ya estaba en el gratuito. Ejemplo: paga el 20 ⇒ Pro desde el 20.

Consecuencias:
- Pagar durante la prueba no la acorta: el periodo pagado empieza cuando termina la prueba. Desde que se abona, el vendedor ya ve «Plan Pro» (vigente hasta el fin de lo pagado, con la nota de cuándo empieza), no «Prueba» (ajuste del 2026-11-20, tras la prueba real).
- Comprar el anual a mitad de un mes pagado no pierde días ni necesita prorrateo: el año empieza al terminar el mes.
- La gracia solo sigue a un periodo pagado. Al terminar la prueba o un premio, el vendedor baja directo al gratuito (con aviso 3 días antes).
- Los meses se cuentan en hora de México: del 31 de enero, un mes llega al último día de febrero.
- Los periodos van **al instante exacto** (confirmado el 2026-11-19). La prueba que empieza el 19/11 a las 15:00 termina el 19/12 a las 15:00, y en pantalla solo se muestra la fecha.
- El paquete del último pago (mensual o anual) define el link de renovación y los recordatorios.

**Vendedores existentes** (base local y base de pruebas en Azure): la migración les crea la prueba de 30 días desde el día de la migración si ya están activos. Los demás la reciben al activarse.

## 4. Cobro con link de pago de Pey

**Qué ofrece Pey** (OpenAPI 2026-10-20 y documentación del webhook, revisados el 2026-11-18)
- `POST /payment-link/create` recibe `amount` (> 0), `successUrl` y `backUrl` (HTTPS), `hasTip`, `clientReferenceId` (≤ 255) y `metadata` (≤ 50 pares), y responde `{id, url}`. **No acepta `bankAccountId`**: el dinero cae en la cuenta de la plataforma, que es justo lo que necesita la suscripción.
- La página de pago acepta **CoDi y tarjeta** (`paymentMethods`). En los ejemplos, `amount = base + tip + fee`: con tarjeta parece que el pagador paga la comisión encima del precio. Además hay un `applicationFee`. Está por confirmar (§11).
- **El link no expira ni se puede cancelar.** `POST /payment-link/update` solo cambia el monto mientras está en `pending`.
- `GET /payment-link/{id}` devuelve el `status` (`pending`, `processing`, `paid` o `partial`) y `transactions[]` con `amountBreakdown {base, tip, fee}` y `applicationFee`.
- El webhook `payment_link.status_updated` trae `paymentLinkId`, `status` (`pending`, `processing` o `paid`), `clientReferenceId` y `transactions[]`. La metadata llega en `transactions[].metadata.externalMetadata`. `partial` no aparece en el webhook.
- El backend de la [PoC del link de pago](../log_plan/poc-link-de-pago.md) ya existe (sin commit al 2026-11-18): `IPaymentGateway.CreatePaymentLinkAsync`/`GetPaymentLinkAsync`, los DTOs de `Application/Payments/PaymentLinks.cs`, la validación previa en `PeyRequestGuard` y `PublicSiteOptions.BaseUrlFor`. Se reutiliza tal cual; la pantalla de la PoC se borra.

**Flujo**
1. «Mi plan» (`/app/plan`) muestra el estado, la vigencia, el contador del mes, los pagos anteriores y el botón **Pagar / adelantar**, que siempre está visible.
2. El vendedor elige un paquete. El sitio calcula el monto (precio normal, de fundador o congelado, menos el descuento de bienvenida si aplica), crea un `SubscriptionCharge` y después el link:
   - `amount` = monto del cargo;
   - `successUrl` = `{base}/app/plan/pago/{id}` y `backUrl` = `{base}/app/plan`;
   - `hasTip: false`;
   - `clientReferenceId` = `sub-{id}`;
   - `metadata` = `{kind: "subscription", merchantId, subscriptionChargeId}`.
3. Si ya hay un link `pending` del mismo paquete y monto, se reutiliza en lugar de crear otro.
4. El sitio redirige a la página de Pey.
5. Al volver, `/app/plan/pago/{id}` muestra «Confirmando tu pago…» y consulta su estado cada 3 s. **El regreso del navegador no activa nada.** Solo activan el webhook o una consulta del servidor a `GET /payment-link/{id}`.
6. Con el webhook `payment_link.status_updated` en `paid`:
   - guarda cada transacción, única por `transactionId`;
   - si la suma de `base` cubre el monto, abona el periodo;
   - si es el primer pago de un referido, da el premio al que lo refirió (§6);
   - avisa en el sitio y por correo.

**Reglas**
- Todo link pagado se abona, aunque sea viejo o aunque el vendedor pague dos: Pey no deja anular links. Si paga dos, se le abonan los dos periodos y se le avisa.
- Con `processing` se muestra «Procesando tu pago».
- Con `partial`, o con `paid` por un monto menor, no se abona: queda para revisión del admin (Fase 6).
- Los links se muestran mientras sirven, hasta que termina la gracia. Después se ocultan, pero se siguen abonando si alguien los paga.
- Si el POST se queda sin respuesta (timeout o 500), el cargo queda `Failed` y se crea otro al reintentar. Nadie conoce la URL del link perdido, así que nadie lo puede pagar.
- El cargo se busca por `PeyPaymentLinkId`. `clientReferenceId` y la metadata sirven de respaldo.
- `PaymentRecorder` (pagos de ventas) no cambia: el pago de un link llega por otro webhook.
- Prueba real: en desarrollo se pone el precio en $0.01 en `SystemSettings` y se paga con CoDi. El dinero llega a la cuenta de la plataforma.

**Respaldo del webhook**
- Job `conciliar-links`: consulta `GET /payment-link/{id}` de los cargos pendientes, con un calendario como el de las ventas: cada 10 min durante 2 h, cada hora hasta 3 días y diario hasta 60 días desde que se creó el cargo. *(5.6: el diseño decía «30 días después del fin de la gracia», que no tiene una fecha clara para un cargo pagado por adelantado.)* La pantalla de confirmación y su API también consultan, con un freno de 15 s.
- ~~Barrido nocturno con `GET /transaction/api-key`~~ **pospuesto** (decisión del usuario, 2026-11-20): esa consulta trae todas las transacciones de la API key, incluidas las ventas de todos los vendedores. Lo que no cubren el webhook y la conciliación (un link de más de 60 días pagado con el webhook perdido) se abona a mano desde el admin (Fase 6).

**Recordatorios** (correo y aviso en el sitio, con la liga a «Mi plan»; job diario a las 9:00, hora de México)

| Momento | Mensual | Anual | Prueba |
|---|---|---|---|
| 15 días antes | — | ✓ | — |
| 3 días antes | ✓ | ✓ | ✓ («tu prueba termina») |
| Día del vencimiento | ✓ | ✓ | — |
| Día 3 de la gracia | ✓ | ✓ | — |
| Al bajar al gratuito | ✓ | ✓ | ✓ |

~~El job crea el link de renovación con el primer aviso (3 días antes, o 15 en el anual) y con el mismo paquete del último pago.~~ *(Cambio 2026-11-21: el job **no** crea links. El correo lleva a «Mi plan», que avisa que toca renovar y marca el paquete del último pago. El link de Pey se genera solo cuando el vendedor elige un paquete, para no crear links en vano si no renueva. Un link pendiente del mismo paquete y monto se reutiliza solo si tiene a lo más `GraceDays` días. Un link generado y sin pagar no se muestra como «pago pendiente».)*

## 5. Límites del gratuito
- **Cobros concretados**: ventas con `PaidAt` dentro del mes calendario, en hora de México. En el QR fijo cuenta cada pago, porque cada uno crea su venta.
- **Contador visible** en Inicio, en Cobrar y en «Mi plan»: «82 de 100 cobros este mes».
- **Avisos**:
  - en 80, aviso en el sitio y correo;
  - en 100, «Ya no puedes generar cobros hasta el 1 de diciembre o hasta que pagues».
- **Bloqueo**: con 100 concretados no se generan cobros nuevos (no hay cobros de cortesía; se quitaron el 2026-11-20), ni por QR ni por celular. Un QR o un push que ya se generó se puede pagar y se registra, porque el dinero va directo a su CLABE.
- **Tope técnico**: 300 cobros generados al mes en el gratuito, aunque no se paguen, para frenar abusos.
- **Uso justo de Pro**: 5,000 cobros concretados al mes. Al pasarlos se alerta al admin, sin bloquear. El número va en los Términos.
- **Catálogo**: solo lectura. Se combina `Merchant.CanManageProducts` con el plan. Se muestra «Tu catálogo de 63 productos te espera».
- **Carrito**: la pestaña «Productos» de Cobrar muestra el aviso de Pro, y `CreateCartSaleAsync` también lo valida en el servidor.
- **Recibo**: sin botones de WhatsApp ni «Compartir»; solo «Copiar liga». Las ligas que ya se enviaron siguen funcionando.
- **Historial**: últimos 30 días con el filtro de estatus. Lo anterior se oculta, no se borra: «Tienes 47 ventas de hace más de 30 días». El detalle de una venta más vieja abre con el aviso de Pro, no con un 404.
- **Ventas pendientes de Pro**: una venta con carrito creada antes de bajar al gratuito se puede seguir pagando.
- Todos los números viven en `SystemSettings` y se editan desde el admin (Fase 6).

## 6. Afiliados
- **Código**: uno por vendedor, de 6 caracteres sin letras confusas (sin 0, O, 1 ni I), único. La liga `/r/{código}` lleva al registro con el código ya escrito; una cookie de 30 días lo conserva también si se registra con Google o Facebook. El registro tiene el campo opcional «Código de invitación».
- El código solo se captura al registrarse; no se agrega ni se cambia después.
- **Referido**: en su primer pago de suscripción recibe 30 % de descuento sobre el precio de un mes, en el paquete que elija: $59.70 menos sobre $199, o $44.70 sobre $149 si es fundador. El descuento se aplica al crear el link y sigue disponible hasta que pague alguno.
- **Referidor**: cuando su referido paga por primera vez, gana 10 días de Pro (periodo `ReferralReward`). Tiene un máximo de 12 premios en 12 meses móviles. Los referidos que pasen del tope se registran sin premio y se le avisa.
- **Contra el abuso**:
  - el premio solo llega con un pago real;
  - la CLABE única impide que dos cuentas tengan la misma cuenta bancaria;
  - cada vendedor solo puede ser referido una vez.
- *(5.8, decisiones del usuario del 2026-11-23)* Un código que no existe da error en el campo y no crea la cuenta. Si quien invita sigue en el onboarding (sin suscripción) cuando su referido paga, el premio queda pendiente y se le da al empezar su prueba, después de ella. Detalle en la [bitácora](../log_plan/fase-5.8-afiliados.md).
- **Pantalla «Invita y gana»** (`/app/invita`): código, liga, botón de WhatsApp (`wa.me` con mensaje), premios ganados y referidos que aún no pagan.
- **Admin** (Fase 6): referidos de cada vendedor y anulación de un premio.

## 7. Lo que hace atractivo pagar
- **Lo que te pierdes**, en el gratuito: «Tienes 47 ventas de hace más de 30 días ocultas», «Tu catálogo de 63 productos te espera» y el aviso de Pro en el carrito y en WhatsApp.
- **Resumen mensual**, el día 1, por correo y en el sitio, para todos: «En noviembre cobraste $18,400 en 212 ventas. Con una terminal habrías pagado unos $747 de comisión; tu plan cuesta $199». La tasa de referencia (3.5 % + IVA) está en `SystemSettings` y el texto dice que es una estimación.
- **Recibo con marca**: en Pro lleva el logo del negocio (de «Mi información», 4.1). En el gratuito dice «Cobrado con Local133», con liga a la portada que incluye el código de afiliado del vendedor, así que cada recibo le puede dar un premio.
- **Catálogo público** (`/tienda/{slug}`):
  - nombre, logo y foto del local; productos activos con precio y foto; botón de WhatsApp al teléfono del negocio;
  - solo en Pro: el vendedor lo publica y elige el slug;
  - en el gratuito, la liga muestra «Catálogo no disponible por ahora»;
  - sin pedidos ni pagos en línea;
  - las fotos se sirven por una ruta pública nueva.
- **Precio de fundador**: los primeros 100 vendedores que paguen Pro pagan $149 al mes o $1,490 al año, de por vida. La página de precios muestra los lugares que quedan.
- **Precio congelado del anual**: quien renueva el anual sin bajar al gratuito paga lo mismo que la vez anterior, aunque el precio haya subido.

## 8. Retiro del modelo de comisión
Los pasos, archivo por archivo, están en la [subfase 5.1 del plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas). Lo que **se conserva** y por qué:
- `Payments` (pagos de ventas): sostiene el historial, el recibo y la conciliación. Solo pierde `CommissionRate`, `CommissionAmount` y `StatementId`.
- `SystemSettings`: se quedan `SaleQrExpirationSeconds`, `GraceDays` (pasa a 5 y ahora es la gracia de la suscripción) y `VatRate` (desglose del IVA incluido y CFDI futuro). Solo se va `CommissionRate`.
- `GatewayTransaction.ApplicationFee`: es la comisión de Pey, no la de la plataforma.
- `SaleMetadata.StaticKind`: el QR fijo (2.7) sigue en el plan.

## 9. Modelo de datos

| Tabla | Campos principales | Notas |
|---|---|---|
| `Subscriptions` | `MerchantId` (único), `TrialStartedAt`, `ProUntil`, `LastPeriodKind`, `LastPackage`, `IsFounder`, `FounderNumber`, `LockedAnnualPrice` | Una por vendedor. `ProUntil` es el máximo de `SubscriptionPeriods.EndsAt`, guardado para consultar rápido |
| `SubscriptionPeriods` | `MerchantId`, `Kind` (`Trial`, `Paid`, `ReferralReward`, `AdminGrant`), `StartsAt`, `EndsAt`, `SubscriptionChargeId`, `ReferralId`, `CreatedAt` | Historia de cada día de Pro |
| `SubscriptionCharges` | `MerchantId`, `Package`, `Months`, `ListPrice`, `DiscountAmount`, `DiscountReason`, `Amount`, `PeyPaymentLinkId` (único), `Url`, `Status`, `Origin` (renovación o vendedor), `CreatedAt`, `PaidAt` | Un link por cargo |
| `SubscriptionPayments` | `SubscriptionChargeId`, `PeyTransactionId` (único), `PaymentMethod`, `Amount`, `Base`, `Fee`, `ApplicationFee`, `PaidAt` | Lo que reporta Pey |
| `Referrals` | `ReferrerMerchantId`, `ReferredMerchantId` (único), `Code`, `RegisteredAt`, `QualifiedAt`, `RewardedAt`, `SkippedReason` | *(5.8: `RewardedAt` en lugar de `RewardPeriodId`; el periodo apunta al referido con `SubscriptionPeriods.ReferralId`, único)* |
| `Notifications` | `MerchantId`, `Type`, `Title`, `Body`, `Url`, `CreatedAt`, `ReadAt`, `DedupKey` (único por vendedor) | Avisos en el sitio. `DedupKey` hace idempotente al job |
| `Merchants` | Nuevos: `ReferralCode` (único), `PublicSlug` (único, opcional), `PublicCatalogEnabled` | |
| `BankAccounts` | Índice único filtrado de `Clabe` (`CodiStatus <> 'Error'`) | 5.2 |
| `SystemSettings` | Nuevos: `MonthlyPrice` (199), `AnnualPrice` (1990), `FounderMonthlyPrice` (149), `FounderAnnualPrice` (1490), `FounderSlots` (100), `TrialDays` (30), `FreeMonthlyPaidLimit` (100), `FreeMonthlyGeneratedLimit` (300), `FreeHistoryDays` (30), `ProFairUseMonthlyPaid` (5000), `ReferralRewardDays` (10), `ReferralRewardsPerYear` (12), `ReferralWelcomeDiscountRate` (0.30), `ReferenceTerminalFeeRate` (0.035). `GraceDays` pasa a 5 | Cambios auditados (Fase 6) |

- Las tablas con `MerchantId` llevan el filtro global de EF Core (`OnModelCreating`). El webhook y los jobs leen sin alcance (`SystemTenantContext`).
- Orden de creación:
  - la 5.3 creó `Subscriptions`, `SubscriptionPeriods` (sin `SubscriptionChargeId` ni `ReferralId`) y todos los campos nuevos de `SystemSettings`. `TrialStartedAt` no es nula, porque la suscripción nace con la prueba;
  - `SubscriptionCharges`, `SubscriptionPayments` y `SubscriptionPeriods.SubscriptionChargeId` llegan en la 5.6;
  - `Referrals`, `SubscriptionPeriods.ReferralId` y los campos de afiliados de `Merchants` llegan en la 5.8;
  - `Notifications` llega en la 5.4.
- Los importes van en `decimal(18,2)` y las tasas en `decimal(5,4)`, como hoy.

## 10. Detalle de las subfases
Cada subfase que toca vistas pasa la revisión de Playwright a 375, 768 y 1280 px (CLAUDE.md) y actualiza `azure.md` al terminar.

### 5.1 Retirar el modelo de comisión
Los pasos están en el plan. Para el vendedor solo cambian dos cosas: desaparece la comisión del detalle de venta y cambian los textos legales.

### 5.2 CLABE única
- Antes de llamar a Pey, `BankAccountRegistration` revisa si otro vendedor tiene la misma CLABE en una cuenta que no esté en `Error`. Si la tiene, devuelve un error en el campo y **no llama a Pey**: «Esta CLABE ya está registrada en otra cuenta de Local133. Si es tuya, contáctanos para aclararlo».
- Índice único filtrado `IX_BankAccounts_Clabe` (`WHERE [CodiStatus] <> 'Error'`) como última defensa ante dos altas simultáneas. Una cuenta rechazada no aparta la CLABE; la de un vendedor dado de baja sí.
- **Migración**: si hay CLABEs repetidas, se detiene con un `THROW` y un mensaje claro, en lugar de fallar con un error críptico al arrancar. La CLABE de prueba `012180015826225713` seguramente está repetida en la base local y en la de Azure: depurarla antes.
- Desde aquí, la CLABE de prueba solo la puede usar un vendedor por base. Las pruebas de integración usan CLABEs válidas distintas.
- El cambio de cuenta de la 4.2 sigue la misma regla.
- En el admin (Fase 6): ver qué vendedor tiene una CLABE y liberarla tras una aclaración.
- **Pruebas**: otro vendedor con la misma CLABE (error, sin llamada a Pey), la misma CLABE después de un `Error` (permitido), el mismo vendedor que reintenta la suya (permitido) y dos altas simultáneas.

### 5.3 Planes y vigencia
- `Domain/Subscriptions`:
  - enums `PlanTier`, `SubscriptionPackage`, `SubscriptionPeriodKind` y `SubscriptionChargeStatus`;
  - `SubscriptionPeriod` y el cálculo del estado (`PlanStatus.For(periodos, ahora, ajustes)` ⇒ estado, `ProUntil` y fin de la gracia);
  - la regla del inicio del periodo siguiente (§3);
  - `PlanEntitlements.For(estado)`, con lo que permite cada estado;
  - `SubscriptionPricing`, con precios por paquete, fundador, congelado y descuento.
- La prueba se crea cuando la cuenta pasa a `Active`, una sola vez por vendedor y en el mismo guardado que el cambio de estatus: en `BankAccountRegistration` si Pey la activa de inmediato, y en `BankAccountStatusSync` para el webhook, el sondeo y el botón.
- `IPlanAccess` (Application) da el estado del vendedor actual, con caché por petición.
- Migración `Subscriptions`: tablas, `SystemSettings` nuevos y prueba para los vendedores activos (§3).
- **Demostrable**: Inicio muestra «Prueba: te quedan 23 días» o el estado que corresponda.
- **Pruebas unitarias**: fin de mes (31 → 28 o 29), gracia (día 5 contra día 6), pago en la gracia, pago después de la gracia, pago en la prueba, anual sobre mensual, premios encadenados y medianoche de México contra UTC.

### 5.4 Avisos en el sitio y correos
- Tabla `Notifications`, campana con el número de no leídos en `_MerchantLayout`, lista en `/app/avisos` y banner para lo urgente (gracia, límite alcanzado).
- `INotificationService.NotifyAsync(...)`: idempotente por `DedupKey`, con correo opcional por tipo vía `IEmailSender`.
- Los jobs también mandan correos y no tienen petición HTTP. Por eso las plantillas van donde el job las pueda usar, y las ligas absolutas salen de `App:PublicBaseUrl`, que se vuelve obligatoria fuera de desarrollo.
- **Pruebas**: idempotencia, avisos aislados entre vendedores y correos en `InMemoryEmailSender`.

### 5.5 Límites del gratuito
- Aplicar §5 en el servidor, no solo en la vista:
  - `CreateDirectSaleAsync` y `CreateCartSaleAsync` devuelven resultados nuevos (`PlanRequired`, `MonthlyLimitReached`, `GenerationLimitReached`);
  - `ProductCatalog` respeta la solo lectura;
  - `SalesHistoryQuery` recorta a 30 días y cuenta las ventas ocultas;
  - el detalle de venta y `_ReceiptShare` se ajustan al plan.
- Contador del mes en Inicio y en Cobrar. Los avisos de 80 y 100 se disparan después de registrar el pago (`PaymentRecorder`); si el aviso falla, el pago ya quedó guardado.
- Componentes reutilizables «Disponible con Pro» y «Próximamente».
- **Pruebas de integración** de cada bloqueo y de los bordes de mes.

### 5.6 Mi plan y pago con link de Pey
- Reutilizar el backend de la PoC. Borrar `PaymentLinkPocController`, sus modelos y vistas, la liga de «Más» y `PaymentLinkPocTests`. La bitácora de la PoC se queda como historia.
- `/app/plan` con paquetes y precios, `/app/plan/pago/{id}` con la confirmación, y `GET /api/subscription-charges/{id}` para la consulta cada 3 s.
- `ISubscriptionCheckout` crea o reutiliza el cargo y el link. `ISubscriptionPaymentRecorder` registra los pagos de forma idempotente y abona el periodo.
- El webhook deja de marcar `payment_link.status_updated` como `Ignored`. Su contrato va en `PeyWebhookContracts`.
- Fundador: con el primer pago, si quedan lugares, se le asigna su número dentro de una transacción, para no pasar de 100. *(5.6, confirmado por el usuario: el cargo sale a precio de fundador si quedan lugares al crear el link; si al pagar ya no hay lugar, se abona igual sin número.)*
- Job `conciliar-links`. *(El `barrido-links` se pospuso el 2026-11-20.)*
- *(5.6)* Solo paga quien ya empezó la prueba (tiene suscripción) y no está dado de baja. Al abonar se manda el correo «Recibimos tu pago»; el aviso en el sitio llega con la 5.4. Detalle en la [bitácora](../log_plan/fase-5.6-mi-plan-y-pago.md).
- **Pruebas**:
  - WireMock (ya están las de la PoC);
  - webhook firmado: `paid`, `processing`, `partial`, repetido, link viejo, dos links pagados y monto menor;
  - conciliación;
  - unitarias de precios.
- **Prueba real** con $0.01 y CoDi, con confirmación del usuario.

### 5.7 Recordatorios, gracia y baja al gratuito
- Job `suscripciones-diario` a las 9:00, hora de México:
  - crea los links de renovación y manda los recordatorios de §4;
  - avisa del fin de la prueba y de la baja al gratuito;
  - el día 1 manda el resumen mensual (su contenido llega en la 5.10).

  Es idempotente por `DedupKey`.
- La baja al gratuito es un cálculo: el job solo avisa, no cambia ningún estatus.
- Banner de gracia con el link y la fecha límite.
- **Pruebas** con reloj falso (`TimeProvider`) en cada borde.
- *(5.7, hecho 2026-11-21)*:
  - cada recordatorio es una **ventana** de días y no un día exacto: si el job no corre, sale al día siguiente con los días reales, y pasada la ventana no se manda;
  - la llave es `sub:{etapa}:{ProUntil}`, así que un pago empieza otro ciclo;
  - el correo lleva a «Mi plan», no a Pey. Ajuste del mismo día: el job no crea links, y el banner de gracia y «Mi plan» no muestran links pendientes (ver §4);
  - se adelantó la base de la 5.4 (tabla `Notifications` e `INotificationService`), sin la campana ni la lista.

  Detalle en la [bitácora](../log_plan/fase-5.7-recordatorios-y-gracia.md).

### 5.8 Afiliados
- Aplicar §6:
  - código al registrarse, y para los vendedores existentes en la migración;
  - `/r/{código}` y la cookie;
  - campo en `Register` y en `ExternalLogin`, a través de `AccountRegistration`;
  - descuento al crear el link;
  - premio al confirmarse el primer pago;
  - `/app/invita`.
- **Pruebas**: código inválido, premio solo con pago, tope de 12, gratuito que refiere y descuento solo en el primer pago.

### 5.9 Pro de lanzamiento
- **Historial**:
  - rango de fechas libre y búsqueda por folio o concepto;
  - exportación a Excel en `/app/ventas/exportar`, con los mismos filtros;
  - para el `.xlsx`, una librería sin vulnerabilidades conocidas (p. ej. ClosedXML), o CSV en UTF-8 si no conviene la dependencia.
- **Corte de caja diario** (`/app/ventas/corte?fecha=`):
  - total cobrado y número de ventas;
  - desglose por método (QR o celular) y por origen (monto o carrito);
  - ticket promedio y productos vendidos;
  - pendientes y vencidas;
  - vista para imprimir y para compartir por WhatsApp.

  Cuando haya vendedores adicionales, también por vendedor. Sale de `DaySummaryQuery` y `SalesHistoryQuery`.
- *(5.9, hecho 2026-11-23)*:
  - el `.xlsx` se arma con `DocumentFormat.OpenXml` (Microsoft, firmado), con un tope de 50,000 ventas por archivo;
  - la búsqueda es por folio («#12») o por el concepto del vendedor, sin el nombre del negocio, y no distingue acentos;
  - el corte cuenta lo cobrado por el día del pago, y las pendientes y vencidas por el día en que se creó la venta;
  - en el gratuito, la exportación responde 403 y el corte muestra el aviso de Pro.

  Detalle en la [bitácora](../log_plan/fase-5.9-pro-de-lanzamiento.md).

### 5.10 Valor visible
- Recibo con marca y catálogo público (§7). Los dos dependen del logo y la foto de la 4.1.
- «Lo que te pierdes» en el historial, el catálogo y Cobrar.
- Contenido del resumen mensual, que manda el job de la 5.7.
- Catálogo público: slug único, `noindex` mientras el vendedor no lo publique y fotos por una ruta pública.
- *(5.10, hecho 2026-11-24)*:
  - la marca del recibo sigue el plan **al abrir la liga** (decisión del usuario);
  - «Cobrado con Local133» lleva a la portada con `?codigo=`, y su botón a `/r/{código}`;
  - el resumen mensual va solo a quien tuvo cobros en el mes y no está dado de baja, con la llave `summary:{yyyy-MM}`;
  - el catálogo se publica en `/app/tienda` («Catálogo en línea»). Publicado y en Pro se puede indexar; si no, «Catálogo no disponible por ahora» con `noindex`. Su pie también lleva el código del vendedor.

  Detalle en la [bitácora](../log_plan/fase-5.10-valor-visible.md).

### 5.11 Precios y textos legales
- **Portada**: sección «Planes» con la tabla de §2, «Prueba todo gratis 30 días», lugares de fundador y lo que está «Próximamente».
- **Términos**: planes, prueba, límites y uso justo (con número), pago con link, gracia, baja al gratuito, referidos, fundador, precio congelado, cancelación y reembolsos.
- **Aviso de privacidad**: datos de referidos, pagos de la suscripción y avisos.
- Subir las versiones en `LegalDocuments` y pasar los textos por revisión legal.

## 11. Por confirmar
**Con el usuario** (este documento ya usa el valor por defecto que se indica)
1. **Fundador**: los «primeros 100» son los que pagan, no los que se registran. El anual de fundador cuesta $1,490. Lo conserva aunque deje de pagar un tiempo («de por vida»).
2. **Recibo en el gratuito**: solo «Copiar liga», sin botones de WhatsApp ni «Compartir».
3. **Historial en el gratuito**: conserva el filtro de estatus; el rango de fechas, la búsqueda y la exportación son de Pro.
4. **Números**: tope de 300 cobros generados al mes en el gratuito, uso justo de 5,000 al mes en Pro y tasa de referencia de terminal de 3.5 % + IVA.
5. **Anual**: recordatorio también 15 días antes.
6. **Cancelación y reembolsos**: no se reembolsan periodos pagados y se puede dejar de pagar cuando sea.
7. **Datos de cuentas que no vuelven**: cuánto tiempo se guardan (va en el aviso de privacidad).
8. **Términos nuevos**: si se pide aceptarlos de nuevo a los usuarios existentes. Hoy solo se guarda la versión al registrarse.

**Con soporte de Pey**
1. Con tarjeta, ¿el `fee` lo paga el pagador encima del monto? ¿Qué es `applicationFee` y se descuenta a la plataforma? ¿Un link puede ofrecer solo CoDi? Si el pagador paga una comisión con tarjeta, el precio «IVA incluido» sube con tarjeta, y hay que decirlo en «Mi plan».
2. ¿Qué significa `partial`? ¿Un link pagado acepta otro pago?
3. ¿Los links expiran o se pueden desactivar? ¿Qué política de reintentos tiene `payment_link.status_updated`?
4. ¿Pey deja registrar la misma CLABE dos veces con la misma API key?
5. Pendientes de la PoC: qué concepto ve el pagador y el monto mínimo con tarjeta.

## 12. Fuera de esta implementación
- CFDI de la suscripción, timbrado con un PAC.
- Vendedores adicionales, QR impreso del puesto (2.7, sigue en el plan), marco de QR por producto y reportes: se muestran como «Próximamente».
- Recordatorios por WhatsApp: requieren WhatsApp Business API, que es de pago y pide plantillas aprobadas.
- Inventario con alerta de stock, descuentos y promociones en el carrito, kit físico de bienvenida y soporte prioritario por WhatsApp.
- Links de pago para que el vendedor cobre con tarjeta: el link no acepta `bankAccountId` y el dinero caería en la plataforma. Solo se retoma si Pey lo agrega.

## 13. Azure
- `App__PublicBaseUrl`: obligatoria y con HTTPS, porque la usan los links de Pey y las ligas de los correos de los jobs.
- El webhook es el mismo; ahora también procesa `payment_link.status_updated`.
- Jobs nuevos: `conciliar-links` (5.6) y `suscripciones-diario` (9:00, hora de México). El `barrido-links` nocturno se pospuso.
- Migraciones:
  - retiro de la comisión: borra columnas;
  - CLABE única: depurar antes las repetidas;
  - suscripciones, referidos y avisos.
- Correo: habrá más envíos (recordatorios y resúmenes). Revisar los límites diarios y mensuales del plan de Resend antes del piloto.

## 14. Verificación
- **Unitarias**:
  - vigencia: prueba, gracia, periodos encadenados, fin de mes y zona horaria;
  - precios: paquetes, fundador, congelado y descuento;
  - límite mensual: bordes de mes y aviso al 80 %;
  - tope de premios;
  - lo que permite cada estado.
- **Integración**:
  - webhook de links firmado;
  - conciliación;
  - bloqueos del gratuito en el servidor;
  - aislamiento entre vendedores;
  - CLABE única;
  - registro con código.
- **Playwright** a 375, 768 y 1280 px de:
  - «Mi plan», el pago hasta antes de Pey y la confirmación;
  - campana y avisos;
  - Inicio y Cobrar con el contador;
  - catálogo de solo lectura, carrito bloqueado e historial de 30 días;
  - corte de caja, exportación e «Invita y gana»;
  - catálogo público, recibo con marca y planes en la portada.
- **Extremo a extremo con Pey real** (con confirmación):
  - con precio de $0.01, un link pagado con CoDi ⇒ webhook ⇒ Pro;
  - un referido paga ⇒ 10 días más al que lo refirió.
