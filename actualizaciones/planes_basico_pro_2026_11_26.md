# Planes Básico y Pro, y checkout — 2026-11-26

Cambia el modelo de la [suscripción del 2026-11-18](suscripciones_2026_11_18.md). **Se quita el plan gratuito.** Quedan dos planes de pago, **Básico** y **Pro**, con una **prueba de Pro de 15 días** que empieza con el primer cobro pagado. Cuando vence la prueba o lo pagado, el vendedor **no puede generar cobros hasta que pague**. Además, el pago de la suscripción pasa a ser un **checkout por pasos**, como el de una tienda en línea.

**Por qué:** un gratuito con 100 cobros al mes le alcanza a la mayoría de los vendedores informales, así que casi nadie tendría motivo para pagar Pro. Con Básico, el vendedor que no necesita el catálogo tiene una opción barata, y la prueba le enseña lo que trae Pro.

Este documento **no implementa nada**: fija las reglas, los datos y el detalle de cada subfase. La lista de subfases está en la [Fase 5 del plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas).

Sale de la propuesta del usuario, de la revisión con Claude y de sus respuestas del 2026-11-26 (§1). Lo que queda con un valor por defecto sin confirmar está en §13.

**Qué sigue vigente del diseño del 2026-11-18:**
- la mecánica del link de Pey y su conciliación (§4 de aquel documento);
- la CLABE única (5.2);
- los afiliados (§6), con los ajustes de §8;
- los avisos (5.4) y el resumen mensual.

Todo lo del plan gratuito (§2, §3 y §5 de aquel documento) queda reemplazado por este.

---

## 1. Decisiones

| Tema | Decisión |
|---|---|
| Planes | **Básico** y **Pro**. Ya no hay gratuito. |
| Prueba | 15 días de Pro completo, una sola vez por vendedor. |
| Inicio de la prueba | Con el **primer cobro pagado**: a la hora `PaidAt` del primer `Payment` del vendedor. Mientras no cobre, no empieza (decisión del usuario: sin el respaldo de «3 días después de activarse»). |
| Al vencer | Estado **Vencido** (`Expired`): no genera cobros. Sí entra a su cuenta, lee su historial, comparte recibos de ventas anteriores y paga. Conserva su catálogo, su historial y sus datos. |
| Gracia | 5 días, solo después de un periodo **pagado**, con las funciones del plan que pagó. La prueba, los premios y los regalos vencen sin gracia. |
| Precios (IVA incluido) | Básico: $99 al mes o $990 al año. Pro: $199 al mes o $1,990 al año. Paquetes de 1 mes y anual (los de 3 y 6 meses se retiraron el 2026-11-27). |
| Fundador | Es un atributo del **vendedor**, con 100 lugares entre los dos planes. Paga $69 / $690 en Básico y $149 / $1,490 en Pro, de por vida. Si cambia de plan, conserva el precio de fundador del plan nuevo. |
| Subir de Básico a Pro | Inmediato. El tiempo de Básico que le queda **se convierte en tiempo de Pro** en proporción al precio mensual que le toca (99/199; un fundador, 69/149). Lo pagado se encadena después. Nunca se devuelve dinero. |
| Bajar de Pro a Básico | Básico empieza cuando termina lo pagado de Pro. |
| Anual congelado | Por plan: quien renueva el anual del mismo plan sin haber vencido paga lo mismo que la vez anterior. |
| Uso justo | 5,000 cobros concretados al mes, en los dos planes. Al pasarlos se alerta al admin, sin bloquear. |
| Afiliados | Quien invita gana 10 días del plan que tenga (§8). El referido recibe 30 % del precio de un mes del plan que elija. Siguen el tope de 12 premios en 12 meses y la regla de que el premio solo llega con un pago real. |
| Checkout | Por pasos: Plan (plan y duración en una página) → Resumen del pedido → Pago en Pey → Confirmación con número de pedido y comprobante (§6). |
| Datos fiscales | No se piden (decisión del usuario). El CFDI sigue fuera de esta implementación. |
| Links de Pey | Se crean **solo** al pulsar «Pagar» en el resumen. Ningún paso anterior, ningún job y ningún correo crea links. |
| Vendedores existentes | Quien no ha pagado recibe 15 días de Básico de transición. Las pruebas de 30 días en curso se respetan. Lo pagado sigue siendo Pro (§10). |
| Publicación | Las subfases 5.12 a 5.18 se publican **juntas**. Publicar una parte dejaría a los vendedores del gratuito sin poder cobrar ni pagar. |

## 2. Planes

| Función | Prueba y Pro | Básico | Vencido |
|---|---|---|---|
| Cobro por monto con QR o por celular | ✓ | ✓ | — |
| Cobros concretados al mes | Ilimitados, con uso justo | Ilimitados, con uso justo | — |
| Historial de ventas (completo, con fechas, búsqueda y detalle) | ✓ | ✓ | Solo lectura |
| Corte de caja diario | ✓ | ✓ | — |
| Recibo con WhatsApp y «Compartir» | ✓ | ✓ | ✓, de las ventas anteriores |
| Recibo con el logo del negocio | ✓ | — (dice «Cobrado con Local133») | — (dice «Cobrado con Local133») |
| Exportar el historial a Excel | ✓ | — | — |
| Catálogo de productos | ✓ | Solo lectura | Solo lectura |
| Cobro con carrito (QR o celular) | ✓ | — | — |
| Catálogo en línea (`/tienda/{slug}`) | ✓ | — | — |
| Vendedores adicionales, QR impreso del puesto, reportes | Próximamente | — | — |
| Invita y gana | ✓ | ✓ | ✓ |

- Lo de Pro se distingue por los **productos**: catálogo, carrito, catálogo en línea y Excel. La diferencia no está en cuántos cobros se pueden hacer.
- El pie «Cobrado con Local133» de Básico sigue llevando a la portada con el código del vendedor. Así se conserva la publicidad que hacía el gratuito.
- La búsqueda en el historial pasa a Básico: sin ella, el historial completo se queda cojo.

