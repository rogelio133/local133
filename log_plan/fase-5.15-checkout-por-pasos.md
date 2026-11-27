# Fase 5.15 — Checkout por pasos

**Estado:** ✅ hecho 2026-11-27, sin publicar: sale junto con la 5.12 a la 5.14 y la 5.16 a la 5.18 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [diseño](../actualizaciones/planes_basico_pro_2026_11_26.md#515-checkout-por-pasos)

## Alcance (del plan)
- Plan, duración y resumen del pedido (total, IVA y casilla de los Términos), sin llamar a Pey hasta «Pagar».
- Confirmación con número de pedido, comprobante imprimible y «Tus pedidos» en «Mi plan».
- Abono por plan, con conversión y encadenado.

**Demostrable:** un vendedor con Básico entra a «Mi plan» → «Cambiar a Pro» → Anual. El resumen dice «Tus 19 días de Básico se vuelven unos 9 días de Pro desde hoy», con precio de lista, precio de fundador, total e IVA. Sin la casilla no se puede pagar. Al abonarse el webhook, los días de Básico se convierten y el anual empieza después. La confirmación dice «¡Gracias! Tu pedido #123 está pagado» y lleva al comprobante.

## Decisiones (propias)
- **Todo en `PlanController`.** Así `_MerchantLayout` sigue sin pintar el banner del plan en «Mi plan» ni en el checkout, sin tocar el layout.
- **Slugs en el dominio** (`SubscriptionSlugs`) y **rutas en Application** (`SubscriptionPaths`). Las comparten el sitio (`PlanUrls`), el `backUrl` del link de Pey y el correo. Un slug con otra forma (`Pro`, `Annual`) da 404.
- **El resumen es un objeto de dominio** (`SubscriptionOrder.For`), probado en unitarias. Usa el mismo `SubscriptionOffers.For` (que gana `Starts`), así que «Del … al …» sale del mismo cálculo que el abono. Los días de la conversión se redondean hacia abajo («unos 9 días»).
- **«Precio de lista»** es el precio sin fundador ni anual congelado. El renglón de descuento dice «Precio de fundador» o «Tu precio anual congelado»; el diseño solo mencionaba el de fundador.
- **La revisión del total va en `StartAsync`** (`CheckoutResult.PriceChanged`), antes de reutilizar o crear nada. El total viaja en `Input.ExpectedTotal` (texto invariante, el patrón del carrito). Un total ilegible cuenta como precio cambiado.
- **Reutilizar un link también exige la misma versión de los Términos** (`CanBeReusedFor(…, termsVersion)`). Un link `Pending` de antes de la migración, sin versión, ya no se reutiliza: al pagar se crea otro, con la aceptación registrada.
- **Un POST con error vuelve a pintar el resumen** (200, en `/app/plan/checkout`) cuando el error es de la casilla o del precio, para mostrarlo en el campo o en el resumen. Los errores de Pey o de HTTPS redirigen al resumen con `TempData` (PRG), como antes en «Mi plan».
- **El asunto del correo sigue siendo constante**: «Recibimos tu pago · Local133», sin «de Pro». El número de pedido va en el cuerpo y en el preheader. Hay pruebas que comparan contra la constante.
- **Comprobante**: muestra solo lo guardado en el cargo. No hay «precio de lista normal» de los pedidos de fundador, porque no se guarda y recalcularlo con los precios de hoy podría mentir. «Compartir» y «Copiar» mandan el texto, no una liga, porque el comprobante pide sesión (igual que el corte de caja).
- **Confirmación**: al pasar a `Paid`, `subscription-payment.js` recarga la página (`data-reload-on-paid`) para que el servidor pinte el pedido con su periodo y la liga al comprobante.
- **Barra de pasos**: los pasos hechos son ligas solo hasta el resumen; en la confirmación ya no, porque el pedido existe. A menos de `sm` se ve «Paso 3 de 5 · Resumen». Entre `sm` y `lg` no cabían los cinco nombres a 768 px, así que solo se ve el del paso actual; los demás quedan para lectores de pantalla.
- **Entradas**: «Conoce Pro» (`_ProRequired` y Cobrar) lleva al paso 2 de Pro. «Elegir plan» y «Renovar» (vencido, gracia, Inicio y aviso de renovar) llevan a `PlanUrls.ChoosePlanFor(MerchantPlan)`: el resumen del último plan y paquete o, si nunca pagó, el paso 1. `MerchantPlan` gana `LastPlan` y `LastPackage`. Productos del vencido sigue en el paso 1, porque no tiene el plan a la mano. Los botones de los correos son de la 5.16.
- **Conversión al abonar**: después de asignar el fundador, con la proporción de fundador si el cargo salió a precio de fundador o si ya lo era (la misma que usa la vista previa).
- **Admin**: el abono a mano hereda la conversión. Su texto ya no dice «Pro hasta»: dice el plan del cargo y «vigente hasta».
- **`POST /app/plan/pagar` se borró.** Solo lo usaba el formulario de «Mi plan».

## Qué se hizo
- **Dominio**:
  - `SubscriptionSlugs` y `SubscriptionOrder` (`SubscriptionOrderTimeline`);
  - `SubscriptionOffer.Starts`/`FirstDay`;
  - `SubscriptionCharge.Create(…, termsVersion)` (sella `TermsAcceptedAt`) y `CanBeReusedFor(…, termsVersion)`.
- **Application**:
  - `SubscriptionPaths`;
  - `ISubscriptionCheckout.GetOffersAsync(merchantId, plan?)` y `GetOrderAsync`, con `CheckoutOrderResult`;
  - `StartCheckoutCommand(…, Plan, …, TermsVersion, ExpectedTotal)` y `CheckoutResult.PriceChanged`;
  - `SubscriptionPaymentCredited` con plan, periodo, `StartsLater` y conversión;
  - `MerchantPlan.LastPlan`/`LastPackage`.
- **Infraestructura**:
  - `SubscriptionCheckout` por plan, con la revisión del total, los Términos y el `backUrl` al resumen;
  - `SubscriptionCrediting` con `ConvertBasicToPro`;
  - `PlanAccess` con el último pedido;
  - `MerchantEmails.SubscriptionPaidBody` como confirmación del pedido;
  - el texto del abono a mano.
- **Web**:
  - `PlanController`: «Mi plan», los pasos 1 a 3, el POST, la confirmación y el comprobante;
  - `PlanViewModels`: `PlanUrls`, `MyPlanViewModel.ActionsFor`, `OrderRow`, `OrderPriceRow`, `SubscriptionPaymentViewModel` y `SubscriptionReceiptViewModel`;
  - `CheckoutViewModels`: pasos, `CheckoutInput`, `CheckoutMessages` y los view models de los tres pasos;
  - vistas `Index`, `ChoosePlan`, `ChooseDuration`, `Summary`, `Payment` y `Receipt`, y el parcial `_CheckoutSteps`;
  - el banner, `_ProRequired` y `_PlanExpired` con sus ligas nuevas;
  - `subscription-payment.js`;
  - estilos `.checkout-step*`.

## Pruebas
- **Unitarias** (761, en verde):
  - `SubscriptionSlugsTests`;
  - `SubscriptionOrderTests`: el ejemplo de §3 («unos 9 días», inicio el 10/dic a las 08:47:38), fundador ($500 y $205.52 de IVA), Básico en la prueba y con Pro, vencido, la gracia de Básico sin conversión, bienvenida por plan y anual congelado;
  - `SubscriptionChargeTests`: los Términos en el cargo y en la reutilización, y una versión inválida.
- **Integración** (922, en verde):
  - **`CheckoutTests`** (nuevo):
    - ningún GET (Mi plan y los pasos 1 a 3 de los dos planes y sus cuatro paquetes) llama a Pey ni crea cargos;
    - el paso 1 y el resumen con sus textos;
    - la conversión en el resumen y «Cambiar a Pro»;
    - un slug inválido da 404;
    - onboarding y dado de baja sin botón;
    - sin casilla, con el precio cambiado (y con los lugares de fundador agotados entre el resumen y el pago) no se llama a Pey;
    - se reutiliza el link del mismo plan y con otro plan se crea otro, con su `backUrl`;
    - el comprobante con sus datos;
    - 404 si no está pagado o es de otro vendedor.
  - **`SubscriptionWebhookTests`**:
    - Pro anual pagado con Básico por delante ⇒ periodo `Conversion` con 69/149 y el anual después, congelado por plan;
    - Básico pagado durante Pro ⇒ se encadena;
    - Básico pagado durante la prueba ⇒ sigue en `Trial`;
    - un cargo sin versión de Términos se abona igual;
    - el correo trae el pedido y «Ver comprobante» como primera liga.
  - **Al día**:
    - `MyPlanTests`: duraciones, `BackUrl`, Términos en el cargo, 404 del plan o paquete desconocido, confirmación como pedido y «Tus pedidos»;
    - `PlanBannerTests`: las ligas al resumen del último pedido;
    - `ReferralTests`: el descuento en el paso 2;
    - `CustomerPlanTests`: el asunto del correo.
- **Portada**: 61, en verde. `dotnet build` sin advertencias; `has-pending-model-changes` sin cambios (no hay migración).

## Revisión en navegador
- **Montaje**: Playwright a 375, 768 y 1280 px. El sitio corrió en `http://localhost:5299`, compilado a una carpeta aparte, porque Visual Studio tenía el sitio en 5231/7180 y bloqueadas las DLL de `bin`. Arrancó con `Resend__ApiKey=`, `Pey__ApiKey=`, `Jobs__Enabled=false` y la base local, **sin llamar a Pey**.
- **Vendedor de prueba** `plan515@prueba.mx` (Merchant 56): registrado en el sitio y confirmado por SQL. Por SQL: activo, Básico pagado con 20 días por delante y un pedido pagado (#1008) para la confirmación y el comprobante. Después, vencido.
- **Lo revisado**:
  - «Mi plan»: botones «Renovar igual que la última vez», «Cambiar a Pro» y «Elegir plan», y «Tus pedidos» con «Comprobante»;
  - los pasos 1 a 3 con la barra, «Cambiar plan» y «Cambiar duración», y el botón «atrás» y «adelante» del navegador (resumen → duración → planes);
  - la casilla sin marcar: el error sale en el cliente y el botón no se queda bloqueado;
  - con la casilla, el POST por http vuelve al resumen con el aviso de HTTPS y **no crea cargo**;
  - el precio cambiado: se subió por SQL el anual de fundador a $1,500 entre el GET y el POST. El resumen se volvió a pintar con «El precio de tu pedido cambió…» y $1,500, sin cargo. Después se restableció a $1,490;
  - la confirmación pagada y el comprobante, también en vista de impresión: solo el ticket, con $69 = $59.48 + $9.52;
  - el vencido: Inicio, el banner de «Más» y «Mi plan» llevan a `/app/plan/checkout/basico/1-mes`. «Conoce Pro» de Productos (Básico) lleva a `/app/plan/checkout/pro`.
- **Lo que se corrigió durante la revisión**:
  - la barra de pasos no tenía estilos;
  - a 768 px los nombres se encimaban; ahora entre `sm` y `lg` solo se ve el del paso actual.
- **Comprobaciones generales**: en las 10 pantallas y los tres tamaños, sin scroll horizontal, la pestaña activa correcta («Más»; «Cobrar» en Cobrar), nada tapado por la barra inferior, la consola sin errores y ningún recurso con 400 o más.
- **Datos de esta base**: el Pro de fundador vale $0.01 (de las pruebas con Pey), así que la proporción de conversión da 1 y el paso 2 muestra precios de centavos.
- Capturas en `.playwright-mcp/fase515-*.png` (fuera de git).

## Pendientes
- ~~**Compilación normal**~~ hecha el 2026-11-27, con Visual Studio detenido:
  - `dotnet build` sin advertencias;
  - `has-pending-model-changes`: «No changes have been made to the model since the last migration»;
  - `dotnet test`: 761 unitarias, 922 de integración y 61 de la portada, todas en verde. Las fallas de antes (`El_logo_existe_en_wwwroot` y `SharedAssetsTests`) eran por compilar fuera del repo.
- **Base local**: quedan el vendedor `plan515@prueba.mx` (Merchant 56, vencido, con el pedido pagado #1008, que no pasó por Pey) y `SystemSettings` restablecido.
- **Prueba real con Pey** ($0.01, con confirmación del usuario), parte de la verificación de extremo a extremo de los planes:
  - pagar Básico durante la prueba;
  - subir a Pro y ver la conversión por el webhook real;
  - ver que «Regresar» de Pey vuelve al resumen.
- **Por confirmar con Pey** (§13 del diseño): si con tarjeta se suma una comisión al total. El resumen ya dice «con tarjeta, Pey puede sumar una comisión al precio».
- Lo demás está asignado a su subfase:
  - botones de los correos al checkout y las etapas `Expired`/`ExpiredFollowUp` (5.16);
  - selector de plan al regalar días y pestaña Plan del admin con el plan de cada cargo (5.17);
  - Términos nuevos y su versión (5.18).

## Ajuste 2026-11-27: checkout simplificado
Lo pidió el usuario después de la revisión de la 5.15. Decisiones del usuario:
- plan y duración en **una sola página**;
- se conserva «Cambiar a Pro»;
- **no** se tocan la portada ni los Términos, que todavía mencionan los paquetes de 3 y 6 meses.

### Qué se hizo
- **Solo 1 mes y Anual**:
  - `SubscriptionPackages.Offered` y `IsOffered()`. `SubscriptionOffers.For` solo arma esos dos y pierde `AnnualUpgradeDifference`, que era del de 6 meses;
  - `SubscriptionOrder.For` y `SubscriptionCheckout` lanzan con 3 o 6 meses;
  - `ThreeMonths`/`SixMonths` se quedan en el enum por los cargos y periodos que ya existen. Un link viejo de esos paquetes se sigue abonando.
- **Paso 1 = plan + duración**: `/app/plan/checkout` tiene dos secciones, `#basico` y `#pro` (`CheckoutPlanSectionViewModel`), cada una con sus tarjetas de 1 mes y Anual:
  - las tarjetas dicen «Básico (1 mes)», «Básico Anual», «Pro (1 mes)» y «Pro Anual»;
  - el anual dice «12 meses por el precio de 10» (`SubscriptionOffer.PaidMonths`, con precios de lista) y «Equivale a $X al mes».
- **Barra**: Plan ─ Resumen ─ Pago ─ Listo (4 pasos).
- **Rutas viejas y paquetes retirados**: `/app/plan/checkout/{plan}`, `/app/plan/checkout/{plan}/3-meses|6-meses` y el POST con esos paquetes redirigen a `/app/plan/checkout#{plan}`, sin cargo ni Pey. `PlanUrls.ChoosePlanFor` lleva a la sección del plan si el último paquete fue de 3 o 6 meses.
- **Resumen**:
  - título «Pro Anual»;
  - en el anual, «Estás obteniendo 12 meses por el precio de 10» (`data-order-annual-deal`);
  - «← Planes» y un solo «Cambiar», que lleva a la sección del plan.
- **«Mi plan»**: `ActionsFor(plan, isRenewal)`:
  - «Renovar mi plan Básico/Pro» en la ventana del aviso de renovar (15 días antes del anual, 3 días antes, el día y la gracia) o si está vencido;
  - si no, «Adquirir más días de Básico/Pro»;
  - más «Cambiar a Pro» si tiene Básico;
  - todos van a la sección de su plan;
  - se quitaron «Elegir plan» y «Renovar igual que la última vez».
- **Encontrado en la revisión y corregido**:
  - el aviso de renovar decía «Tu plan **Pro** vence…» aunque el plan fuera Básico. Ahora `RenewalNoticeViewModel.From(aviso, plan, hoy)` usa el nombre del plan;
  - el aviso traía su propio «Renovar», repetido justo encima de «Renovar mi plan …». Se quitó (`RenewalUrl` ya no existe);
  - a 768 px, «Equivale a … al mes» del anual sin destacado quedaba pegado al precio y se partía. Ahora va en su renglón (`IsPerMonthInline`).
- **Mayúsculas**: los textos de planes ya usaban «Básico» y «Pro». Los nuevos siguen esa forma, con acento como en el resto del sitio.

### Pruebas
- Unitarias:
  - las ofertas son solo `OneMonth` y `Annual`;
  - `PaidMonths == 10` con precio normal, de fundador y con descuento de bienvenida;
  - `SubscriptionOrder.For` rechaza 3 y 6 meses.
- Integración:
  - los botones de «Mi plan» por caso: Básico a media vigencia, Básico y Pro a 2 días de vencer, Pro lejos de vencer, prueba con Pro pagado, gracia y vencido;
  - las 4 tarjetas, las anclas, «12 meses por el precio de 10» y sin `3-meses`/`6-meses`;
  - el resumen del anual con el destacado y el de 1 mes sin él;
  - las redirecciones;
  - un POST de 3 meses no crea cargo ni llama a Pey.
- Resultado: `dotnet test -c Release` con 763 unitarias, 928 de integración y 61 de la portada en verde. Después, las 145 de suscripciones e Inicio también en verde. Se corrió en Release porque Visual Studio tenía bloqueadas las DLL de `src/Local133.Web/bin/Debug`.

### Revisión en navegador
- **Montaje**: el build de Release en `http://localhost:5232`, con `Pey__ApiKey=` y `Resend__ApiKey=` vacías.
- **Vendedor**: `plan514@prueba.mx` (Merchant 55, Básico hasta el 22 de diciembre). Su contraseña se restableció con «Olvidé mi contraseña» (correo en `App_Data/emails`) a `Prueba.1234!`.
- **Lo revisado en 375, 768 y 1280 px**:
  - «Mi plan»: «Adquirir más días de Básico» + «Cambiar a Pro»;
  - el scroll a `#basico` y `#pro`: el título no queda tapado por la barra superior;
  - las tarjetas;
  - el resumen del Básico Anual con «Estás obteniendo 12 meses por el precio de 10» y el de Pro (1 mes) sin él;
  - la casilla de los Términos validada en el cliente;
  - «Pagar» no queda tapado por la barra inferior;
  - la liga vieja `/app/plan/checkout/pro/3-meses` lleva a `#pro`.
- **Ventana de renovar**: se movió por SQL el fin del Básico a 2 días. Salieron «Tu plan Básico vence en 2 días» y «Renovar mi plan Básico». Después se regresó el periodo 1048 a su fecha original.
- **Comprobaciones generales**: sin scroll horizontal y consola sin errores ni advertencias.
- **Dato de esta base**: el Pro mensual de fundador vale $0.01, así que el Pro Anual no muestra «12 por 10» (no hay meses de regalo con esos precios). Es correcto.

### Pendientes
- **Portada y Términos** (decisión del usuario: no se tocan aquí): la nota de precios y la pregunta frecuente de la portada, y los Términos, todavía mencionan los paquetes de 3 y 6 meses. Encaja con la 5.18 (portada y textos legales).
- **Correos de recordatorio y avisos de Inicio** (`MerchantEmails.SubscriptionReminder`) siguen diciendo «Tu plan Pro vence…» aunque el plan sea Básico. Toca en la 5.16 (recordatorios), junto con sus botones al checkout.
- **Compilación en Debug y `has-pending-model-changes`**: no se corrieron, porque Visual Studio tenía las DLL bloqueadas. No hay cambios de entidades ni migración. Correrlos con Visual Studio detenido.
