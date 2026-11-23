# Fase 5.8 — Afiliados

**Estado:** ✅ hecho 2026-11-23 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [Diseño §6](../actualizaciones/suscripciones_2026_11_18.md#6-afiliados)

## Alcance (del plan)
Código y liga por vendedor, capturados en el registro; 30 % de un mes para el referido en su primer pago; 10 días para quien invita cuando el referido paga, hasta 12 en 12 meses; pantalla «Invita y gana».

**Demostrable:** un vendedor comparte `/r/QX6XKP`; quien la abre se registra con el código ya escrito, ve en «Mi plan» el precio tachado y «tienes $44.70 de descuento»; al abonarse su primer pago, quien lo invitó recibe 10 días de Pro, un aviso y un correo, y lo ve en `/app/invita`.

## Decisiones
- **Del usuario (2026-11-23):**
  - Solo la 5.8. La **CLABE única (5.2) sigue sin hacer**: hoy dos cuentas pueden usar la misma CLABE, que era una de las defensas contra el abuso de referidos. Queda como riesgo (ver Pendientes). El premio igual exige un pago real.
  - Si quien invita está en el onboarding (sin suscripción) cuando su referido paga, el premio **queda pendiente** y se le da al empezar su prueba, encadenado al final de ella.
  - Un código que no existe da **error en el campo** («No encontramos ese código de invitación. Revísalo o deja el campo vacío.») y no crea la cuenta.
- **`RewardedAt` en lugar de `RewardPeriodId`** (cambio frente al §9 del diseño): el premio apunta a su referido con `SubscriptionPeriods.ReferralId` (FK, índice único filtrado: un premio por referido). Así no hay FKs circulares, y `RewardedAt` sirve para contar el tope.
- **Califica cualquier cargo abonado**, haya tenido descuento o no. Un pago incompleto (`partial` o `paid` por menos) no califica. Un segundo pago no hace nada.
- **El descuento sigue mientras el referido no tenga un pago abonado** (`Referral.QualifiedAt` nulo), en cualquier paquete y sobre el precio de un mes que le toca (normal o de fundador). Un link con descuento que se generó antes de calificar se sigue abonando si se paga (todo link pagado se abona).
- **Tope:** premios con `RewardedAt` desde `MexicoTime.AddMonths(ahora, −12)`. Un vendedor dado de baja no gana (se registra con el motivo); uno suspendido sí.
- **Meses de regalo del anual** con los precios de lista: con el descuento, el anual decía «3 meses de regalo».
- **«Invita y gana» para todos**, también el gratuito y el onboarding (el código de afiliado es ✓ en los dos planes, §2). Muestra el primer nombre del invitado (o el negocio), nunca su correo.
- **Textos sin monto en «Invita y gana»** («30 % de un mes»): el monto depende de si el invitado paga precio de fundador. «Mi plan» del invitado sí muestra el monto exacto.
- **Se reutiliza `receipt-share.js`** en lugar de un `invite-share.js` nuevo: ya hace copiar, «Compartir» y WhatsApp de escritorio para cualquier `[data-receipt-share]`. Se le agregaron `data-share-title` y `data-copy-failed`.

## Qué se hizo
- **Domain:**
  - `Merchants/ReferralCode` (alfabeto sin 0, O, 1 ni I; `Generate`, `Normalize`, `IsValid`, `InputPattern`, `PathFor`);
  - `Merchant.ReferralCode` y `Merchant.Register(..., referralCode = null)` (sin código, genera uno);
  - `Subscriptions/Referral` (`Qualify`, `MarkRewarded`, `Skip`, `IsPendingReward`) y `ReferralSkipReason`;
  - `Subscriptions/ReferralRewardPolicy` (`Decide` ⇒ `Grant | Pending | Skip`, `WindowStart`);
  - `SubscriptionPeriod.ReferralId`, `Subscription.AddDaysPeriod(..., referralId)`, `Subscription.QuoteFor(..., welcomeDiscount)`, `SubscriptionOffers.For(..., welcomeDiscount)`;
  - `NotificationType.ReferralRewardGranted | ReferralRewardPending | ReferralRewardSkipped`.
- **Infrastructure:**
  - configuraciones de `Merchants.ReferralCode` (`char(6)` único), `Referrals` y `SubscriptionPeriods.ReferralId`; filtro multi-tenant de `Referrals` para los dos vendedores;
  - migración `Referrals`: la columna nace nula, un T-SQL genera los códigos de los existentes y luego pasa a no nula con el índice único;
  - `Subscriptions/ReferralRewarder`: `QualifyAsync` (lo llama `SubscriptionPaymentRecorder.CreditAsync` dentro de su transacción, con candado `UPDLOCK, HOLDLOCK` sobre la suscripción de quien invita) y `GrantPendingAsync` (lo llama `TrialStarter.EnsureTrialAsync`);
  - `ReferralRewardNotifier` (después del commit, con `INotificationService`, `DedupKey` `ref:{id}`, nunca lanza) y `MerchantEmails.ReferralReward`;
  - `SubscriptionCheckout`: descuento de bienvenida si hay un `Referral` sin calificar; `SubscriptionOffersResult.WelcomeDiscount`.
- **Web:**
  - `MerchantSignUpInput.ReferralCode`, `ReferralCodeMessages`, parcial `_ReferralCodeField` en Register y ExternalLogin; el error `UnknownReferralCode` va al campo;
  - `AccountRegistration.CreateMerchantAsync(..., referralCode)`: valida el código antes de crear nada, genera un código único y crea el `Referral` en la misma transacción;
  - `Identity/ReferralCookie` y `Controllers/ReferralLinksController` (`GET /r/{código}`, `noindex`);
  - `Areas/Merchant/Controllers/InviteController` (`/app/invita`), `Models/InviteViewModels.cs`, `Views/Invite/Index.cshtml`, entrada en «Más» y liga en «Mi plan»;
  - «Mi plan»: aviso `data-welcome-discount`, precio tachado (`data-plan-offer-list-price`) y fila «Invita y gana días de Pro» en la tabla de funciones;
  - `site.scss`: `.invite-code` y `.invite-share`.

## Pruebas
- `dotnet build`: sin advertencias.
- `dotnet ef migrations has-pending-model-changes`: sin cambios.
- `dotnet test`: 593 unitarias, 12 de la portada y 695 de integración, todas correctas.
  - **Unitarias nuevas:** `ReferralCodeTests` (alfabeto, normalización, patrón del campo, código al registrarse) y `Subscriptions/ReferralTests` (calificar una vez, premio u omisión, política con tope 11/12, dado de baja y onboarding, ventana de 12 meses, premio encadenado con su referido, descuento en cualquier paquete, meses de regalo sin cambio).
  - **Integración nuevas:** `Identity/ReferralRegistrationTests` (código único por vendedor, registro con código en minúsculas y con espacios, código que no existe y mal formado sin crear cuenta, validación fuera del formulario, `/r/{código}` con cookie y campo lleno, `/r/` con un código que no existe, ExternalLogin con la cookie y con un código que no existe) y `Subscriptions/ReferralTests` (descuento solo en el primer pago, sin código no hay descuento, `partial` no califica, premio de 10 días una sola vez con aviso y correo, gratuito gana Pro desde ese momento, tope de 12, premio de hace 13 meses no cuenta, dado de baja, onboarding pendiente que se da al activarse, «Invita y gana» con invitados y aislamiento, «Más» y sesión).
- Migración aplicada en la base local: 30 vendedores, 30 códigos distintos, ninguno con 0, O, 1 ni I.

## Revisión en navegador
Hecha el 2026-11-23 con el MCP de Playwright (Edge) a 375, 768 y 1280 px, con el sitio sin Pey, Resend ni jobs, contra la base de desarrollo.
- **Datos:** se registraron `referido-58@prueba.mx` (con la liga de otro vendedor) e `invitado-58@prueba.mx` (con la liga de `referido-58`). A `referido-58` se le confirmó el correo y se le puso una prueba vigente por SQL. Para ver el descuento se pusieron los precios reales en `SystemSettings` durante la revisión y después se regresaron a los de la prueba real ($199 / $0.02 / $0.01 / $0.01).
- **Revisado:**
  - `/r/4xltmh` ⇒ registro con «4XLTMH» escrito; validación en el cliente con «ab1» («El código tiene 6 letras y números.») y aceptación de « 4xltmh »; «ZZZ999» ⇒ error del servidor bajo el campo, sin crear la cuenta; con el código correcto se creó el `Referral`;
  - `/r/QX6XKP` y registro sin escribir el código (llegó de la URL);
  - «Invita y gana»: código, liga, WhatsApp (en escritorio `whatsapp://` y el respaldo de WhatsApp Web), «Copiar liga» («Liga copiada.»), contadores e invitado «Ernesto · Aún no paga»; pestaña «Más» activa;
  - «Mi plan» del referido: «tienes $44.70 de descuento», $149 tachado ⇒ $104.30, anual $1,490 ⇒ $1,445.30 con «2 meses de regalo»;
  - «Más» con «Invita y gana».
- **Resultado:** consola sin errores, ninguna respuesta ≥ 400 y sin scroll horizontal.
- **Corregido durante la revisión:** «Invita y gana» decía «30 % de un mes ($59.70)», pero con precio de fundador el invitado ahorra $44.70; ahora dice «30 % de un mes».
- **No revisado en navegador:** el aviso pendiente y el premio al activarse (solo en pruebas de integración), y el registro con Google/Facebook real (sin credenciales; probado con el esquema falso).

## Pendientes
- **CLABE única (5.2) sin hacer**: dos cuentas pueden registrar la misma CLABE. El premio exige un pago real, pero alguien podría crear cuentas con su misma CLABE para invitarse y ganar días pagando el primer mes con descuento.
- **Prueba real contra Pey** (con confirmación): con el precio en $0.01, un referido paga ⇒ 10 días al que lo invitó. Con $0.01 el descuento redondea a $0.00, así que esa prueba no cubre el descuento.
- **Admin** (Fase 6.2): referidos de cada vendedor y anulación de un premio.
- **Carrera poco probable:** si la cuenta de quien invita se activa justo mientras se abona el pago de su referido, el premio puede quedar pendiente sin darse. Se corrige a mano desde el admin (6.2).
- **Textos legales** (5.11): referidos en los términos, y la cookie y los datos de referidos en el aviso de privacidad.
- La campana y `/app/avisos` que muestran los avisos de premio llegan con la 5.4 (hoy el aviso se guarda y sale el correo).