**Precios (IVA incluido)**

| Paquete | Básico | Básico fundador | Pro | Pro fundador |
|---|---|---|---|---|
| 1 mes | $99 | $69 | $199 | $149 |
| Anual | $990 | $690 | $1,990 | $1,490 |

- **2026-11-27: solo se venden 1 mes y Anual.** Los paquetes de 3 y 6 meses (n × el precio mensual) se retiraron del checkout. Siguen en el código (`SubscriptionPackage.ThreeMonths`/`SixMonths`) por los cargos y periodos que ya existen.
- El anual tiene su propio precio: 12 meses por el precio de 10 (2 meses de regalo).
- Lo que se ve en pantalla del anual de Básico: «12 meses por el precio de 10» y «Equivale a $82.50 al mes» ($57.50 al mes para un fundador). En el resumen del pedido: «Estás obteniendo 12 meses por el precio de 10». Se calcula con los precios de lista (`SubscriptionOffer.PaidMonths`). Si los precios no dan meses de regalo, no sale.
- Desglose del IVA: $99 = $85.34 + $13.66 de IVA, y $69 = $59.48 + $9.52 (`SubscriptionPricing.VatBreakdown`).
- Los precios viven en `SystemSettings` y cada cargo guarda su monto, igual que hoy: cambiar un precio solo afecta los pedidos nuevos.

## 3. Estados y vigencia

**Estados (`PlanTier`).** Se siguen calculando con la historia de periodos y no se guardan.

| Estado | Cuándo | Qué tiene |
|---|---|---|
| `Onboarding` | Todavía no empieza la prueba: la cuenta bancaria está en revisión, o ya está activa y no ha tenido su primer cobro pagado | Todo Pro (cobra en cuanto la cuenta está activa), salvo publicar el catálogo en línea |
| `Trial` | Los 15 días desde el primer cobro pagado | Todo Pro |
| `Pro` | Dentro de un periodo de Pro: pagado, regalado, premio o conversión | Todo Pro |
| `Basic` | Dentro de un periodo de Básico: pagado, regalado, premio o transición | Básico |
| `Grace` | Hasta 5 días después de que vence un periodo **pagado** | Las funciones del plan pagado, con aviso |
| `Expired` | Ninguno de los anteriores | Vencido (§5) |

- **`SubscriptionPlan { Basic, Pro }`** (enum nuevo): cada periodo y cada cargo dicen de qué plan son. `PlanStatus` expone `Plan`, el plan cuyas funciones aplican en ese instante:
  - en la prueba, Pro;
  - en la gracia, el del último periodo pagado;
  - en el onboarding y en el vencido, `null`.
- `MerchantStatus` sigue mandando sobre el plan: un vendedor suspendido o dado de baja no cobra en ningún plan.
- El catálogo en línea no se publica en el `Onboarding`. Así nadie usa Local133 solo como tienda gratis sin cobrar nunca. Inicio dice: «Tu prueba de 15 días de Pro empieza con tu primer cobro».

**Periodos.** Las reglas del 2026-11-18 se quedan:
- al instante y en hora de México;
- un periodo empieza donde termina el anterior si el vendedor sigue vigente o está en gracia, o en ese momento si ya venció;
- pagar en la gracia no regala días.

Lo nuevo:
- **Comprar Básico con la prueba o con Pro vigente**: Básico se encadena al final. Mientras tanto sigue en `Trial` o `Pro`, con la nota «Tu plan Básico empieza el 20 de diciembre».
- **Regla del 2026-11-20** («la prueba con un pago ya abonado cuenta como Pro»): solo vale si lo pagado es Pro. Si pagó Básico, sigue en `Trial` hasta que termina la prueba.
- **Comprar Pro con Básico por delante (subir de plan)**: al abonarse, `Subscription.ConvertBasicToPro(now, ratio)`:
  1. recorre la cadena desde ahora;
  2. encoge cada tramo de Básico por `ratio` y lo vuelve un periodo `Conversion` de Pro;
  3. recorre hacia atrás lo que sigue, sin dejar hueco (como `RemoveUnusedDays`);
  4. encadena lo pagado al final.

  `ratio` es el precio mensual de Básico ÷ el de Pro que le toca al vendedor: el normal (99/199) o el de fundador (69/149). Se toma de `SystemSettings` al abonar.
- **Ejemplo**: Básico vence el 20 de diciembre a las 10:00, y el 30 de noviembre a las 10:00 se abona su Pro anual.
  - Le quedaban 20 días de Básico: 20 × 99/199 = 9.95 días de Pro.
  - Pro por conversión: del 30/nov 10:00 al 10/dic ~08:48.
  - El año pagado: del 10/dic/2026 al 10/dic/2027.
  - Un fundador tendría 20 × 69/149 = 9.26 días.
- **En pantalla**: el cálculo es exacto, al instante. Se muestran días redondeados hacia abajo, y el resumen del pedido dice «unos 9 días», porque la conversión se hace al abonar, no al cotizar.
- **Gracia**: en la gracia de Básico ya no quedan días. Comprar Pro empieza en la fecha de vencimiento original, como hoy, sin conversión.
- **Fin de la cadena**: el último periodo decide si hay gracia (solo si es `Paid`). `Conversion` y `Transition` no la tienen.

## 4. Prueba al primer cobro
- `TrialStarter.EnsureTrialAsync` deja de llamarse en `BankAccountStatusSync.ApplyAsync` y en `BankAccountRegistration`.
- Se llama desde `PaymentRecorder` **después** de guardar el pago, cuando la venta pasa a `Paid`:
  - la prueba empieza en el `PaidAt` del pago, no en la hora del registro: una conciliación tardía no le quita minutos;
  - es idempotente por la llave alterna única `Subscriptions.MerchantId`. Si dos pagos llegan juntos, uno gana y el otro encuentra la suscripción;
  - nunca lanza: si falla, el pago ya quedó guardado y el webhook responde 200.
