# Plan de desarrollo — Portal de cobros CoDi para vendedores informales (Pey API)

## 1. Contexto

Plataforma para que vendedores informales (tianguis, mercados, bazares, puestos) cobren con CoDi usando la API de Pey. El vendedor se registra, da de alta su cuenta bancaria, y cobra generando un QR (carrito o monto directo). El negocio cobra una **suscripción**: un plan gratuito con límites y un plan Pro de $199 al mes o $1,990 al año (IVA incluido), con 30 días de prueba, que se paga con el link de pago de Pey. *(Cambio 2026-11-18: reemplaza la comisión del 3 % por venta, agrupada en estados de cuenta semanales. Diseño en [actualizaciones/suscripciones_2026_11_18.md](actualizaciones/suscripciones_2026_11_18.md).)*

Repositorio vacío (`c:\personalprojects\local133`): proyecto desde cero.

**Decisiones confirmadas**
- **ASP.NET Core 10 MVC** (vistas Razor + Bootstrap 5, móvil primero) en un solo proyecto web que también expone la API REST interna, el webhook y el hub SignalR; portal cliente y admin como áreas MVC. *(Cambio 2026-11-09: reemplaza la SPA React original. Cambio 2026-11-16: de .NET 9 a .NET 10 LTS, [bitácora](log_plan/mantenimiento-net10.md).)*
- **SQL Server** con EF Core 10.
- **Correo con Resend** (SDK oficial .NET, HTML armado en el código, llave de solo envío). Sin `Resend:ApiKey` los correos se guardan en disco. *(Cambio 2026-11-17, [bitácora](log_plan/mantenimiento-resend.md).)*
- **Correo «Tu cuenta ya está activa»** al pasar el vendedor a activo (alta, webhook, sondeo o botón), uno por vendedor, y layout de marca para todos los correos. *(Cambio 2026-11-18, [bitácora](log_plan/mantenimiento-correo-cuenta-activa.md).)*
- **Formularios de Identity a prueba de doble envío** (botón deshabilitado al enviar y, si el login llega dos veces con la sesión ya iniciada, redirección en vez del 400). *(Corrección 2026-11-26 por un error en producción, [bitácora](log_plan/mantenimiento-doble-envio-login.md).)*
- **Ajustes 2026-11-26** (marca, navegación con sesión, concepto del cobro, «Mi información», recibo, Ventas, «Más», PWA, menú del admin y categorías), por bloques según el [documento de los ajustes](actualizaciones/ajustes_2026_11_26.md#implementación):
  - ✅ Bloque 1, marca y navegación (logo `01.png` en Web y en la portada, «Local133» junto al logo, la barra según el rol, `returnUrl` solo del área del rol, `/` con sesión redirige y home sin barra), 2026-11-26, [bitácora](log_plan/ajustes-2026-11-26-marca-y-navegacion.md);
  - pendientes: bloque 2 (concepto y «Mi información»), 3 (pantallas del vendedor), 4 (PWA), 5 (menú del admin) y 6 (categorías).
- La suscripción se paga con **link de pago de Pey** (cae en la cuenta de la plataforma) y se concilia por webhook. Tiene 5 días de gracia; si no paga, el vendedor **baja solo al plan gratuito**, sin suspensión. *(Cambio 2026-11-18: antes, estado de cuenta semanal pagado con QR CoDi y suspensión por falta de pago.)*
- **CLABE única** entre vendedores y **programa de afiliados**: un código por vendedor; 10 días de Pro por cada referido que pague y 30 % de un mes para el referido. *(2026-11-18)*
- Además del QR dinámico: **QR fijo del puesto**, **cobro por número celular** y **recibo compartible**.

---

## 2. Hallazgos de la API de Pey (afectan el diseño)

Base URL `https://api.pey.mx` · Auth `Authorization: Bearer <api-key>` · Sin sandbox (se prueba en producción con $0.01).

| # | Hallazgo | Consecuencia |
|---|---|---|
| 1 | Pey deposita **directo a la CLABE del vendedor**; no hay split de pagos | La plataforma nunca toca el dinero del vendedor. Su ingreso es la suscripción, que se paga aparte con link de pago. *(Antes: comisión del 3 % facturada en estados de cuenta; cambio 2026-11-18.)* |
| 2 | Una sola API key de la plataforma registra N cuentas (`POST /bank-account`) y cada cobro elige destino con `bankAccountId` | Modelo multi-vendedor con **una API key y un webhook** (Pey permite 1 webhook por API key). |
| 3 | Alta de cuenta tarda **hasta 5 días hábiles** (Banxico) | Pantalla de "cuenta en revisión" + notificación al activarse. El vendedor puede cargar productos mientras tanto. |
| 4 | Webhook `bank.account.updated` **solo se envía al pasar a `ACTIVE`** | `ERROR` (y `errorMessage`) solo se detecta con **polling** a `GET /bank-account/{id}` ⇒ job en segundo plano. |
| 5 | `amount: 0` en `POST /codi/qr` = el pagador escribe el monto; `expiration: 0` = no expira | Habilita el **QR fijo imprimible**. |
| 6 | `expiration` en segundos: `0` o **mínimo 360** | QR dinámico de venta con 600 s; no se puede menos de 6 min. |
| 7 | `numRef` máx. 7 dígitos; `metadata` hasta 50 pares escalares (vuelven como string en `metadata.externalMetadata`) | `numRef` = folio corto de venta; `metadata` = `{kind, merchantId, saleId}` para conciliar. El link de la suscripción lleva `{kind: "subscription", merchantId, subscriptionChargeId}`. |
| 8 | Webhooks firmados con **Standard Webhooks** (`webhook-id`, `webhook-timestamp`, `webhook-signature`; se valida el body crudo) | Verificar firma + idempotencia por `webhook-id` y por `transactionId`. |
| 9 | CLABE de prueba que activa de inmediato: `012180015826225713` (Ramón Orraca Lascurain); pagos no se devuelven | Usarla en desarrollo con montos de $0.01. |
| 10 | `POST /bank-account` exige `alias`, `name`, `clabe`, `entity`; no acepta `taxId` | El formulario necesita **tipo de titular (persona/empresa)**, que no estaba en la idea original. |
| 11 | No está documentado webhook de rechazo/expiración de push ni si un QR puede pagarse más de una vez | Modelar **Pagos separados de Ventas** (un QR ⇒ 0..N pagos) y expirar ventas por tiempo. Confirmar con soporte de Pey. |
| 12 | **Link de pago** (`POST /payment-link/create`): no acepta `bankAccountId` ni concepto, así que el dinero cae en la cuenta de la plataforma. Acepta CoDi y tarjeta. `successUrl`/`backUrl` deben ser HTTPS. **No expira ni se cancela**; `POST /payment-link/update` solo cambia el monto en `pending` | Sirve para cobrar la suscripción, no para las ventas del vendedor. Un link por cargo; todo link pagado se abona, aunque sea viejo. |
| 13 | Webhook `payment_link.status_updated` (`pending`/`processing`/`paid`; `paymentLinkId`, `clientReferenceId`, `transactions[]` con `amountBreakdown {base, tip, fee}` y `applicationFee`). `GET /payment-link/{id}` también puede dar `partial`, sin documentar. Con tarjeta el `fee` parece sumarse a lo que paga el cliente | Abonar solo con `paid` y una suma de `base` que cubra el monto; `partial` ⇒ revisión manual. Respaldo con `GET /payment-link/{id}`. Confirmar con Pey quién paga `fee` y `applicationFee`. |

### Enumeradores de Pey (para `Domain/Pey`)

```csharp
enum PeyCodiStatus        { PENDING, PROCESSING, ACTIVE, ERROR }   // estatus cuenta bancaria
enum PeyEntity            { BUSINESS, PERSON }
enum PeyTransactionStatus { PENDING, PAID }
enum PeyPaymentMethod     { CARD, CODI }
enum PeyPayableType       { PAYMENT_LINK, API_CODI_QR, API_CODI_PN }
enum PeyPaymentLinkStatus { pending, processing, paid, partial }   // suscripciones (link de pago)
enum PeyOwnerType         { API_KEY }
// webhook "type"
"codi.qr.payment_received" | "codi.pn.payment_received" | "bank.account.updated" | "payment_link.status_updated"
// error.type  → { "error": { "message", "type", "requestId" } }
INVALID_REQUEST, INVALID_AMOUNT, INVALID_PARAMETER, INCOMPLETE_PARAMETERS, INVALID_CLABE, INVALID_STATE,
INVALID_PARAMETERS (plural, en listados), BANK_ACCOUNT_NOT_FOUND, NOT_FOUND, FORBIDDEN, AUTH_ERROR, BAD_REQUEST, REQUEST_PARSING_ERROR,
PEY_CONSISTENCY_ERROR, CONCURRENT_UPDATE, SERVICE_UNAVAILABLE, FLAGS_UNAVAILABLE, INTERNAL_SERVER_ERROR
```

`codiStatus`: `PENDING` recibida, aún no enviada a Banxico · `PROCESSING` en trámite · `ACTIVE` puede cobrar · `ERROR` falló (ver `errorMessage`).

### Enumeradores propios

```csharp
enum MerchantStatus  { PendingBankAccount, BankAccountInReview, Active, Suspended, Disabled }
enum ChargeMethod    { DynamicQr, StaticQr, PushNotification }
enum SaleSource      { Cart, DirectAmount, OpenAmount }      // OpenAmount = QR fijo
enum SaleStatus      { Pending, Paid, Expired, Cancelled }
enum SuspensionReason{ Admin, BankAccountError }             // la falta de pago ya no suspende (2026-11-18)
enum PlanTier        { Onboarding, Trial, Pro, Grace, Free } // calculado, no se guarda
enum SubscriptionPackage      { OneMonth, ThreeMonths, SixMonths, Annual }
enum SubscriptionPeriodKind   { Trial, Paid, ReferralReward, AdminGrant }
enum SubscriptionChargeStatus { Pending, Processing, Paid, Partial, Failed }
```

### Dónde se invoca cada API

| Paso del flujo | Llamada / evento |
|---|---|
| Vendedor guarda cuenta bancaria | `POST /bank-account` → guardar `id` + `codiStatus` ⇒ `BankAccountInReview` |
| Activación | Webhook `bank.account.updated` ⇒ `Active` + email al vendedor |
| Detectar rechazo / respaldo | Job cada 30 min: `GET /bank-account/{id}` para cuentas `PENDING/PROCESSING` |
| Cobrar venta (carrito o monto) | `POST /codi/qr` `{amount, concept, expiration:600, numRef, generateImage:false, bankAccountId, metadata}` → la vista Razor dibuja `qrString` en el navegador con **QRCode.js** (Pey no genera la imagen: con ella responde más lento) |
| QR fijo del puesto | `POST /codi/qr` `{amount:0, expiration:0, bankAccountId, metadata:{kind:"static"}}` (una vez por vendedor) |
| Cobro por celular | `POST /codi/pn` `{amount, phoneNumber(10), concept, expiration≥360, bankAccountId, metadata}` → `transactionId` |
| Confirmar pago | Webhooks `codi.qr.payment_received` (match por `codiQrId`) y `codi.pn.payment_received` (sin `codiQrId`: match por el `transactionId` que devolvió `POST /codi/pn`, guardado en `Sales.PeyTransactionId`) |
| Respaldo si se pierde webhook | `GET /transaction/codi/qr/{idc}` para ventas pendientes; nocturno `GET /transaction/bank-account/{id}` |
| Pago de la suscripción | `POST /payment-link/create` `{amount, successUrl, backUrl, hasTip:false, clientReferenceId:"sub-{id}", metadata:{kind:"subscription", merchantId, subscriptionChargeId}}` (sin `bankAccountId`: cae en la cuenta de la plataforma) |
| Confirmar pago de la suscripción | Webhook `payment_link.status_updated` (`paid`) ⇒ periodo Pro. Respaldo: `GET /payment-link/{id}` de los cargos pendientes (job `conciliar-links` y la pantalla de confirmación). El barrido nocturno de `GET /transaction/api-key` (`payableType` `PAYMENT_LINK`) se pospuso (2026-11-20) |
| Detalle / soporte en admin | `GET /transaction/{id}`, `GET /transaction/api-key` |

---

## 3. Evaluación de los módulos

**Los módulos propuestos son correctos.** Ajustes y faltantes:

**Ajustes a lo propuesto**
- *Cuenta bancaria*: agregar **tipo de titular (Persona/Empresa)**; `nombre del negocio` se envía como `alias`. Mostrar estatus y mensaje de error; permitir reintento/cambio de CLABE. Validar dígito verificador de la CLABE antes de enviar.
- *Estados de cuenta* y *comisión*: se reemplazan por la **suscripción** (cambio 2026-11-18, [diseño](actualizaciones/suscripciones_2026_11_18.md)). Se conserva la idea de guardar el monto en cada cargo: cambiar un precio no altera lo ya cobrado.
- *Registro*: sumar verificación de correo, recuperación de contraseña y aceptación de términos/aviso de privacidad.

**Módulos que faltan (necesarios para el MVP)**
1. **Historial de ventas / inicio** (cliente): ventas del día, lista con filtros, detalle, estado del pago. Sin esto el vendedor no puede verificar cobros.
2. **Onboarding / estatus de activación**: checklist visible (perfil → cuenta bancaria → en revisión → activo).
3. **Notificaciones** (correo y avisos en el sitio; WhatsApp después): cuenta activada/rechazada, pago de la suscripción por vencer, gracia, baja al gratuito, 80 % del límite de cobros.
4. **Suscripciones y cobranza** (job + admin): recordatorios, gracia y baja automática al gratuito; en el admin, registrar un pago manual, regalar días y suspender/reactivar.
5. **Admin – transacciones y monitoreo**: ventas globales, bitácora de webhooks, reintento de conciliación, tablero (GMV, ingresos por suscripción, vendedores por plan y por estatus).
6. **Admin – usuarios y auditoría**: roles admin, registro de quién cambió precios, planes o estatus.

**Módulos para después (fuera del MVP)**
Categorías e inventario · vendedores adicionales (en Pro como «Próximamente») · CFDI de la suscripción · PWA instalable con modo offline para catálogo. *(2026-11-18: la exportación a Excel entra en Pro (5.9). Se descartan los links de pago con tarjeta para las ventas del vendedor: el link de Pey no acepta `bankAccountId` y el dinero caería en la plataforma.)*

---

## 4. Arquitectura

```
Local133.sln
src/
  Local133.Domain          entidades, enums (propios y Pey), reglas (CLABE, vigencia y precios de la suscripción)
  Local133.Application     casos de uso, DTOs, validadores (FluentValidation), interfaces
  Local133.Infrastructure  EF Core (SQL Server), PeyClient, webhooks, storage, email, jobs
  Local133.Web             MVC: áreas Merchant/Admin (vistas Razor + Bootstrap), Identity scaffoldeado
                           (Areas/Identity), endpoints /api (webhook, consulta de venta), hub SignalR
tests/
  Local133.UnitTests · Local133.IntegrationTests (WebApplicationFactory<Program> sobre Local133.Web + base de pruebas `Local133_Tests` en el SQL Server local, recreada con migraciones y limpiada entre pruebas con Respawn)
```

- **Auth**: ASP.NET Core Identity con la **UI scaffoldeada** (Razor Pages en `Areas/Identity`, personalizada en español y adaptada al layout), cookie segura, roles `Merchant` / `Admin`, login externo Google y Facebook.
- **Frontend**: MVC con vistas Razor; `_Layout` Bootstrap 5 móvil primero (barra inferior en el área del vendedor, sidebar/Offcanvas en admin). Bootstrap, cliente JS de SignalR y demás librerías cliente vía **LibMan** en `wwwroot/lib` (sin npm ni build de JS). Validación con jQuery Validation Unobtrusive en cliente + FluentValidation en servidor; antiforgery en todos los formularios. JS ligero solo donde hace falta (campo de monto, carrito, QR CoDi con QRCode.js y su cuenta regresiva).
- **Identidad visual**: Bootstrap 5.3.8 compilado desde SCSS con variables propias (rosa mexicano `#E4007C`, azul lona, cempasúchil, verde nopal, tinta ciruela; Bungee en títulos, Atkinson Hyperlegible en texto; botones y campos más altos; sombras duras) con `AspNetCore.SassCompiler` (dart-sass empacado, sin Node). Fuente en `src/Local133.Web/Styles` (`_custom.scss` variables, `site.scss` import + estilos propios); el SCSS de Bootstrap llega por LibMan (`twbs/bootstrap@v5.3.8` en `Styles/vendor`, versionado, no se edita). `wwwroot/css/site.css` es generado (build/publish, y watcher en Debug) y está en `.gitignore`. Personalizar solo con variables Sass antes del import; texto tenue con `text-body-secondary` (`secondary` es el azul). *(Cambio 2026-11-12: antes `bootstrap.min.css` precompilado + `site.css` a mano. Verificado en Edge a 375/768/1280: inicio, registro, `/app`, cobrar, QR, venta pagada, cuenta bancaria, admin con offcanvas, modal de cancelar; consola sin errores, sin 404 ni scroll horizontal; el watcher recompila al guardar `_custom.scss`.)*
- **Multi-tenant**: toda entidad de negocio lleva `MerchantId`; filtro global de EF Core + política de autorización.
- **PeyClient**: `HttpClient` tipado + Polly (timeout por intento y total, circuit breaker; reintentos: GET en 5xx/408/429/red/timeout, POST **solo en 503/429** porque Pey no tiene idempotencia), mapeo de `error.type` a excepciones de dominio, logging con `requestId`.
- **Webhook** `POST /api/webhooks/pey`: leer body crudo → verificar firma Standard Webhooks → guardar en `WebhookEvents` (único por `webhook-id`) → procesar en la misma petición → 200. Si falla algo transitorio (base de datos) responde 500 y el evento queda sin procesar: el reintento de Pey con el mismo `webhook-id` lo reprocesa. Errores permanentes (venta no encontrada, payload inválido) quedan `Failed` con 200. Procesamiento idempotente por `transactionId`. *(Cambio 2026-11-12: antes «responder 200 → procesar»; en la misma petición el vendedor ve el pago al instante sin depender de la cola de Hangfire.)*
- **Tiempo real**: SignalR, grupo por vendedor; el webhook publica `PaymentReceived` y la vista del QR lo escucha con el cliente JS de SignalR. Respaldo: polling desde la vista a `GET /api/sales/{id}` cada 3 s.
- **Jobs (Hangfire, almacenamiento SQL Server)**: sondeo de cuentas bancarias · expirar ventas · conciliación de pagos · suscripciones (diario a las 9:00 `America/Mexico_City`: recordatorios, fin de prueba, baja al gratuito y resumen mensual; sin crear links, 2026-11-21; contenido del resumen mensual en la 5.10) · conciliación de links de pago (`conciliar-links`, cada 10 min, 5.6). *(Cambio 2026-11-18: antes, corte semanal y suspensión por falta de pago.)*
- **Archivos**: `IFileStorage` (disco local en dev, blob en prod), redimensionado con SkiaSharp. *(Cambio 2026-11-14: antes ImageSharp; ver 3.1.)*
- **Secretos**: API key y secreto de webhook de Pey en user-secrets / variables de entorno. CLABE enmascarada en UI.

### Modelo de datos (tablas principales)

`Merchants` (perfil, estatus, `ReferralCode`, `PublicSlug`) · `BankAccounts` (titular, CLABE única, alias, entity, `PeyBankAccountId`, `CodiStatus`, `ErrorMessage`) · `Products` (nombre, descripción, precio, foto, `IsDeleted`) · `Sales` (folio, método, origen, monto, estatus, `PeyQrId`, `QrString`, `PeyTransactionId`, expiración, token público de recibo) · `SaleItems` (snapshot de nombre/precio/cantidad) · `Payments` (`PeyTransactionId` único, `SaleId`, monto, `PaidAt`, `TrackingKey`) · `StaticQrs` · `Subscriptions` · `SubscriptionPeriods` · `SubscriptionCharges` (un link de pago por cargo) · `SubscriptionPayments` · `Referrals` · `Notifications` · `SystemSettings` (precios, prueba, gracia, límites del gratuito, IVA, expiración del QR) · `WebhookEvents` · `AuditLogs` · tablas de Identity. *(Cambio 2026-11-18: sin comisión ni `Statements`. Campos en el [diseño](actualizaciones/suscripciones_2026_11_18.md#9-modelo-de-datos).)*

---

## 5. Fases de desarrollo

Cada fase termina con algo demostrable. Ventas va inmediatamente después del registro; lo mínimo de cuenta bancaria se construye dentro de la fase de ventas porque es prerrequisito para generar un QR.

La bitácora de cada subfase terminada (qué se hizo, pruebas, revisión en navegador, pendientes) está en [log_plan/](log_plan/). Al terminar una subfase: crear `log_plan/fase-<F>.<N>-<slug>.md`, dejar aquí solo ✅, fecha y link, y pasar sus pendientes a [Verificaciones pendientes](#verificaciones-pendientes).

### Fase 0 — Cimientos (≈3 días)
1. ✅ Solución .NET 9 con los 4 proyectos, EF Core + migración inicial, Serilog, ProblemDetails, Swagger. *(hecho 2026-11-09)* — [bitácora](log_plan/fase-0.1-solucion-base.md)
2. ✅ Base de datos en el **SQL Server local del equipo** (sin Docker): crear la base `Local133`, cadena de conexión en user-secrets (`ConnectionStrings:Default`, p. ej. `Server=localhost\SQLEXPRESS;Database=Local133;Trusted_Connection=True;TrustServerCertificate=True`) y aplicar migraciones con `dotnet ef database update`; `README` con arranque local y requisitos (instancia de SQL Server instalada); CI (build + tests). *(hecho 2026-11-09)* — [bitácora](log_plan/fase-0.2-base-de-datos-y-ci.md)

> Nota (2026-11-09): el antiguo punto 0.2 (SPA React) se construyó y quedó **descartado** al cambiar el frontend a MVC Razor; detalle en la [bitácora de la 0.2](log_plan/fase-0.2-base-de-datos-y-ci.md#nota-spa-descartada-2026-11-09).

### Fase 1 — Registro y autenticación (≈1 semana)
1. **Limpieza y paso a MVC**: eliminar `src/Local133.CustomerPortal` (y su `.esproj` de la solución), el target `BuildSpaForPublish` / `SkipSpaBuild` del `.csproj`, el job `spa` de `.github/workflows/ci.yml` y el `MapFallbackToFile("index.html")`. Renombrar `Local133.Api` → `Local133.Web` (carpeta, `.csproj`, namespaces, `.sln`, CI, README). `AddControllersWithViews` + `MapRazorPages`; `_Layout` Bootstrap 5 (LibMan) móvil primero; áreas `Merchant` y `Admin`; rutas `/api/*` desconocidas siguen respondiendo 404 ProblemDetails. ✅ *(hecho 2026-11-09)* — [bitácora](log_plan/fase-1.1-paso-a-mvc.md)
2. **Scaffold de Identity** (`dotnet aspnet-codegenerator identity`) sobre `AppDbContext` + roles, solo con las páginas necesarias (Register, Login, Logout, ConfirmEmail, ForgotPassword, ResetPassword, ExternalLogin, Manage); traducir al español y adaptarlas al layout. Registro con correo/contraseña, verificación de correo, login, logout, recuperación. ✅ *(hecho 2026-11-09)* — [bitácora](log_plan/fase-1.2-identity.md)
3. Login externo Google y Facebook. ✅ *(hecho 2026-11-10)* — [bitácora](log_plan/fase-1.3-login-externo.md)
4. Al registrarse se crea `Merchant` en `PendingBankAccount`; casillas de aceptación de términos y aviso de privacidad en la página Register scaffoldeada. ✅ *(hecho 2026-11-10)* — [bitácora](log_plan/fase-1.4-merchant-y-terminos.md)
5. Inicio del portal cliente (área `Merchant`) con checklist de onboarding; seed del usuario admin. ✅ *(hecho 2026-11-11)* — [bitácora](log_plan/fase-1.5-inicio-y-admin.md)

### Fase 2 — Ventas (núcleo) (≈2.5 semanas)
1. **PeyClient** completo (`bank-account`, `codi/qr`, `codi/pn`, `transaction/*`) con enums y manejo de errores. ✅ *(hecho 2026-11-10)* — [bitácora](log_plan/fase-2.1-peyclient.md)
2. **Cuenta bancaria mínima**: formulario (titular, CLABE con validación, alias, tipo de titular) → `POST /bank-account`; pantalla de estatus. ✅ *(hecho 2026-11-11; «Verificar estatus» agregado el mismo día)* — [bitácora](log_plan/fase-2.2-cuenta-bancaria.md)
3. **Endpoint de webhook** con verificación de firma, `WebhookEvents`, manejo de `bank.account.updated` ⇒ `Active`. Job de sondeo de estatus. ✅ *(hecho 2026-11-12, junto con la 2.5)* — [bitácora](log_plan/fase-2.3-webhook-y-sondeo.md)
4. **Venta por monto directo**: campo de monto → crea `Sale` → `POST /codi/qr` (`generateImage: false`) → vista con el QR (dibujado en el navegador con QRCode.js a partir de `qrString`) y cuenta regresiva en JS. ✅ *(hecho 2026-11-11; campo Concepto obligatorio el 2026-11-12)* — [bitácora](log_plan/fase-2.4-venta-monto-directo.md)
5. **Confirmación en tiempo real**: `codi.qr.payment_received` → crea `Payment` (con comisión calculada; se retira en la [5.1](#fase-5--suscripciones-56-semanas)) → `Sale.Paid` → SignalR → pantalla de "Pago recibido". Expiración y cancelación de venta. ✅ *(hecho 2026-11-12; corrección de forwarded headers el 2026-11-13)* — [bitácora](log_plan/fase-2.5-tiempo-real.md)
6. **Cobro por celular**: `POST /codi/pn` + webhook `codi.pn.payment_received`. ✅ *(hecho 2026-11-13)* — [bitácora](log_plan/fase-2.6-cobro-por-celular.md)
7. **QR fijo del puesto**: generación única, vista imprimible (PDF/imagen con logo); cada pago recibido crea venta `OpenAmount`. Es función de Pro y no hace falta para lanzar las suscripciones: mientras no exista se muestra como «Próximamente». Si el vendedor baja al gratuito, el QR impreso sigue cobrando (Pey no lo anula): esos pagos se registran y cuentan para el límite. *(2026-11-18)*
8. **Historial de ventas** + resumen del día; **recibo compartible** (URL pública por token, botón WhatsApp). ✅ *(hecho 2026-11-14, junto con la 3.3)* — [bitácora](log_plan/fase-2.8-historial-y-recibo.md)
9. Job de conciliación (`GET /transaction/codi/qr/{idc}`) para ventas pendientes. ✅ *(hecho 2026-11-18; también cobros por celular con `GET /transaction/{id}`)* — [bitácora](log_plan/fase-2.9-conciliacion.md)
10. Bloqueo: solo vendedores `Active` pueden generar cobros. ✅ *(hecho con la 2.4: `Merchant.CanCharge`; la 5.5 suma los límites del plan)* — [bitácora](log_plan/fase-2.4-venta-monto-directo.md)

### Fase 3 — Productos y carrito (≈1.5 semanas)
1. CRUD de productos con baja lógica, foto. ✅ *(hecho 2026-11-14; búsqueda asíncrona el mismo día)* — [bitácora](log_plan/fase-3.1-productos.md)
2. Pantalla de venta con catálogo: búsqueda, agregar/quitar, cantidades, total → mismo flujo de QR. `SaleItems` guarda snapshot de precio. ✅ *(hecho 2026-11-14)* — [bitácora](log_plan/fase-3.2-carrito.md)
3. Detalle de venta con productos en historial y recibo. ✅ *(hecho 2026-11-14, dentro de la 2.8)* — [bitácora](log_plan/fase-2.8-historial-y-recibo.md)

### Fase 4 — Mi información (≈4 días)
0. **Nombre en el registro** (obligatorio en las dos altas, en `Merchant.OwnerName`; saludo con el primer nombre en Inicio y en los correos). ✅ *(hecho 2026-11-23)* — [bitácora](log_plan/fase-4.0-nombre-en-registro.md)
1. Datos generales: nombre (el capturado en la 4.0), nombre del negocio, dirección, teléfono, email, logo y foto del local. El logo y la foto se usan en el recibo con marca y en el catálogo público (5.10). ✅ *(hecho 2026-11-23; el teléfono es el celular de la cuenta de Identity, que sale de «Mi cuenta»; logo PNG con transparencia; paso opcional en Inicio)* — [bitácora](log_plan/fase-4.1-mi-informacion.md)
2. Cuenta bancaria completa: historial de estatus, mensaje de error, reintento/cambio de cuenta (invalida y regenera el QR fijo). El cambio respeta la CLABE única (5.2).

### Fase 5 — Suscripciones (≈5–6 semanas)
*(Cambio 2026-11-18: reemplaza «Estados de cuenta y cobranza», que no se llegó a construir. Planes, reglas, datos y detalle de cada subfase en [actualizaciones/suscripciones_2026_11_18.md](actualizaciones/suscripciones_2026_11_18.md).)* La 5.1 y la 5.2 no dependen de la 2.7 ni de la Fase 4, así que se pueden adelantar.

1. **Retirar el modelo de comisión**: dominio, base (migración `RemoveCommission`), detalle de venta, textos legales, pruebas y docs; `bankAccountId` obligatorio en QR y push. ✅ *(hecho 2026-11-18)* — [bitácora](log_plan/fase-5.1-retiro-comision.md)
2. **CLABE única**: se revisa antes de llamar a Pey, con índice único filtrado (`CodiStatus <> 'Error'`) y un mensaje para aclaraciones. La migración se detiene si hay CLABEs repetidas, y la de prueba lo está en las bases de desarrollo. — [detalle](actualizaciones/suscripciones_2026_11_18.md#52-clabe-única)
3. **Planes y vigencia**: estados `Onboarding`, `Trial`, `Pro`, `Grace` y `Free`, calculados con la historia de periodos; prueba de 30 días al activarse la cuenta; precios y parámetros en `SystemSettings`. La migración les da la prueba a los vendedores activos. ✅ *(hecho 2026-11-19; periodos al instante, en hora de México)* — [bitácora](log_plan/fase-5.3-planes-y-vigencia.md)
4. **Avisos en el sitio y correos**: campana, lista y banner, idempotentes por `DedupKey`, con correo opcional por tipo. La base (tabla `Notifications` e `INotificationService` con correo) ya existe desde la 5.7; falta la campana, `/app/avisos`, el banner genérico y conectar los avisos de 80/100 y del pago abonado. — [detalle](actualizaciones/suscripciones_2026_11_18.md#54-avisos-en-el-sitio-y-correos)
5. **Límites del gratuito**, validados en el servidor:
   - catálogo de solo lectura, carrito bloqueado, recibo sin WhatsApp e historial de 30 días;
   - 100 cobros concretados al mes, sin cortesía, con contador visible y avisos al 80 % y al 100 %;
   - tope de cobros generados.

   ✅ *(hecho 2026-11-19; los 5 cobros de cortesía se quitaron el 2026-11-20; los avisos de 80 y 100 solo van al log hasta la 5.4, y «Mi plan» es provisional hasta la 5.6)* — [bitácora](log_plan/fase-5.5-limites-del-gratuito.md)
6. **Mi plan y pago con link de Pey**:
   - paquetes de 1, 3 y 6 meses y anual, con precio de fundador y anual congelado;
   - un link por cargo;
   - se abona solo con el webhook `payment_link.status_updated` o con una consulta a Pey, y hay conciliación;
   - reutiliza el backend de la [PoC](log_plan/poc-link-de-pago.md) y borra su pantalla.

   ✅ *(hecho 2026-11-20; sin el barrido nocturno de links, que se pospuso)* — [bitácora](log_plan/fase-5.6-mi-plan-y-pago.md)
7. **Recordatorios, gracia y baja al gratuito**: job diario (sin crear links: el link nace cuando el vendedor elige paquete) con avisos 15 (anual) y 3 días antes, el día del vencimiento y el día 3 de la gracia, fin de prueba y baja al gratuito. ✅ *(hecho 2026-11-21; con la base de los avisos de la 5.4 y el resumen del día 1 solo en el log hasta la 5.10)* — [bitácora](log_plan/fase-5.7-recordatorios-y-gracia.md)
8. **Afiliados**:
   - código y liga por vendedor, capturados en el registro;
   - 30 % de un mes para el referido en su primer pago;
   - 10 días para el que refiere cuando el referido paga, hasta 12 en 12 meses.

   ✅ *(hecho 2026-11-23; sin la CLABE única de la 5.2; premio pendiente si quien invita sigue en el onboarding)* — [bitácora](log_plan/fase-5.8-afiliados.md)
9. **Pro de lanzamiento**: historial completo con búsqueda y exportación a Excel, y corte de caja diario. ✅ *(hecho 2026-11-23; Excel con DocumentFormat.OpenXml)* — [bitácora](log_plan/fase-5.9-pro-de-lanzamiento.md)
10. **Valor visible**:
    - recibo con marca: logo en Pro, y en el gratuito «Cobrado con Local133» con el código del vendedor;
    - catálogo público con liga;
    - «lo que te pierdes» y resumen mensual.

    ✅ *(hecho 2026-11-24; la marca del recibo sigue el plan al abrir la liga, «Cobrado con Local133» lleva a la portada con el código y el resumen va solo a quien cobró en el mes)* — [bitácora](log_plan/fase-5.10-valor-visible.md)
11. **Precios y textos legales**: planes en la portada, con «Próximamente» y lugares de fundador; términos con uso justo, referidos, fundador y cancelación; aviso de privacidad; revisión legal. ✅ *(textos legales hechos el 2026-11-24, versión `2026-11-24`, con marcadores para el responsable y el contacto y pendientes de revisión legal; la sección «Planes» de la portada quedó pendiente y se hizo el 2026-11-25 con la portada nueva, con los precios escritos en la vista)* — [bitácora](log_plan/fase-5.11-textos-legales.md) · [portada nueva](log_plan/portada-nueva.md) · el 2026-11-25 los textos legales se movieron a la portada (el portal redirige con 301), ver [portada nueva](log_plan/portada-nueva.md#textos-legales-en-la-portada-2026-11-25)

### Fase 6 — Portal administrador (≈1.5 semanas)
1. Listado de clientes con búsqueda y filtros (estatus, plan, fecha, vencimiento); detalle con pestañas General / Cuenta / Ventas / Plan. ✅ *(hecho 2026-11-23; solo lectura, la pestaña Plan con el resumen)* — [bitácora](log_plan/fase-6.1-clientes.md)
2. **Plan por cliente**: periodos, cargos y pagos de la suscripción, y referidos. Acciones: regalar días, registrar un pago manual y anular un premio. **Parámetros globales**: precios y fundador, días de prueba y de gracia, límites del gratuito, uso justo, premios de referidos, IVA y expiración del QR. Todo con auditoría. ✅ *(hecho 2026-11-24; el pago manual solo abona cargos que ya existen y cuenta igual que uno de Pey; anular un premio quita los días que no se han usado y recorre lo que sigue; revisado en navegador)* — [bitácora](log_plan/fase-6.2-plan-y-parametros.md)
3. Cobranza: cargos `partial` o con monto menor, links pagados sin abonar, CLABE en disputa (ver quién la tiene y liberarla), suspender/reactivar.
4. Tablero (GMV, ingresos por suscripción, vendedores por plan, conversión de prueba a pago, lugares de fundador), transacciones globales, bitácora de webhooks con reproceso. ✅ *(hecho 2026-11-25, antes que la 6.3; transacciones = ventas + pagos de la suscripción; payload enmascarado; el reproceso no llama a Pey; también la alerta de uso justo de la 5.5; el tablero va en caché 5 min en `/admin/tablero` y la entrada del admin es Clientes)* — [bitácora](log_plan/fase-6.4-tablero-y-webhooks.md)

*(Cambio 2026-11-18: antes, comisión por cliente, estados de cuenta y condonación.)*

### Fase 7 — Endurecimiento y salida (≈1 semana)
Rate limiting en generación de cobros y de links de pago · pruebas de carga del webhook · respaldos · monitoreo y alertas (fallos de webhook, errores Pey) · despliegue a producción (HTTPS público para el webhook, registrar URL y secreto en portal.mipey.mx; declarar la IP o red del proxy en `ForwardedHeaders:KnownProxies`/`KnownNetworks`, o el sitio redirigirá con 307 lo que el proxy le entregue por HTTP) · prueba piloto con 3–5 vendedores.

**Total estimado:** ≈14–15 semanas para una persona. *(2026-11-18: la Fase 5 pasó de ≈1.5 a ≈5–6 semanas con las suscripciones.)*

---

## Verificaciones pendientes

Pendientes abiertos de las subfases terminadas (el detalle sigue en cada bitácora). Al cerrar uno: quitarlo de aquí y anotar el resultado en su bitácora.

**Pruebas reales contra Pey** (producción, $0.01 y CLABE de prueba `012180015826225713`; requieren confirmación):
- [2.2](log_plan/fase-2.2-cuenta-bancaria.md): alta real de la cuenta bancaria y una consulta real de «Verificar estatus».
- [2.3](log_plan/fase-2.3-webhook-y-sondeo.md): registrar la URL pública y el secreto en portal.mipey.mx; recibir un `bank.account.updated` real.
- [2.4](log_plan/fase-2.4-venta-monto-directo.md): cobro real por QR escaneado con app bancaria; ver cómo muestra la app un concepto largo y si los acentos llegan al `qrString` (BOM de QRCode.js).
- [2.5](log_plan/fase-2.5-tiempo-real.md): pago real con el webhook por el túnel HTTPS. La venta 24 ya se concilió en la [2.9](log_plan/fase-2.9-conciliacion.md#conciliación-real-contra-pey-2026-11-18-confirmada-por-el-usuario).
- [2.6](log_plan/fase-2.6-cobro-por-celular.md): cobro real por push a un celular con CoDi activo y su webhook.
- [3.2](log_plan/fase-3.2-carrito.md): cobro real con carrito (QR y celular) y su webhook.
- [5.8](log_plan/fase-5.8-afiliados.md): un referido paga un link real ⇒ 10 días al que lo invitó (con $0.01 el descuento redondea a $0.00).
- [5.6](log_plan/fase-5.6-mi-plan-y-pago.md): ~~pagar un link real con $0.01~~ hecho el 2026-11-20 (se abonó por la consulta de la página de regreso; cubre el pendiente de la [PoC](log_plan/poc-link-de-pago.md)). Falta ver llegar el webhook `payment_link.status_updated` real con túnel + `App:PublicBaseUrl`.

**Confirmar con soporte de Pey** (ver también §6):
- [2.4](log_plan/fase-2.4-venta-monto-directo.md): límite del `concept` y que `qrString` sea solo ASCII.
- [2.5](log_plan/fase-2.5-tiempo-real.md): tolerancia del timestamp del webhook, política de reintentos y si un QR con monto acepta varios pagos.
- [2.6](log_plan/fase-2.6-cobro-por-celular.md): qué responde si el celular no tiene CoDi (¿400 o 200 sin que llegue nada?) y si hay aviso de rechazo del pagador.
- [2.9](log_plan/fase-2.9-conciliacion.md): límites de peticiones (la conciliación hace unas 4 consultas por venta no pagada).
- [PoC link de pago](log_plan/poc-link-de-pago.md): cómo dirigir el dinero del link a la CLABE del vendedor (no acepta `bankAccountId`); si el pagador ve algún concepto; el monto mínimo con tarjeta; el payload de `payment_link.status_updated`. *(2026-11-18: para la suscripción el dinero debe caer en la cuenta de la plataforma, así que dirigirlo al vendedor ya no hace falta. El payload del webhook está documentado en docs.pey.mx; ver el [hallazgo 13](#2-hallazgos-de-la-api-de-pey-afectan-el-diseño).)*

**Revisión en navegador sin hacer** (no hay evidencia de una revisión posterior que la cubra):
- [1.4](log_plan/fase-1.4-merchant-y-terminos.md): la validación en cliente bloquea el registro con la casilla de términos sin marcar.
- [1.5](log_plan/fase-1.5-inicio-y-admin.md): Inicio del vendedor (`/app`) a 375 px.
- [2.2](log_plan/fase-2.2-cuenta-bancaria.md): formulario de cuenta bancaria (validación de CLABE en cliente) y «Verificar estatus», a 375, 768 y 1280 px con el MCP de Playwright.

**Otros**:
- [1.3](log_plan/fase-1.3-login-externo.md): prueba real con credenciales de Google/Facebook (crear clientes OAuth y registrar los callbacks `https://localhost:7180/signin-google` y `/signin-facebook`). Con la [4.0](log_plan/fase-4.0-nombre-en-registro.md): ver en el navegador qué nombre propone cada proveedor en el alta.
- [2.3](log_plan/fase-2.3-webhook-y-sondeo.md): correo de cuenta **rechazada** (Notificaciones). El de cuenta activada ya está hecho ([bitácora](log_plan/mantenimiento-correo-cuenta-activa.md)).
- [Correo de cuenta activa](log_plan/mantenimiento-correo-cuenta-activa.md) (con el logo nuevo de los [ajustes 2026-11-26](log_plan/ajustes-2026-11-26-marca-y-navegacion.md), `LogoVersion` 2): verlo en buzones reales (Gmail en Android y en web, Outlook, modo oscuro), con túnel + `App:PublicBaseUrl` o desde producción, porque el logo no carga desde `localhost`. Requiere confirmación para el envío real.
- [3.1](log_plan/fase-3.1-productos.md): «Tomar foto» en un celular real (Android e iPhone), también la foto del local de la [4.1](log_plan/fase-4.1-mi-informacion.md); confirmar que CI (Ubuntu) carga los binarios nativos de SkiaSharp; en producción, `LocalDiskFileStorage` ⇒ blob (Fase 7).
- [2.8](log_plan/fase-2.8-historial-y-recibo.md): confirmar en Windows que los botones de WhatsApp abren la app de escritorio (`whatsapp://`); abrir el recibo y los botones de WhatsApp («al cliente» y «a otro contacto») y «Compartir» en un celular real (Android e iPhone); cómo previsualiza WhatsApp la liga. En producción fijar `App:PublicBaseUrl` (ver `azure.md`).
- [Correo con Resend](log_plan/mantenimiento-resend.md): restringir la llave a `notificaciones.local133.com.mx` en el panel de Resend (la API no cambia permisos), crear la de producción para Key Vault (ver `azure.md`) y repetir el correo real en un buzón propio con remitente `hola@` y liga que no sea `localhost` (túnel o producción); el primero llegó a spam. Decidir el buzón de las respuestas a `hola@` (el dominio no tiene MX).
- [2.9](log_plan/fase-2.9-conciliacion.md): barrido nocturno con `GET /transaction/bank-account/{id}` (pagos sin venta pendiente: adicionales, push sin transacción confirmada, ventas de más de 24 h). Pospuesto. El reproceso de webhooks del admin ya existe ([6.4](log_plan/fase-6.4-tablero-y-webhooks.md)), pero solo cubre lo que sí llegó.
- [5.5](log_plan/fase-5.5-limites-del-gratuito.md): avisos en el sitio y correos de 80 y 100 cobros (los conecta la 5.4 reemplazando `LoggingChargeLimitNotifier`). La alerta del uso justo de Pro ya está en el tablero ([6.4](log_plan/fase-6.4-tablero-y-webhooks.md)).
- [5.6](log_plan/fase-5.6-mi-plan-y-pago.md): barrido nocturno de links con `GET /transaction/api-key` (pospuesto; cubriría links de más de 60 días pagados con el webhook perdido); lista de cobranza con los cargos `Partial` y los pagos extra (6.3; el abono a mano de un cargo ya está en la 6.2); aviso en el sitio del pago abonado (5.4); confirmar con Pey el `fee` con tarjeta, `applicationFee`, `partial`, si un link puede ofrecer solo CoDi y si `externalMetadata` siempre viaja en el webhook.
- [5.8](log_plan/fase-5.8-afiliados.md): sin la CLABE única (5.2) alguien puede abrir cuentas con su misma CLABE e invitarse; revisar en el navegador el aviso pendiente y el premio al activarse.
- [5.7](log_plan/fase-5.7-recordatorios-y-gracia.md): ver correr `suscripciones-diario` en Hangfire a las 9:00 y un recordatorio real en un buzón (requiere confirmación). La campana y `/app/avisos` que muestran sus avisos llegan con la 5.4 (el contenido del resumen mensual ya llegó con la 5.10).
- [5.9](log_plan/fase-5.9-pro-de-lanzamiento.md): abrir el `.xlsx` exportado en Excel de escritorio, en el celular (Excel o Google Sheets) y en Numbers; imprimir el corte de caja en papel o PDF (Android, iPhone, Windows) y mandarlo por WhatsApp real.
- [5.10](log_plan/fase-5.10-valor-visible.md): resumen mensual enviado a Outlook el 2026-11-24 (falta revisar cómo se ve, Gmail, modo oscuro y el envío del job el día 1); catálogo y recibo con marca en un celular real y la vista previa de WhatsApp de la liga del catálogo; el aviso del resumen en la campana (5.4).
- [5.11](log_plan/fase-5.11-textos-legales.md):
  - ~~sección «Planes» de la portada~~: hecha con la [portada nueva](log_plan/portada-nueva.md); sus pendientes van abajo;
  - revisión legal de Términos y Aviso de privacidad, y razón social, domicilio y correo de contacto en lugar de los marcadores (con un buzón que reciba correo);
  - eliminación de cuentas tras 2 años sin actividad: el aviso lo promete y no hay job;
  - cambiar desde el admin un parámetro que aparece en los Términos exige publicar una versión nueva: la pantalla de la 6.2 lo advierte, pero no lo impide;
  - decidir si se pide reaceptar a quien tenga la versión `2026-11-18`.
- [6.2](log_plan/fase-6.2-plan-y-parametros.md): anular un premio y «Dar premios pendientes» en el navegador (la base de desarrollo no tiene premios; los cubren las pruebas de integración).
- [Portada nueva](log_plan/portada-nueva.md):
  - «Quedan N lugares» de fundador está oculto: falta de dónde saca el dato la portada, que no tiene base;
  - los precios están escritos a mano en `_Pricing.cshtml`: si se cambian desde el admin (6.2), hay que cambiar la vista;
  - columna «Contacto» del pie: falta un correo que reciba y un WhatsApp;
  - confirmar con Pey o con el abogado «con CoDi® o tarjeta» (posible comisión con tarjeta) y «Precio congelado si renuevas a tiempo»;
  - el video del hero: reemplazar `_HeroPhone.cshtml`;
  - revisar en un celular real los carruseles, el menú y las fuentes.
- [6.4](log_plan/fase-6.4-tablero-y-webhooks.md): la búsqueda dentro del payload de la bitácora no tiene índice (columnas propias para los ids de Pey si se vuelve lenta).
- [Migración a .NET 10](log_plan/mantenimiento-net10.md): cambiar la pila del App Service a .NET 10 antes del próximo despliegue (ver `azure.md`) y confirmar que CI (GitHub Actions, Ubuntu) pasa con el SDK 10.0.401.

---

## 6. Riesgos y pendientes por confirmar

- **Con soporte de Pey**: ¿un QR con monto puede pagarse más de una vez?, ¿hay webhook de rechazo/expiración de push?, política de reintentos de webhooks, límites de cuentas por API key y de peticiones, fecha del sandbox. Del link de pago de la suscripción: quién paga el `fee` con tarjeta y qué es `applicationFee`; si un link puede ofrecer solo CoDi; qué es `partial` y si un link pagado acepta otro pago; si los links expiran o se pueden desactivar; reintentos de `payment_link.status_updated`; si Pey acepta la misma CLABE dos veces ([detalle](actualizaciones/suscripciones_2026_11_18.md#11-por-confirmar)).
- **Espera de 5 días hábiles** para activar: principal fricción de adopción; mitigar con comunicación clara y notificación inmediata. La prueba de 30 días empieza al activarse la cuenta, así que la espera no le quita días.
- **Cobranza**: la palanca es la baja al gratuito, que no le quita el cobro básico. Riesgo: que el gratuito alcance y nadie pague; en el piloto, medir la conversión de prueba a pago y cuánto se usa el límite. Los links de Pey no expiran ni se cancelan: un pago tardío siempre se abona. *(Cambio 2026-11-18: antes, la palanca era la suspensión.)*
- **Fiscal/legal** (validar con contador/abogado): CFDI de la suscripción (fuera de esta implementación; mientras tanto, factura global), precio con IVA incluido, términos (uso justo, referidos, fundador, cancelación y reembolsos), aviso de privacidad (referidos y retención de cuentas inactivas); resguardo de CLABE y datos personales.
- **Abuso del gratuito** con varias cuentas: CLABE única, y premios de referidos solo cuando el referido paga.
- **Tarjeta en el link de pago**: si Pey le cobra la comisión al pagador, con tarjeta se paga más que el precio «IVA incluido». Avisarlo en «Mi plan» o pedir que el link ofrezca solo CoDi.
- **Dependencia de un solo proveedor**: aislar Pey tras `IPaymentGateway` para poder cambiar o agregar otro.

---

## 7. Verificación

- **Unitarias**: dígito verificador de CLABE; vigencia de la suscripción (prueba, gracia, periodos encadenados, bordes de mes y zona horaria); precios (paquetes, fundador, descuento de bienvenida); límite mensual del gratuito; tope de premios de referidos; transiciones de estatus. *(2026-11-18: sin comisión ni corte semanal.)*
- **Integración**: webhook con firma válida/ inválida/duplicada; webhook `payment_link.status_updated` (`paid`, `processing`, `partial`, repetido, link viejo); bloqueos del gratuito en el servidor; CLABE única; `PeyClient` contra servidor simulado (WireMock) con cada `error.type`; aislamiento entre vendedores; páginas de Identity y áreas `Merchant`/`Admin` accesibles solo con el rol correcto (redirección a login / 403).
- **Extremo a extremo (producción Pey, $0.01)**:
  1. Registrarse → capturar CLABE de prueba `012180015826225713` → recibir `bank.account.updated` → estatus `Active`.
  2. Venta por monto directo → escanear QR con app bancaria → pantalla cambia a "Pago recibido" sin recargar → aparece en el historial.
  3. Venta con carrito, cobro por celular y QR fijo.
  4. Con el precio de prueba en $0.01, pagar un link de suscripción con CoDi → webhook → Pro. Simular el vencimiento → gracia → baja al gratuito (catálogo de solo lectura, carrito bloqueado) → pago → Pro desde el día del pago.
  5. Afiliados: registrarse con el código de otro vendedor → primer pago con 30 % de descuento → 10 días más para el que refirió. Admin: cambiar un precio → solo afecta los links nuevos.
- Exponer el webhook en local con un túnel HTTPS (ngrok / dev tunnels) y revisar "Webhook Logs" en el portal de Pey.
