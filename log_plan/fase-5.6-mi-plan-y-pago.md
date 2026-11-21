# Fase 5.6 — «Mi plan» y pago con link de Pey

**Estado:** ✅ hecho 2026-11-20 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas)

## Alcance (del plan)
Pagar Pro desde «Mi plan» con el link de pago de Pey y abonarlo como periodo pagado ([diseño §4, §9 y §10](../actualizaciones/suscripciones_2026_11_18.md#56-mi-plan-y-pago-con-link-de-pey)):
- paquetes de 1, 3 y 6 meses y anual, con precio de fundador y anual congelado;
- un link por cargo;
- se abona solo con el webhook `payment_link.status_updated` o con una consulta a Pey, y hay conciliación;
- reutiliza el backend de la [PoC](poc-link-de-pago.md) y borra su pantalla.

**Demostrable:** el vendedor entra a «Mi plan», ve los cuatro paquetes con su precio y la fecha hasta la que tendría Pro, y pulsa «Pagar». El sitio crea el link y lo manda a la página de Pey. Al volver, `/app/plan/pago/{id}` muestra «Confirmando tu pago…» y cambia sola a «¡Pago confirmado!» cuando llega el webhook (o cuando la consulta del servidor a Pey lo ve pagado). Le llega el correo «Recibimos tu pago».

## Decisiones
- **Confirmadas por el usuario (2026-11-20):**
  - Al abonar se manda el correo «Recibimos tu pago» (`ISubscriptionPaymentNotifier`). La 5.4 le agrega el aviso en el sitio.
  - **Fundador:** si quedan lugares al crear el link, el cargo sale a precio de fundador (`IsFounderPrice`). El número se asigna al confirmarse el pago, dentro de la transacción del abono (`MAX+1` con `UPDLOCK, HOLDLOCK`). Si para entonces ya no hay lugar, el pago se abona igual (ya pagó $149) pero no queda como fundador.
  - **Sin barrido nocturno** (`barrido-links` con `GET /transaction/api-key`): solo webhook + conciliación por link. Esa consulta trae todas las transacciones de la API key, incluidas las ventas de todos los vendedores, para encontrar casi siempre nada. Los casos que no cubre (un link de más de 60 días pagado con el webhook perdido, o un link cuyo id no se guardó y además se perdió el webhook) se abonan a mano desde el admin (Fase 6). Se puede agregar después.
- **Decisiones de implementación:**
  - Solo paga quien ya tiene `Subscription` (la prueba empezó) y no está `Disabled`. En el onboarding no hay paquetes: un periodo `Paid` sin prueba sería invisible para `PlanStatus` y estorbaría a `TrialStarter`. Un vendedor `Suspended` sí puede pagar.
  - Afiliados (5.8) fuera: nunca hay descuento de bienvenida.
  - **Anual congelado:** al abonar un anual se guarda `LockedAnnualPrice = ListPrice`. Al cotizar se respeta solo si el estado es `IsProLike` (no bajó al gratuito); si el precio vigente es menor, se cobra el vigente (`SubscriptionPricing`).
  - **El periodo se encadena desde el momento del pago** (`datePaid` de Pey, sin pasar de ahora), no desde que nos enteramos: si la conciliación lo ve horas después, no se pierden días ni se regalan.
  - **Calendario de `conciliar-links`**, contado desde `CreatedAt`: primera consulta a los 10 min; cada 10 min hasta 2 h, cada hora hasta 3 días y diario hasta 60 días. El diseño decía «30 días después del fin de la gracia», que no tiene una fecha clara para un cargo que el vendedor crea por adelantado.
  - `GET /api/subscription-charges/{id}` y la página de confirmación **también consultan a Pey**, con un freno de 15 s por cargo. Así, en local sin túnel (sin webhook), el regreso de Pey se confirma igual. El freno cuenta también el último intento fallido (en memoria), para que con Pey caído el polling no le pegue cada 3 s; el intento fallido no se sella, para que el job lo reintente.
  - Un link `pending` del mismo paquete y monto se reutiliza; si cambia el precio (fundador agotado, ajuste en `SystemSettings`), se crea otro.
  - Pagos sobre un cargo ya pagado (Pey no debería permitirlos) se guardan sin abonar otra vez y dejan una advertencia en el log. Dos links pagados son dos cargos y se abonan los dos, encadenados.
  - Con tarjeta, Pey parece cobrar una comisión encima del precio (§11 del diseño, sin confirmar). «Mi plan» lo avisa debajo de los paquetes.
  - Si el sitio corre por http (local), «Pagar» no crea el cargo ni llama a Pey: avisa que hace falta HTTPS (perfil `https` o túnel + `App:PublicBaseUrl`). Fuera de Development el aviso es genérico.
- **Payload del webhook:** revisado en docs.pey.mx (`/docs/webhooks/payment-link/status-update`, 2026-11-20). Es plano, con `type`, `apiKeyId`, `paymentLinkId`, `status` en minúsculas (`pending|processing|paid`), `clientReferenceId` y `transactions[]` (`transactionId`, `paymentMethod`, `datePaid` en segundos Unix, `amount`, `amountBreakdown {base, tip, fee}`, `applicationFee`, `metadata.externalMetadata`). `externalMetadata` sale en el ejemplo pero no en la tabla de campos, así que la búsqueda principal es por `paymentLinkId`.

## Qué se hizo
- **Dominio** (`Domain/Subscriptions`):
  - `SubscriptionCharge` (con `SubscriptionChargeOrigin { Merchant, Renewal }`) y `SubscriptionPayment`;
  - `SubscriptionPeriod.SubscriptionChargeId`; `Subscription.AddPaidPeriod(..., subscriptionChargeId)`, `QuoteFor`, `AssignFounder` y `LockAnnualPrice`;
  - `SubscriptionOffers` (paquetes con equivalencia mensual, «por $X más el año», meses de regalo y fecha de Pro);
  - `PaymentLinkReconciliationSchedule`;
  - `SubscriptionPackage.Label()`.
- **Application** (`Subscriptions/SubscriptionCheckout.cs`): `ISubscriptionCheckout`, `ISubscriptionPaymentRecorder`, `ISubscriptionPaymentReconciliation`, `ISubscriptionPaymentNotifier`, `PaymentLinkUpdate` y `SubscriptionChargeMetadata`.
- **Infrastructure**:
  - `Subscriptions/SubscriptionCheckout`, `SubscriptionPaymentRecorder`, `SubscriptionPaymentReconciliation` y `SubscriptionPaymentEmailNotifier`;
  - `Jobs/PaymentLinkReconciliationJob` (`conciliar-links`, cada 10 min);
  - `MerchantEmails.SubscriptionPaidBody`;
  - webhook: contrato `PeyPaymentLinkStatusUpdated`, despacho en `PeyWebhookProcessor` (antes `Ignored`), y `PeyJson.ParsePaymentLinkStatus`, compartido con `PeyClient`;
  - **migración `SubscriptionCharges`** (no destructiva): tablas `SubscriptionCharges` (índice único filtrado en `PeyPaymentLinkId`, check `Amount = ListPrice − DiscountAmount`) y `SubscriptionPayments` (único en `PeyTransactionId`), FK `SubscriptionPeriods.SubscriptionChargeId`; filtros multi-tenant.
- **Web**:
  - `PlanController`: «Mi plan» definitivo, `POST /app/plan/pagar` y la confirmación `/app/plan/pago/{id}`;
  - `SubscriptionChargesApiController` (`GET /api/subscription-charges/{id}`);
  - vistas `Plan/Index` y `Plan/Payment`, y `wwwroot/js/subscription-payment.js`;
  - modelos `PlanOfferViewModel`, `SubscriptionChargeRow`, `SubscriptionPaymentViewModel` y `PlanUrls.Pay`/`PaymentStatus`/`ApiStatus`.
- **PoC borrada**: `PaymentLinkPocController`, sus modelos y vistas, y `PaymentLinkPocTests`. La liga de «Más» ya no existía.

## Pruebas
- **Unitarias** (`SubscriptionChargeTests`): transiciones del cargo, referencia `sub-{id}`, reutilización, cobertura con la base, cotización con y sin lugar de fundador, fundador sin lugares, anual congelado solo sin bajar al gratuito, periodo con su cargo, ofertas (normales y de fundador) y cada borde del calendario.
- **Integración**:
  - `MyPlanTests`: paquetes en la prueba con precio de fundador; sin lugares, precio normal; onboarding y dado de baja sin paquetes ni llamadas a Pey; `POST pagar` con monto, URLs, `clientReferenceId` y metadata; reutilización; cambio de precio ⇒ link nuevo; Pey no disponible o rechazo ⇒ `Failed` + aviso; sin HTTPS no crea cargo; paquete desconocido; confirmación y API que abonan al consultar a Pey; freno de la API (también con Pey caído); cargo ajeno ⇒ 404; API sin sesión ⇒ 401; pagos en «Mi plan».
  - `SubscriptionWebhookTests`: `paid` abona después de la prueba (con fundador 1 y correo); `processing` y luego `paid`; `partial` y monto menor ⇒ `Partial`; repetido por `webhook-id` y por transacción; link viejo; dos links encadenados; pago en la gracia (cuenta desde el vencimiento); pago en el gratuito (empieza al pagar); anual congelado; fundador sin lugar al pagar; cargo encontrado por la referencia; metadata de otro vendedor ⇒ `Failed`.
  - `PaymentLinkReconciliationJobTests`: abona un link cuyo webhook se perdió; no consulta recién creados ni pagados; `NOT_FOUND` sella y una falla no; respeta el calendario.
  - Ajustadas: `PeyWebhookTests` (`payment_link.status_updated` sin `paymentLinkId`, con estatus desconocido o sin cargo ⇒ `Failed`), `FreePlanLimitsTests` («Mi plan» ya no dice «Fase 5.6») y las URLs de ejemplo de `PeyClientTests`.
- **Resultados** (Release, porque Visual Studio tenía el sitio en depuración y bloqueaba `bin/Debug`):
  - `dotnet test -c Release`: 1023 en verde (424 unitarias, 12 de la portada y 587 de integración).
  - `dotnet build -c Release` sin advertencias (`TreatWarningsAsErrors`).
  - `dotnet ef migrations has-pending-model-changes`: sin cambios.
  - Debug: compilado sin errores a una carpeta aparte. Al final no se pudo repetir porque Visual Studio tenía abierto `obj/Debug`.

## Revisión en navegador (Playwright, Edge)
Instancia propia en `http://localhost:5251` (Release), con `Jobs__Enabled=false`, `Pey__ApiKey=` vacío y `Pey__BaseUrl` a un puerto cerrado, así que **no se llamó a Pey real**. Correos a disco (`Resend__ApiKey=` vacío). Vendedor `plan56@prueba.mx`, registrado por la UI; los estados se armaron con SQL.
- «Mi plan» en onboarding (375), prueba (375, 768), Pro con pagos en proceso e historial (375), gracia (768) y gratuito (1280). Sin scroll horizontal, pestaña «Más» activa y consola sin errores.
- «Pagar» por http: aviso de HTTPS, sin cargo ni llamada a Pey (1280).
- Confirmación con un cargo `Pending` sembrado (375): «Confirmando tu pago…» y «Volver a la página de pago». Con un `payment_link.status_updated` firmado a mano (curl), la página cambió sola a «¡Pago confirmado!» con «Tienes Pro hasta el 13 de enero de 2027». Llamadas a la API con 200, sin 404. También «Procesando» (768) y pagado (1280).
- Correo «Recibimos tu pago» en `App_Data/emails`: monto, paquete, «Tienes Pro hasta…», fundador número 1 y botón a `/app/plan`.
- **Corregido durante la revisión:**
  - el anual repetía «$124.17 al mes» (junto al precio y en el texto destacado); ahora solo en el destacado;
  - el aviso de HTTPS salía dos veces tras pulsar «Pagar»;
  - con Pey sin responder, el polling reintentaba la consulta cada 3 s (freno nuevo para intentos fallidos);
  - un cargo pagado cuya vigencia ya terminó decía «Ya tienes Pro… hasta» una fecha pasada: el título ahora es «¡Pago confirmado!» y la fecha solo sale si es futura.
- Capturas en `.playwright-mcp/56-*.png`.
- Datos que quedaron en la base de desarrollo: el vendedor `plan56@prueba.mx` (en el gratuito, con un cargo pagado de prueba, link falso `01PLREVISION56`). El cargo «Procesando» con link falso se borró para que `conciliar-links` no consulte a Pey por él.

## Prueba real con Pey (2026-11-20) y ajuste
- El usuario pagó el mensual a precio de fundador ($0.01) con CoDi desde su vendedor, que estaba en la prueba. El pago **se abonó**: el cargo quedó `Paid` con su transacción `CODI`, el periodo `Paid` se encadenó al terminar la prueba (19-dic → 19-ene) y el vendedor quedó como fundador #2. No llegó webhook (sin túnel); lo confirmó la consulta de la página de regreso (`PaymentCheckedAt`).
- **Hallazgo:** las pantallas seguían diciendo «Prueba: te quedan 60 días» (además mal contado: hasta el fin de lo pagado) y «Paga Pro y sigue después de tu prueba», así que parecía que no había pagado.
- **Ajuste:** `PlanStatus.For` regresa `Pro` si la prueba cubre el instante pero ya hay un periodo `Paid` vigente o futuro (un premio o regalo no cuenta). Nuevo `TrialEndsAt`/`LastTrialDay`. La tarjeta de Inicio y «Mi plan» dice «Plan Pro · Vigente hasta el 19 de enero de 2027. Lo que pagaste empieza al terminar tu prueba, el 19 de diciembre.» y los paquetes pasan a «Agrega más tiempo de Pro» (antes «Adelanta tu plan»; el usuario lo cambió porque se entendía como adelantar la fecha). Los avisos de fin de prueba (5.4) ya no le llegarán a quien pagó.
- Pruebas: 3 unitarias nuevas o ajustadas en `PlanStatusTests`/`SubscriptionPeriodChainTests`, una de integración nueva (`Pagado_durante_la_prueba_las_pantallas_ya_muestran_Pro`) y `MyPlanTests.Los_pagos_aparecen_en_Mi_plan` ajustada. Suite completa en Release: 1027 en verde.
- Navegador (Release, sin Pey): `plan56@prueba.mx` con prueba en curso + mes pagado después. Inicio (375, 1280) y «Mi plan» (375, 768) con «Plan Pro» y la nota, sin scroll horizontal, consola sin errores. Capturas `.playwright-mcp/56b-*.png`.

## Pendientes
- **Webhook real de un link** (con túnel + `App:PublicBaseUrl`): la prueba real del 2026-11-20 se abonó por la consulta de la página de regreso, sin webhook. Falta ver llegar `payment_link.status_updated` de Pey.
- **Confirmar con Pey**: quién paga `fee` con tarjeta y qué es `applicationFee`; si un link puede ofrecer solo CoDi; qué es `partial` y si un link pagado acepta otro pago; si `externalMetadata` siempre viaja en el webhook; reintentos de `payment_link.status_updated`.
- Cargos `Partial` y pagos extra: hoy solo quedan en el log y en la base; su pantalla de revisión es de la Fase 6.
- Barrido nocturno de links (`GET /transaction/api-key`): pospuesto (ver Decisiones).
- Aviso en el sitio del pago abonado: llega con la 5.4.