- **Respaldo**: `suscripciones-diario` empieza la prueba de los vendedores `Active` con algún `Payment` y sin `Subscription`, con el `PaidAt` del primer pago.
- Los premios pendientes de referidos (`ReferralRewarder.GrantPendingAsync`) siguen saliendo al empezar la prueba, encadenados después de ella.
- La prueba dura `SystemSettings.TrialDays` (pasa a 15).
- **Textos**:
  - **Inicio**: en el último paso del onboarding, «Haz tu primer cobro: ese día empiezan tus 15 días de Pro».
  - **Correo «Tu cuenta ya está activa»**: el mismo mensaje.
  - **Portada**: «Tu prueba empieza con tu primer cobro, así que la espera de Banxico no te quita días».
- Solo paga quien ya tiene `Subscription`, como hoy. En el `Onboarding`, el checkout muestra «Tu prueba de 15 días de Pro empieza con tu primer cobro. Después podrás elegir tu plan».

## 5. Vencido
- **Lo que puede hacer**:
  - entrar a Inicio, que muestra la tarjeta «Tu plan venció» con el botón «Elegir plan»;
  - ver Ventas: historial, detalle y recibos de lo anterior, que también puede compartir;
  - usar Mi información, Mi plan y el checkout, Invita y gana (sus premios lo pueden reactivar) y la cuenta bancaria;
  - ver Productos en solo lectura.
- **Cobrar**:
  - en lugar del formulario muestra el parcial nuevo `_PlanExpired` («Tu plan venció el 3 de diciembre. Elige un plan para seguir cobrando»), con el botón al checkout;
  - en el servidor, `SaleCharging.PlanBlockAsync` regresa `CreateSaleResult.PlanExpired` antes de insertar la venta y de llamar a Pey, en monto, celular y carrito;
  - `GET /app/cobrar/productos/catalogo` responde 403.
- **Ventas ya generadas**: una venta generada antes de vencer se abona siempre. El dinero va directo a la CLABE del vendedor y la plataforma no lo puede detener. Lo mismo pasará con el QR fijo (2.7) cuando exista.
- **Bloqueado**:
  - el corte de caja (`Sales/CashCutLocked`, con el texto del vencido);
  - la exportación (403);
  - el catálogo en línea, que muestra «Catálogo no disponible por ahora».
- **Banner**: `GraceBannerViewComponent` pasa a `PlanBannerViewComponent`, con dos variantes:
  - gracia: «Tu plan vence el …», como hoy;
  - vencido: «Tu plan venció. Elige un plan para seguir cobrando».

  No aparece en el checkout ni en «Mi plan».

## 6. Checkout por pasos

El pago se ve como el de una tienda en línea:
- una barra de pasos;
- un resumen del pedido con el total antes de pagar;
- la aceptación de los términos;
- una confirmación con número de pedido;
- un comprobante.

**El estado va en la URL**, no en una sesión ni en un cargo a medias. El botón «atrás» del navegador funciona, y se puede mandar a alguien directo a un paso. **Ningún GET llama a Pey ni crea cargos.**

```
Plan ─── Resumen ─── Pago ─── Listo
            ●
┌──────────────────────────────────────┐
│ Pedido                               │
│ Pro Anual                    Cambiar │
│ 🎁 Estás obteniendo 12 meses por el  │
│    precio de 10                      │
│ Del 10/dic/2026 al 10/dic/2027       │
│ Tus 20 días de Básico se vuelven     │
│ unos 9 días de Pro desde hoy.        │
│ ──────────────────────────────────── │
│ Precio de lista            $1,990.00 │
│ Precio de fundador          −$500.00 │
│ Total                      $1,490.00 │
│ IVA incluido: $205.52                │
│ ──────────────────────────────────── │
│ Pagarás en la página segura de Pey   │
│ con CoDi o tarjeta.                  │
│ ☐ Acepto los Términos de la          │
│   suscripción (sin reembolsos)       │
│ [        Pagar $1,490.00         ]   │
└──────────────────────────────────────┘
```

**«Mi plan» (`/app/plan`)** es la página de la cuenta, no del pago:
- estado, vigencia y lo que viene después, por ejemplo: «Pro hasta el 9/dic, después Básico hasta el 9/ene»;
- botones (simplificados el 2026-11-27). Cada uno lleva al paso 1 con el scroll en la sección de su plan (`/app/plan/checkout#basico` o `#pro`):
  - el principal, con su plan (el actual; la prueba cuenta como Pro; si está vencido, el de su último pago; si nunca pagó, Pro):
    - «Renovar mi plan Básico/Pro» si ya toca renovar: está en la ventana del aviso (15 días antes del anual, 3 días antes, el día del vencimiento o la gracia) o el plan venció;
    - si no, «Adquirir más días de Básico/Pro»;
  - «Cambiar a Pro» si tiene Básico (ahora o después);
  - ya no hay «Elegir plan» ni «Renovar igual que la última vez»;
- el aviso de renovar dice el plan que vence («Tu plan Básico vence en 2 días») y no trae botón propio: el principal va justo abajo;
- **«Tus pedidos»**: reemplaza a «Pagos». Muestra los pedidos pagados, en proceso y parciales, con su número y la liga al comprobante. Los pedidos sin pagar **no** aparecen, como en la 5.7: un link abandonado no es un «pago pendiente»;
- la tabla de funciones de los dos planes.

**Paso 1, Plan** (`GET /app/plan/checkout`; desde el 2026-11-27 incluye la duración)
- Una sección por plan (`id="basico"` y `id="pro"`, a las que llegan los botones de «Mi plan»), con lo principal del plan, la nota de desde cuándo cuenta lo que pague e insignias:
  - «Tu plan»;
  - «Recomendado» en Pro.
- En cada sección, dos tarjetas: «Básico (1 mes)» y «Básico Anual», «Pro (1 mes)» y «Pro Anual». Cada una con:
  - el precio y, si aplica, «Precio de fundador» y el precio tachado del descuento de bienvenida;
  - en el anual, «12 meses por el precio de 10» y «Equivale a $X al mes»;
  - «Hasta el 9 de diciembre de 2027» (`SubscriptionOffers` por plan);
  - la insignia «Tu paquete» en el del último pago y «Mejor precio» en el anual.
- Cada tarjeta lleva al resumen.
- La tabla de funciones de §2, que reutiliza `MyPlanViewModel.FeaturesFor` y `PlanFeatureRow`.
- Los avisos de fundador («Quedan N lugares») y del descuento de bienvenida.
- `GET /app/plan/checkout/{basico|pro}` (el antiguo paso de la duración) redirige a la sección de su plan.

**Paso 2, Resumen del pedido** (`GET /app/plan/checkout/{plan}/{paquete}`; paquete `1-mes` o `anual`; `3-meses` y `6-meses` redirigen a la sección del plan, p. ej. el «Regresar» de un link viejo)
- Plan y duración («Pro Anual»), con «Cambiar» a la sección del plan en el paso 1. En el anual: «Estás obteniendo 12 meses por el precio de 10».
- Periodo: desde–hasta, con `NextPeriodStart` y la duración.
- Qué pasa con lo que tiene:
  - «Tus 20 días de Básico se vuelven unos 9 días de Pro desde hoy»;
  - «Tu Básico empieza el 9/dic, cuando termina tu Pro»;
  - «Empieza hoy».
- Renglones:
  - precio de lista;
  - precio de fundador;
  - descuento de bienvenida;
  - total;
  - la nota «IVA incluido: $X».
- «Pagarás en la página segura de Pey con CoDi o tarjeta».
- La casilla obligatoria «Acepto los [Términos de la suscripción](https://local133.com.mx/terminos) (sin reembolsos)» (`PublicSiteOptions.TermsUrl`, en otra pestaña) y el botón «Pagar $X».
- Un plan o un paquete que no existe ⇒ 404. Un vendedor que no puede pagar (onboarding o dado de baja) ve el aviso de siempre en lugar del botón.

**`POST /app/plan/checkout`** (`CheckoutInput`: `Plan`, `Package`, `AcceptTerms`, `ExpectedTotal` como texto invariante)
1. Valida el plan, el paquete (solo 1 mes y Anual; otro que ya no se ofrece redirige a la sección del plan sin crear nada) y la casilla. Sin la casilla, regresa al resumen con el error en el campo (también en el cliente) y **no** llama a Pey.
2. Vuelve a cotizar. Si el total ya no es el que vio el vendedor, regresa al resumen con «El precio de tu pedido cambió. Revisa el total antes de pagar» y no llama a Pey. Es el mismo patrón que `ExpectedTotal` y `SaleCartMessages.PricesChanged` del carrito. Esto pasa si cambió un precio, se acabaron los lugares de fundador o se abonó el descuento.
3. Llama a `ISubscriptionCheckout.StartAsync(merchantId, plan, paquete, baseUrl, termsVersion)`, que hace lo de siempre (5.6 y 5.7), con estos cambios:
   - un link `Pending` se reutiliza solo si coincide en **plan**, paquete y monto y se creó hace a lo más `GraceDays` días;
   - el cargo guarda `Plan`, `TermsVersion` (`LegalDocuments`) y `TermsAcceptedAt`;
   - `successUrl` = `{base}/app/plan/pago/{id}`, como hoy;
   - `backUrl` = el resumen del mismo pedido (antes, «Mi plan»), para que «Regresar» de Pey vuelva al carrito.
4. Redirige a Pey (**paso 3**). Los errores (`NotAllowed`, `HttpsRequired`, `GatewayFailed`) regresan al resumen con el aviso.

**Paso 4, Confirmación** (`GET /app/plan/pago/{id}`)
- Se queda en esta ruta porque es la `successUrl` de los links que ya existen.
- Mientras Pey confirma: «Confirmando tu pago…», con la consulta a Pey con freno y el polling de `subscription-payment.js`, como hoy.
- Pagado: «¡Gracias! Tu pedido #123 está pagado», con el resumen (plan, periodo y total), «Ver comprobante» e «Ir a Inicio».
- `Processing`, `Partial` y `Failed` conservan sus textos. El regreso del navegador sigue sin abonar nada.

**Comprobante** (`GET /app/plan/pedidos/{id}/comprobante`)
- Solo de pedidos pagados. Un pedido sin pagar o de otro vendedor responde 404 (filtro multi-tenant).
- Trae:
  - número de pedido;
  - nombre del negocio y correo;
  - plan, paquete y periodo;
  - precio de lista, descuentos y total;
  - desglose del IVA;
  - método de pago (CoDi o tarjeta, de `SubscriptionPayments`) y fecha del pago;
  - la leyenda «Este comprobante no es una factura».
- «Imprimir» (`data-receipt-print`) y «Compartir» (`receipt-share.js`). Las barras van con `d-print-none`.

**Correo «Recibimos tu pago»** (`MerchantEmails.SubscriptionPaidBody`)
- Es la confirmación del pedido: número, plan, periodo, total y el botón «Ver comprobante».
- El botón sigue siendo la primera liga del correo.

**Barra de pasos.** Parcial `_CheckoutSteps` (`<ol>` con `aria-current="step"`):
- los pasos ya hechos son ligas;
- el de Pey nunca lo es;
- en 375 px se compacta a «Paso 2 de 4 · Resumen».

**Entradas al checkout** (rutas en `PlanUrls`):
- `_ProRequired` («Conoce Pro») lleva a la sección de Pro del paso 1;
- el banner de vencido, la tarjeta de Inicio y los correos llevan al resumen de su último plan y paquete; si ese paquete fue de 3 o 6 meses, a la sección de su plan; si nunca pagó, al paso 1.

## 7. Recordatorios y avisos
- **`SubscriptionReminders`**:
  - `Downgraded` pasa a `Expired` («Tu plan venció: ya no puedes cobrar»), los días 0 a 2 desde el vencimiento;
  - etapa nueva `ExpiredFollowUp` el día 7, una sola vez («Tus clientes te siguen buscando: elige un plan para volver a cobrar»);
  - las demás etapas y las ventanas se quedan, también las de la prueba (3 días antes hasta el último día). La llave sigue siendo `sub:{etapa}:{ProUntil}`.
- Los botones de los correos van al checkout (§6) y **nunca** a Pey. El job sigue sin crear links.
- `RenewalNoticeFor` de «Mi plan» sigue igual, con la etapa nueva.
- **Resumen mensual** (`MonthlySummary.For`):
  - compara la comisión de terminal con el precio del plan del vendedor (Básico o Pro, de fundador si lo es);
  - al vencido: «Con Básico pagarías $99»;
  - el botón va a `/app/ventas` si está vigente y al checkout si venció.
- **Uso justo**: `ChargeLimitAlerts` solo detecta el cruce de `FairUseMonthlyPaid`, en los dos planes. Los avisos de 80 y 100 cobros desaparecen con el gratuito.

## 8. Afiliados, fundador y anual congelado
- **Premio de quien invita**: `ReferralRewardDays` días del plan del **último periodo de su cadena**, que es donde se encadena el premio. Si su último periodo es la prueba, el premio es de Pro.
  - Un vendedor vencido que gana un premio vuelve a cobrar con ese plan desde ese momento.
  - `ReferralRewardPolicy` no cambia: baja ⇒ `Skip`, sin suscripción ⇒ `Pending` y el tope de 12.
- **Descuento de bienvenida**: `ReferralWelcomeDiscountRate` × el precio de un mes del plan elegido. Es $29.70 en Básico ($20.70 para un fundador) y $59.70 en Pro ($44.70). Sigue disponible hasta el primer pago de cualquier plan.
- **Fundador**:
  - `Subscription.IsFounder` y `FounderNumber` no cambian;
  - el número se asigna con el primer pago de **cualquier** plan, si quedan lugares;
  - el pedido sale a precio de fundador del plan elegido.
- **Anual congelado**: `LockedAnnualPrice` se acompaña de `LockedAnnualPlan`. Solo aplica al renovar el anual del mismo plan sin haber vencido. Cambiar de plan suelta el precio congelado.
- **Regalo del admin**: el admin elige el plan de los días que regala. Por defecto es el del último periodo.

## 9. Modelo de datos
Migración `BasicAndProPlans`. Los enums se guardan como texto, igual que hoy.

| Tabla | Cambio | Notas |
|---|---|---|
| `SubscriptionPeriods` | `Plan` (`Basic`/`Pro`), y kinds nuevos `Conversion` y `Transition` | Los existentes quedan en `Pro` |
| `SubscriptionCharges` | `Plan`, `TermsVersion` y `TermsAcceptedAt` | Los existentes quedan en `Pro`, sin versión |
| `Subscriptions` | `LastPlan` y `LockedAnnualPlan` | `ProUntil` conserva el nombre y pasa a significar «vigente hasta», en los dos planes |
| `SystemSettings` | Nuevos: `BasicMonthlyPrice` (99), `BasicAnnualPrice` (990), `BasicFounderMonthlyPrice` (69), `BasicFounderAnnualPrice` (690). Se renombran `MonthlyPrice`, `AnnualPrice`, `FounderMonthlyPrice` y `FounderAnnualPrice` a `Pro*`. `TrialDays` pasa a 15. `ProFairUseMonthlyPaid` pasa a `FairUseMonthlyPaid` | Se borran `FreeMonthlyPaidLimit`, `FreeMonthlyGeneratedLimit` y `FreeHistoryDays` |

- **Enums** (`Domain/Subscriptions`):
  - `PlanTier { Onboarding, Trial, Basic, Pro, Grace, Expired }`;
  - `SubscriptionPlan { Basic, Pro }`;
  - `SubscriptionPeriodKind { Trial, Paid, ReferralReward, AdminGrant, Conversion, Transition }`.
- **`PlanEntitlements`**:
  - se quitan `HasFullHistory` y `CanShareReceipt` (ahora siempre son verdad) y `HasMonthlyChargeLimit` (ya no hay límite);
  - se agrega `CanCharge`;
  - se quedan `CanEditCatalog`, `CanChargeWithCart`, `CanExportHistory`, `HasDailyCashCut`, `CanPublishCatalog` y `HasBrandedReceipt`;
  - `For(PlanStatus)` usa el estado y su `Plan`.
- `MerchantPlan` (de `IPlanAccess`) suma `Plan`.
- **Nombres en `PlanStatus`**: `IsProLike` pasa a `IsCurrent` (prueba, Básico, Pro o gracia), y `LastProDay` pasa a `LastActiveDay`.

## 10. Vendedores existentes (transición)
La migración trata a cada vendedor según su estado en ese momento. Usa la misma regla SQL que `CustomerListQuery.WithPlan`.

| Estado al migrar | Qué recibe |
|---|---|
| Gratuito | Periodo `Transition` de **Básico** por 15 días desde la migración |
| En prueba (30 días) | Conserva su prueba. La migración le encadena la `Transition` de Básico de 15 días al terminarla |
| En gracia | `Transition` de Básico desde el fin de lo pagado hasta 15 días después de la migración. Los días de gracia que le queden pasan a ser de Básico. Si hay alguno, el admin le puede regalar días de Pro |
| Pro pagado, premio o regalo | Nada: sigue igual. Al vencer pasa por la gracia (si fue pagado) y luego a vencido |
| Sin prueba todavía | Las reglas nuevas: 15 días de Pro con el primer cobro (ver §13, punto legal) |

- Cada periodo `Transition` lleva un renglón en `AuditLogs` (actor «sistema», motivo «Cambio de planes 2026-12»).
- **Anuncio**: el job diario manda una sola vez el aviso y el correo `PlanChangeAnnouncement` (llave `planes-2026-12`) a los vendedores registrados antes de la fecha de corte. Explica los planes nuevos, su transición si la tiene y el botón al checkout.
- Lo que ya se pagó no cambia: los periodos y cargos existentes quedan como Pro.

## 11. Lo que se retira del gratuito
- **Dominio**: `MonthlyChargeAllowance` (solo queda el uso justo, junto a `FairUseMonthlyPaid`), y los resultados `CreateSaleResult.MonthlyLimitReached` y `GenerationLimitReached`.
- **Infraestructura**:
  - `IChargeUsage`/`ChargeUsage`. El tablero del admin calcula el uso justo con su propia consulta (6.4);
  - los avisos de 80 y 100 de `ChargeLimitAlerts`;
  - `LoggingChargeLimitNotifier` queda solo con el uso justo.
- **Vistas**: `_ChargeUsage`, `ChargeUsageViewModel` y `PlanBlockedNotices` del límite.
- **Historial**: `MerchantPlan.HistoryStartUtc`, `HiddenOlderCount`/`HiddenOlderText` y `Sales/DetailLocked`. `SalesHistoryQuery.Filter` ya no recorta ni descarta filtros.
- **Recibo**: el modo limitado (`ReceiptLinks.ShareFor(..., canShare)`, `data-receipt-share-limited`, `ShareRequiresPro`). `/app/ventas/{id}/whatsapp` funciona en todos los estados.
- **Lo que te pierdes**:
  - se quita el «…ocultas» del historial;
  - se quedan `MissingCatalogText` en Básico («Tu catálogo de 63 productos te espera en Pro») y las filas de `PlanFeatureRow`.
- **Admin**: el filtro `plan=gratuito` y su conteo en el tablero.
- **Pruebas**: `SeedFreePlanAsync` pasa a `SeedExpiredPlanAsync`, y se agrega `SeedBasicPlanAsync`.

## 12. Detalle de las subfases
- Cada subfase que toca vistas pasa la revisión de Playwright a 375, 768 y 1280 px (CLAUDE.md) y actualiza `azure.md` al terminar.
- Se trabajan en una rama y **se publican juntas** (§1).
- El orden sigue las dependencias: el dominio primero y la portada al final.

### 5.12 Planes y vigencia v2
✅ *Hecho el 2026-11-27, sin publicar: sale junto con la 5.13 a la 5.18.* — [bitácora](../log_plan/fase-5.12-planes-y-vigencia-v2.md)

Cambios frente a lo de aquí:
- Las columnas `Free*` y las banderas `CanShareReceipt`, `HasFullHistory` y `HasMonthlyChargeLimit` se quedan hasta la 5.14. El vencido conserva los límites del gratuito.
- `ConvertBasicToPro` se conecta al abono en la 5.15.

- **`Domain/Subscriptions`**:
  - `SubscriptionPlan`, `PlanTier` nuevo y `PlanStatus.Plan`;
  - la gracia según el plan pagado;
  - los kinds `Conversion` y `Transition`.
- **`PlanEntitlements`** con la matriz de §2, `CanCharge` y `For(PlanStatus)`.
- **Precios**:
  - `SubscriptionPricing.Quote(plan, package, settings, isFounder, lockedAnnualPrice, welcomeDiscount)`;
  - `SubscriptionOffers.For(..., plan)`;
  - `Subscription.QuoteFor(plan, ...)`;
  - el anual congelado por plan.
- **`Subscription`**:
  - `AddPaidPeriod(plan, package, …)` y `AddDaysPeriod(kind, plan, días, …)`;
  - `ConvertBasicToPro(now, ratio)`, que reutiliza el recorrido de `RemoveUnusedDays`;
  - `LastPlan`.
- **Migración `BasicAndProPlans`** (§9 y la transición de §10).
- **Demostrable**: Inicio y «Mi plan» muestran Básico, Pro, Vencido o «Pro, después Básico».
- **Pruebas unitarias**:
  - cada estado (Básico, gracia de Básico y de Pro, vencido después de la prueba, de un premio y de una conversión);
  - la conversión: precios normales y de fundador, varios tramos de Básico, Básico después de Pro, fin de mes y medianoche de México;
  - precios por plan (paquetes, fundador, congelado y bienvenida);
  - la matriz de funciones.

### 5.13 Prueba al primer cobro
✅ *Hecho el 2026-11-27, sin publicar: sale junto con la 5.12 y la 5.14 a la 5.18.* — [bitácora](../log_plan/fase-5.13-prueba-al-primer-cobro.md)

Cambios frente a lo de aquí:
- Los textos de la portada («Tu prueba empieza con tu primer cobro…») se quedan para la 5.18, con el resto de la portada.
- Un vendedor dado de baja no recibe la prueba; uno suspendido sí, si le pagan un cobro generado antes.

- El disparador en `PaymentRecorder` y el respaldo en `suscripciones-diario` (§4).
- Se quita el inicio de la prueba de `BankAccountStatusSync` y `BankAccountRegistration`.
- En el onboarding, el catálogo en línea no se publica (`IPublicCatalogSettings` ⇒ `PlanRequired`, con el texto «Se activa con tu prueba»).
- Textos de Inicio, del correo de cuenta activa y del checkout.
- **Pruebas de integración**:
  - la prueba empieza con el primer pago por webhook (QR y celular) y por conciliación, con el `PaidAt` del pago;
  - dos pagos simultáneos crean una sola suscripción;
  - activar la cuenta ya no crea la prueba;
  - el premio pendiente de quien invita sale al empezar su prueba;
  - una falla del disparador no tumba el webhook;
  - el respaldo del job.

### 5.14 Acceso por plan y vencido
✅ *Hecho el 2026-11-27, sin publicar: sale junto con la 5.12, la 5.13 y la 5.15 a la 5.18.* — [bitácora](../log_plan/fase-5.14-acceso-por-plan-y-vencido.md)

Cambios frente a lo de aquí:
- «Elegir plan» va a «Mi plan» (`PlanUrls.ChoosePlan`) hasta que la 5.15 lo apunte al checkout.
- Los recordatorios ya no prometen el gratuito (solo el texto); las etapas nuevas siguen en la 5.16.
- El banner del vencido no sale en Inicio ni en Cobrar, que ya muestran el aviso completo.

- **En el servidor**:
  - `PlanExpired` en `SaleCharging.PlanBlockAsync`;
  - el carrito con `PlanRequired` en Básico;
  - `ProductCatalog` y `IPublicCatalogSettings` por la bandera del plan;
  - la exportación (403) y el corte (`CashCutLocked`) según el plan.
- **Vistas**:
  - `_PlanExpired` en Cobrar;
  - la tarjeta «Tu plan venció» en Inicio;
  - `PlanBannerViewComponent` en `_MerchantLayout`;
  - los textos de `ProductListViewModel.ReadOnlyReason` para Básico y vencido.
- Se retira lo de §11.
- **Pruebas de integración**:
  - el vencido no genera cobros por monto, celular ni carrito, y no se llama a Pey;
  - una venta generada antes de vencer se abona después;
  - Básico cobra por monto y por celular, ve el historial completo con búsqueda, usa el corte y comparte por WhatsApp;
  - a Básico se le bloquean el carrito, la edición del catálogo, la exportación y el catálogo en línea;
  - el aislamiento entre vendedores.

### 5.15 Checkout por pasos
✅ *Hecho el 2026-11-27, sin publicar: sale junto con la 5.12 a la 5.14 y la 5.16 a la 5.18.* — [bitácora](../log_plan/fase-5.15-checkout-por-pasos.md)

Cambios frente a lo de aquí:
- El resumen también muestra «Tu precio anual congelado» cuando aplica, además del precio de fundador.
- Reutilizar un link exige además la misma versión de los Términos.
- El asunto del correo es «Recibimos tu pago · Local133»; el número de pedido va en el cuerpo.
- Los botones de los correos de recordatorio siguen en «Mi plan» hasta la 5.16.

- **Pantallas y rutas** de §6:
  - «Mi plan» rediseñado;
  - los pasos 1 a 3;
  - `POST /app/plan/checkout`;
  - la confirmación como pedido, el comprobante y «Tus pedidos»;
  - el correo de pedido pagado.
- **Componentes**:
  - `_CheckoutSteps`;
  - `CheckoutInput`, con la casilla `[MustBeTrue]` en el cliente (como `LegalAcceptanceInput`);
  - `CheckoutMessages`.
- **`SubscriptionCheckout`**: la reutilización por plan, `TermsVersion` y el `backUrl` al resumen.
- **`SubscriptionCrediting`** (lo comparten `SubscriptionPaymentRecorder` y el abono a mano de la 6.2):
  - abona el periodo del plan del cargo;
  - con Pro por encima de Básico, primero `ConvertBasicToPro`;
  - actualiza `LastPlan`, el fundador y el congelado por plan.
- **Pruebas de integración**:
  - ningún GET del checkout llama a Pey ni crea cargos (`FakePaymentGateway.PaymentLinks` vacío);
  - un POST sin la casilla, con el precio cambiado o con un plan inválido no llama a Pey;
  - un link se reutiliza si coinciden plan, paquete y monto, y con otro plan se crea uno nuevo;
  - webhook firmado:
    - un Pro pagado con Básico por delante hace la conversión con los días esperados;
    - un Básico pagado durante Pro se encadena;
    - un Básico pagado durante la prueba deja el estado en `Trial`;
  - el comprobante es 404 para otro vendedor y para un pedido sin pagar;
  - un cargo creado antes de la migración (plan `Pro`, sin versión) se abona igual.
- **Playwright**: el flujo completo hasta antes de Pey, la confirmación con un webhook firmado a mano y el comprobante impreso.

### 5.16 Recordatorios, referidos y avisos
✅ *Hecho el 2026-11-27, sin publicar: sale junto con la 5.12 a la 5.15, la 5.17 y la 5.18.* — [bitácora](../log_plan/fase-5.16-recordatorios-referidos-y-avisos.md)

Cambios frente a lo de aquí:
- El seguimiento del vencido es una ventana de los días 7 a 9 (si el job no corre el 7, sale el 8 o el 9), una sola vez.
- El corte del anuncio es un renglón `PlanChange` de `AuditLogs` que inserta la migración `PlanChangeAnnouncement` al publicarse. El anuncio se manda durante 30 días.
- Los tipos de aviso se renombraron: `DowngradedToFree` → `PlanExpired` y `ProDaysEnding` → `PlanDaysEnding`.
- El resumen mensual del vencido lleva a la sección de Básico del checkout, porque su texto habla de Básico.
- El regalo del admin sigue diciendo «días de Pro» hasta la 5.17.

- Lo de §7 y §8:
  - las etapas `Expired` y `ExpiredFollowUp`;
  - los botones al checkout;
  - el premio con el plan de la cadena y el descuento por plan;
  - el resumen mensual por plan;
  - el anuncio `PlanChangeAnnouncement`.
- **Pruebas**:
  - con reloj falso (`TestClock`), cada etapa nueva en su borde;
  - el premio a un vendedor con Básico, con Pro, en la prueba y vencido;
  - el descuento en cada plan;
  - el anuncio, una sola vez por vendedor.

### 5.17 Admin
✅ *Hecho el 2026-11-27, sin publicar: sale junto con la 5.12 a la 5.16 y la 5.18.* — [bitácora](../log_plan/fase-5.17-admin.md)

Cambios frente a lo de aquí:
- Básico < Pro también aplica a los precios de fundador (confirmado por el usuario). Para la verificación de extremo a extremo de §16 no se pueden poner los dos en $0.01: Básico $0.01 y Pro $0.02.
- Regalar Pro con Básico por delante no convierte el Básico: se encadena al final, como los premios.
- Las subidas de Básico a Pro se cuentan por los periodos `Conversion` creados en 30 días.
- `AuditAction.ProDaysGranted` y `NotificationType.ProDaysGranted` conservan el nombre (se guardan como texto); solo cambian los textos.
- `InTerms` suma los 4 precios de Básico (15 campos).

- **Parámetros**:
  - los campos nuevos en `SettingsInput`;
  - `SystemSettingsRules`: fundador ≤ normal en cada plan, Básico < Pro en el mensual y en el anual, y los campos `InTerms` actualizados.
- **Clientes**:
  - `WithPlan` v2, con `plan=sin-prueba|prueba|basico|pro|gracia|vencido`, y `CustomersTests.Filtro_de_plan_coincide_con_el_estado_calculado` al día;
  - en la pestaña Plan, el plan de cada periodo y de cada cargo, la conversión y la transición;
  - «Regalar días» con selector de plan.
- **Tablero**:
  - vendedores por plan (prueba, Básico, Pro, gracia y vencido);
  - ingresos por plan;
  - conversión de prueba a pago (cualquier plan) y subidas de Básico a Pro en 30 días.
- **Pruebas**: `SettingsTests` con las reglas nuevas, `CustomersTests`, `CustomerPlanTests` (regalar días de Básico) y `DashboardTests`.

### 5.18 Portada y textos legales
- **Portada**:
  - `_Pricing.cshtml` con tres tarjetas: «Prueba Pro 15 días», Básico y Pro, con los precios de §2 y la tabla de funciones;
  - el hero («15 días de Pro gratis»), «Cómo funciona», el cierre y las preguntas frecuentes, con 15 días, la prueba al primer cobro y sin el gratuito. Por ejemplo, «¿Qué pasa al terminar mis 15 días?» pasa a decir «eliges Básico o Pro; si no pagas, no podrás generar cobros nuevos, pero tu información se queda»;
  - `LandingPageTests` contra `SystemSettings.Default*`.
- **Términos**:
  - los dos planes y sus precios, la prueba al primer cobro y el vencimiento sin cobros;
  - la gracia y subir o bajar de plan, con la conversión;
  - el fundador por plan, el anual congelado por plan y el premio de referidos con el plan;
  - el uso justo en los dos planes;
  - la aceptación al pagar cada pedido y la falta de reembolsos.
- Versión nueva en `LegalDocuments`. `LegalPagesTests` y `SystemSettingsRules.InTerms` se actualizan.
- **Aviso de privacidad**: revisar. No hay datos personales nuevos; el pedido guarda la versión de los términos que se aceptó.

## 13. Por confirmar
**Con el usuario** (este documento ya usa el valor que se indica)
1. El vencido **no** ve el corte de caja (sí el historial).
2. Seguimiento del vencido solo el día 7, sin más recordatorios después.
3. «Recomendado» en la tarjeta de Pro del paso 1.
4. La transición de §10, incluido el caso de la gracia.

**Con el abogado**
1. Aceptar los términos en cada pedido funciona como reaceptación de quien paga. ¿Basta, o hay que avisar a todos los existentes y pedirles aceptarlos?
2. Los vendedores sin prueba todavía aceptaron la versión `2026-11-24` (30 días al activarse y un gratuito). ¿Se les aplica la prueba de 15 días al primer cobro, o se les respetan los 30 días?

**Con soporte de Pey** (de antes, pero el resumen del pedido lo necesita)
1. ¿El link puede ofrecer solo CoDi? Con tarjeta, ¿el pagador paga una comisión encima del total? Si es así, el resumen tiene que decirlo antes de «Pagar».

## 14. Fuera de esta implementación
- CFDI y datos fiscales en el checkout.
- Cupones o códigos de descuento distintos del de invitación.
- Correos de «checkout abandonado».
- Cobro automático o tarjeta guardada: Pey no guarda la tarjeta, y cada pedido es un link.
- El QR fijo del puesto (2.7) y lo que sigue «Próximamente».

## 15. Azure
- **5.12**:
  - la migración `BasicAndProPlans` corre al arrancar y crea la transición con la hora del servidor, así que se publica en un horario acordado;
  - si se aplicó a mano antes, la transición no se repite (la migración solo corre una vez).
- **5.13 a 5.17**: sin recursos ni secretos nuevos. Los jobs son los mismos; `suscripciones-diario` suma el respaldo de la prueba y el anuncio.
- **5.18**: la portada se publica con los Términos nuevos **antes o junto** con Web, porque la casilla del checkout enlaza a la versión nueva.

## 16. Verificación
- **Unitarias** (5.12 y 5.16):
  - estados con los dos planes, gracia, vencido y conversión;
  - precios por plan, fundador, congelado y bienvenida;
  - funciones por estado;
  - recordatorios nuevos.
- **Integración**:
  - la prueba al primer cobro;
  - los bloqueos de Básico y del vencido en el servidor;
  - el checkout sin llamadas a Pey antes de «Pagar»;
  - el webhook `payment_link.status_updated` con plan, la conversión y el encadenado;
  - el comprobante y el aislamiento entre vendedores;
  - el admin con el filtro y los parámetros.
- **Playwright** a 375, 768 y 1280 px:
  - «Mi plan»;
  - los tres pasos del checkout, con la barra, «Cambiar», el botón «atrás», la casilla sin marcar y el precio cambiado;
  - la confirmación y el comprobante (también impreso);
  - Inicio, Cobrar y el banner del vencido;
  - el carrito y el catálogo en Básico;
  - las tarjetas de precios de la portada.
- **Extremo a extremo con Pey real** (con confirmación del usuario):
  1. con los precios de Básico y Pro en $0.01, el primer cobro inicia la prueba;
  2. pagar Básico durante la prueba ⇒ webhook ⇒ «Básico empieza el …»;
  3. subir a Pro ⇒ webhook ⇒ conversión de los días;
  4. simular el vencimiento ⇒ Cobrar bloqueado ⇒ pagar ⇒ cobra de nuevo.
