# Plan de desarrollo — Portal de cobros CoDi para vendedores informales (Pey API)

## 1. Contexto

Plataforma para que vendedores informales (tianguis, mercados, bazares, puestos) cobren con CoDi usando la API de Pey. El vendedor se registra, da de alta su cuenta bancaria, y cobra generando un QR (carrito o monto directo). El negocio cobra una comisión configurable (3% por defecto) mediante estados de cuenta semanales.

Repositorio vacío (`c:\personalprojects\local133`): proyecto desde cero.

**Decisiones confirmadas**
- **ASP.NET Core 9 MVC** (vistas Razor + Bootstrap 5, móvil primero) en un solo proyecto web que también expone la API REST interna, el webhook y el hub SignalR; portal cliente y admin como áreas MVC. *(Cambio 2026-11-09: reemplaza la SPA React original.)*
- **SQL Server** con EF Core 9.
- Estado de cuenta semanal se paga con **QR CoDi hacia la cuenta de la plataforma**, conciliado por webhook; falta de pago ⇒ suspensión.
- Además del QR dinámico: **QR fijo del puesto**, **cobro por número celular** y **recibo compartible**.

---

## 2. Hallazgos de la API de Pey (afectan el diseño)

Base URL `https://api.pey.mx` · Auth `Authorization: Bearer <api-key>` · Sin sandbox (se prueba en producción con $0.01).

| # | Hallazgo | Consecuencia |
|---|---|---|
| 1 | Pey deposita **directo a la CLABE del vendedor**; no hay split de pagos | La comisión del 3% no se retiene: se factura aparte (estado de cuenta). La plataforma nunca toca el dinero del vendedor. |
| 2 | Una sola API key de la plataforma registra N cuentas (`POST /bank-account`) y cada cobro elige destino con `bankAccountId` | Modelo multi-vendedor con **una API key y un webhook** (Pey permite 1 webhook por API key). |
| 3 | Alta de cuenta tarda **hasta 5 días hábiles** (Banxico) | Pantalla de "cuenta en revisión" + notificación al activarse. El vendedor puede cargar productos mientras tanto. |
| 4 | Webhook `bank.account.updated` **solo se envía al pasar a `ACTIVE`** | `ERROR` (y `errorMessage`) solo se detecta con **polling** a `GET /bank-account/{id}` ⇒ job en segundo plano. |
| 5 | `amount: 0` en `POST /codi/qr` = el pagador escribe el monto; `expiration: 0` = no expira | Habilita el **QR fijo imprimible**. |
| 6 | `expiration` en segundos: `0` o **mínimo 360** | QR dinámico de venta con 600 s; no se puede menos de 6 min. |
| 7 | `numRef` máx. 7 dígitos; `metadata` hasta 50 pares escalares (vuelven como string en `metadata.externalMetadata`) | `numRef` = folio corto de venta; `metadata` = `{kind, merchantId, saleId \| statementId}` para conciliar. |
| 8 | Webhooks firmados con **Standard Webhooks** (`webhook-id`, `webhook-timestamp`, `webhook-signature`; se valida el body crudo) | Verificar firma + idempotencia por `webhook-id` y por `transactionId`. |
| 9 | CLABE de prueba que activa de inmediato: `012180015826225713` (Ramón Orraca Lascurain); pagos no se devuelven | Usarla en desarrollo con montos de $0.01. |
| 10 | `POST /bank-account` exige `alias`, `name`, `clabe`, `entity`; no acepta `taxId` | El formulario necesita **tipo de titular (persona/empresa)**, que no estaba en la idea original. |
| 11 | No está documentado webhook de rechazo/expiración de push ni si un QR puede pagarse más de una vez | Modelar **Pagos separados de Ventas** (un QR ⇒ 0..N pagos) y expirar ventas por tiempo. Confirmar con soporte de Pey. |

### Enumeradores de Pey (para `Domain/Pey`)

```csharp
enum PeyCodiStatus        { PENDING, PROCESSING, ACTIVE, ERROR }   // estatus cuenta bancaria
enum PeyEntity            { BUSINESS, PERSON }
enum PeyTransactionStatus { PENDING, PAID }
enum PeyPaymentMethod     { CARD, CODI }
enum PeyPayableType       { PAYMENT_LINK, API_CODI_QR, API_CODI_PN }
enum PeyPaymentLinkStatus { pending, processing, paid, partial }   // futuro (tarjetas)
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
enum StatementStatus { Open, Issued, Paid, Overdue, Cancelled }
enum SuspensionReason{ UnpaidStatement, Admin, BankAccountError }
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
| Confirmar pago | Webhooks `codi.qr.payment_received` (match por `codiQrId`) y `codi.pn.payment_received` (match por `transactionId`) |
| Respaldo si se pierde webhook | `GET /transaction/codi/qr/{idc}` para ventas pendientes; nocturno `GET /transaction/bank-account/{id}` |
| Pago de estado de cuenta | `POST /codi/qr` **sin** `bankAccountId` (cae en la cuenta de la plataforma), `metadata:{kind:"statement", statementId}` |
| Detalle / soporte en admin | `GET /transaction/{id}`, `GET /transaction/api-key` |

---

## 3. Evaluación de los módulos

**Los módulos propuestos son correctos.** Ajustes y faltantes:

**Ajustes a lo propuesto**
- *Cuenta bancaria*: agregar **tipo de titular (Persona/Empresa)**; `nombre del negocio` se envía como `alias`. Mostrar estatus y mensaje de error; permitir reintento/cambio de CLABE. Validar dígito verificador de la CLABE antes de enviar.
- *Estados de cuenta*: agregar **pago del estado de cuenta** (QR CoDi), fecha límite y detalle de movimientos que lo componen.
- *Comisión*: guardar la tasa **en cada pago** al momento de cobrarse (cambiar la comisión no altera lo ya vendido). Jerarquía: tasa del vendedor ?? tasa global. Definir si el 3% es **más IVA** (parámetro).
- *Registro*: sumar verificación de correo, recuperación de contraseña y aceptación de términos/aviso de privacidad.

**Módulos que faltan (necesarios para el MVP)**
1. **Historial de ventas / inicio** (cliente): ventas del día, lista con filtros, detalle, estado del pago. Sin esto el vendedor no puede verificar cobros.
2. **Onboarding / estatus de activación**: checklist visible (perfil → cuenta bancaria → en revisión → activo).
3. **Notificaciones** (email; WhatsApp después): cuenta activada/rechazada, estado de cuenta emitido, por vencer, suspensión.
4. **Cobranza y suspensión** (admin + job): vencidos, suspender/reactivar, marcar pagado/condonar manualmente.
5. **Admin – transacciones y monitoreo**: ventas globales, bitácora de webhooks, reintento de conciliación, tablero (GMV, comisión devengada/cobrada, vendedores por estatus).
6. **Admin – usuarios y auditoría**: roles admin, registro de quién cambió comisiones/estatus.

**Módulos para después (fuera del MVP)**
Categorías e inventario · cajeros/empleados por negocio · CFDI de la comisión · exportación a Excel · links de pago con tarjeta (Pey Payment Link) · PWA instalable con modo offline para catálogo.

---

## 4. Arquitectura

```
Local133.sln
src/
  Local133.Domain          entidades, enums (propios y Pey), reglas (comisión, CLABE)
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
- **Jobs (Hangfire, almacenamiento SQL Server)**: sondeo de cuentas bancarias · expirar ventas · conciliación de pagos · corte semanal (lunes 00:05 `America/Mexico_City`) · vencimiento y suspensión.
- **Archivos**: `IFileStorage` (disco local en dev, blob en prod), redimensionado con ImageSharp.
- **Secretos**: API key y secreto de webhook de Pey en user-secrets / variables de entorno. CLABE enmascarada en UI.

### Modelo de datos (tablas principales)

`Merchants` (perfil, estatus, `CommissionRateOverride`) · `BankAccounts` (titular, CLABE, alias, entity, `PeyBankAccountId`, `CodiStatus`, `ErrorMessage`) · `Products` (nombre, descripción, precio, foto, `IsDeleted`) · `Sales` (folio, método, origen, monto, estatus, `PeyQrId`, `QrString`, `PeyTransactionId`, expiración, token público de recibo) · `SaleItems` (snapshot de nombre/precio/cantidad) · `Payments` (`PeyTransactionId` único, `SaleId`, monto, `DatePaid`, `TrackingKey`, `CommissionRate`, `CommissionAmount`, `StatementId`) · `StaticQrs` · `Statements` (periodo, subtotal, IVA, total, estatus, vencimiento, `PeyQrId`, `PaidAt`) · `SystemSettings` (comisión global, IVA, días de gracia) · `WebhookEvents` · `AuditLogs` · tablas de Identity.

---

## 5. Fases de desarrollo

Cada fase termina con algo demostrable. Ventas va inmediatamente después del registro; lo mínimo de cuenta bancaria se construye dentro de la fase de ventas porque es prerrequisito para generar un QR.

### Fase 0 — Cimientos (≈3 días)
1. ✅ Solución .NET 9 con los 4 proyectos, EF Core + migración inicial, Serilog, ProblemDetails, Swagger. *(hecho 2026-11-09: migración `Initial` = `SystemSettings` + semilla; API con controladores; build sin advertencias. Prueba de arranque verificada el mismo día: Swagger, `/api/health` y 404 ProblemDetails.)*
2. ✅ Base de datos en el **SQL Server local del equipo** (sin Docker): crear la base `Local133`, cadena de conexión en user-secrets (`ConnectionStrings:Default`, p. ej. `Server=localhost\SQLEXPRESS;Database=Local133;Trusted_Connection=True;TrustServerCertificate=True`) y aplicar migraciones con `dotnet ef database update`; `README` con arranque local y requisitos (instancia de SQL Server instalada); CI (build + tests). *(hecho 2026-11-09: base `Local133` en `localhost\SQLEXPRESS` con `Initial` y semilla; `README.md`; GitHub Actions `.github/workflows/ci.yml` con job .NET (build Release, `dotnet ef migrations has-pending-model-changes`, `dotnet test`). Los proyectos de prueba se crean en Fase 1 (CI no usa SQL Server todavía).)*

> Nota (2026-11-09): el antiguo punto 0.2 (SPA React en `src/Local133.CustomerPortal`) se construyó y quedó **descartado** al cambiar el frontend a MVC Razor. Su código, el target `BuildSpaForPublish` y el job `spa` de CI se eliminaron en el paso 1.1.

### Fase 1 — Registro y autenticación (≈1 semana)
1. **Limpieza y paso a MVC**: eliminar `src/Local133.CustomerPortal` (y su `.esproj` de la solución), el target `BuildSpaForPublish` / `SkipSpaBuild` del `.csproj`, el job `spa` de `.github/workflows/ci.yml` y el `MapFallbackToFile("index.html")`. Renombrar `Local133.Api` → `Local133.Web` (carpeta, `.csproj`, namespaces, `.sln`, CI, README). `AddControllersWithViews` + `MapRazorPages`; `_Layout` Bootstrap 5 (LibMan) móvil primero; áreas `Merchant` y `Admin`; rutas `/api/*` desconocidas siguen respondiendo 404 ProblemDetails. ✅ *(hecho 2026-11-09: rutas por atributo en español — `/`, `/app` (+`cobrar`, `ventas`, `mas`) y `/admin` (+`clientes`, `transacciones`, `parametros`) con las mismas pantallas placeholder de la SPA (parcial `_ComingSoon`); errores separados: `/api/*` ⇒ ProblemDetails, resto ⇒ vistas `/error` y `/error/{code}`; LibMan como herramienta local con Bootstrap 5.3.8, Bootstrap Icons, jQuery y jQuery Validation Unobtrusive, `wwwroot/lib` versionado; `MapStaticAssets` con fingerprint. Verificado arrancando el sitio: todas las rutas 200, `/api/nada` 404 problem+json, `/nada` 404 HTML, Swagger solo con `/api/health`. Sin guards por rol hasta 1.2.)*
2. **Scaffold de Identity** (`dotnet aspnet-codegenerator identity`) sobre `AppDbContext` + roles, solo con las páginas necesarias (Register, Login, Logout, ConfirmEmail, ForgotPassword, ResetPassword, ExternalLogin, Manage); traducir al español y adaptarlas al layout. Registro con correo/contraseña, verificación de correo, login, logout, recuperación. ✅ *(hecho 2026-11-09:*
   - *Generador `dotnet-aspnet-codegenerator` 9.0.0 (corrió bien). Se deshicieron su `AppDbContext`/`IdentityUser` propios y sus cambios en `Program.cs`/`appsettings`.*
   - *`ApplicationUser` y un `AppDbContext : IdentityDbContext<ApplicationUser>` en Infrastructure. Migración `Identity` con los roles `Merchant`/`Admin` sembrados con Id fijo.*
   - *Se usa `AddIdentity` sin `AddDefaultUI`: solo existen las páginas scaffoldeadas; 2FA y datos personales dan 404. Rutas por defecto `/Identity/Account/*`.*
   - *Páginas, validación y correos en español, más `SpanishIdentityErrorDescriber` y mensajes de model binding. Contraseña de 8+ caracteres con número; bloqueo tras 5 intentos.*
   - *El registro asigna el rol `Merchant`. Tras el login, sin `returnUrl`, Admin va a `/admin` y Merchant a `/app`.*
   - *Áreas con `[Authorize(Roles=…)]`. `/api/*` sin sesión responde 401/403 en vez de redirigir.*
   - *`FileEmailSender` en dev (`App_Data/emails` + log).*
   - *`tests/Local133.IntegrationTests` con 26 pruebas (`Category=SqlServer`, base `Local133_Tests`, Respawn), todas verdes en local. CI las excluye con `--filter "Category!=SqlServer"`.*
   - *Prueba de arranque del sitio real: redirección a login, registro y `.html` del correo generado, Swagger solo con `/api/health`.*
   - *El admin sembrado quedó en 1.5.)*
3. Login externo Google y Facebook. ✅ *(hecho 2026-11-10:*
   - *Las páginas `ExternalLogin` y `Manage/ExternalLogins` ya venían del scaffold de 1.2. Se agregaron los paquetes `Microsoft.AspNetCore.Authentication.Google`/`.Facebook` 9.0.22.*
   - *Los proveedores se registran solo si hay credenciales (`Authentication:Google:ClientId/ClientSecret`, `Authentication:Facebook:AppId/AppSecret` en user-secrets). Sin ellas no hay botones y CI/pruebas no dependen de ellos.*
   - *Reglas: si se conserva el correo del proveedor ⇒ cuenta confirmada y entra directo como `Merchant`. Si se escribe otro ⇒ correo de confirmación. Si el correo ya tiene cuenta ⇒ no se liga solo: mensaje para entrar con contraseña y ligar desde Mi cuenta → Cuentas externas.*
   - *Google se pide con `prompt=select_account`: después de cerrar sesión muestra "Elige una cuenta" en lugar de volver a entrar solo con la misma cuenta (teléfonos compartidos). No pide la contraseña.*
   - *Cancelar o fallar en el proveedor regresa a Login (o a Cuentas externas) con mensaje en español. Botones con icono (parcial `_ExternalProviderButton`).*
   - *12 pruebas nuevas: 9 de flujo con un proveedor falso (`FakeExternalAuthHandler`, `Category=SqlServer`) y 3 de registro condicional que corren en CI. 38/38 verdes en local.*
   - *Pendiente: prueba real con credenciales de Google/Facebook (crear clientes OAuth y registrar `https://localhost:7180/signin-google` y `/signin-facebook`).)*
4. Al registrarse se crea `Merchant` en `PendingBankAccount`; casillas de aceptación de términos y aviso de privacidad en la página Register scaffoldeada. ✅ *(hecho 2026-11-10:*
   - *Entidad `Merchant` (`Domain/Merchants`: `UserId` 1:1 con `AspNetUsers`, FK `Restrict`, índice único; `Status` como texto; `BusinessName` y `CommissionRateOverride` vacíos hasta Fases 4/6) y enum `MerchantStatus`.*
   - *Toda alta de vendedor pasa por `AccountRegistration` (Web/Identity): usuario + rol `Merchant` + cuenta externa + `Merchant` en **una transacción**; si algo falla no queda usuario sin Merchant.*
   - ***Una sola casilla** "Acepto los Términos y condiciones y el Aviso de privacidad" (base `LegalAcceptanceInput`, parcial `_AcceptLegalCheckbox`) en Register y en `/Identity/Account/ExternalLogin` (alta con Google/Facebook; quien ya tiene la cuenta ligada entra sin volver a aceptar). Validación `[MustBeTrue]` en servidor + adaptador en `wwwroot/js/validation.js`.*
   - *`AspNetUsers` guarda `TermsAcceptedAt`/`TermsVersion` y `PrivacyNoticeAcceptedAt`/`PrivacyNoticeVersion`; la versión vigente está en `Domain/Legal/LegalDocuments` (subirla al cambiar el texto).*
   - *Páginas públicas `/terminos` y `/aviso-de-privacidad` con texto **borrador pendiente de revisión legal**; ligas en el pie del layout.*
   - *Migración `Merchants` con backfill: los usuarios con rol `Merchant` que ya existían reciben su `Merchant` pendiente (sin términos aceptados).*
   - *7 pruebas nuevas (`MerchantRegistrationTests`); los helpers de prueba crean vendedores con `AccountRegistration`. 46/46 verdes en local (4 corren en CI).*
   - *Pendiente: probar en navegador que la validación en cliente bloquea el envío con la casilla sin marcar (el HTML ya emite `data-val-mustbetrue` y carga `validation.js`).)*
5. Inicio del portal cliente (área `Merchant`) con checklist de onboarding; seed del usuario admin. ✅ *(hecho 2026-11-11:*
   - *Regla pura `Domain/Merchants/OnboardingChecklist.For(MerchantStatus)`: 4 pasos (crear cuenta → cuenta bancaria → revisión de Banxico → listo para cobrar) con estado Done/Current/Pending. Para `Suspended`/`Disabled` la lista viene vacía. **Sin paso de perfil**: se agrega en la Fase 4 junto con la pantalla.*
   - *`/app` carga el `Merchant` del usuario. Muestra "Hola, {nombre del negocio o correo}" y:*
     - *en onboarding: tarjeta "Activa tu cuenta" con "Paso X de 4", barra de avance y el paso en curso con su descripción. El botón de cuenta bancaria dice "Disponible pronto" hasta 2.2;*
     - *`Active`: "Ya puedes cobrar" con botón a `/app/cobrar`;*
     - *`Suspended`/`Disabled`: aviso.*
   - *Los textos están en `Areas/Merchant/Models/MerchantHomeViewModel`. Atributos `data-merchant-status`, `data-step` y `data-state` para las pruebas.*
   - *Seed del admin al arrancar (`Web/Identity/AdminSeeder`) si existen `Seed:Admin:Email` y `Seed:Admin:Password` (user-secrets o variables de entorno). Crea el usuario con correo confirmado y rol `Admin` en una transacción. Es idempotente: si ya es Admin no cambia la contraseña. No promueve a un `Merchant` (log de error). A un usuario sin roles le agrega `Admin`. Si la contraseña no cumple la política no crea nada. Una falla se registra en el log y el sitio sigue arrancando.*
   - *Nuevo `tests/Local133.UnitTests` (Domain, corre en CI) con 12 pruebas del checklist. 11 de integración: Inicio por estatus + aislamiento entre dos vendedores (`MerchantHomeTests`) y casos del seeder + siembra real al arrancar (`AdminSeederTests`). La fábrica de pruebas vacía `Seed:Admin` para que los user-secrets no siembren nada. 129/129 verdes en local (76 en CI).*
   - *Prueba de arranque del sitio real con `Seed__Admin__*`: se creó el admin y su login va a `/admin`; un vendedor registrado y confirmado ve el paso 2 en curso; con `UPDATE Merchants SET Status=…` se vieron los otros cuatro estatus.*
   - *Pendiente: revisión visual en navegador a 375 px.)*

### Fase 2 — Ventas (núcleo) (≈2.5 semanas)
1. **PeyClient** completo (`bank-account`, `codi/qr`, `codi/pn`, `transaction/*`) con enums y manejo de errores. ✅ *(hecho 2026-11-10:*
   - *Contratos tomados del OpenAPI oficial (`https://docs.pey.mx/openapi.json`, versión 2026-10-20). Apareció un `error.type` nuevo: `INVALID_PARAMETERS` (plural, en los listados). `POST /codi/qr` responde 201; `codi/pn` y `bank-account`, 200.*
   - *`Domain/Pey`: enums en PascalCase (en el cable, `UPPER_SNAKE`), `PeyErrorType` (con `Unknown`), `PeyWebhookEventTypes` y `PeyLimits`.*
   - *`Application/Payments`: `IPaymentGateway` (8 operaciones), DTOs y excepciones `PaymentGatewayException` → `Validation` (+ `InvalidClabe`), `NotFound`, `Authentication`, `State` (`CONCURRENT_UPDATE` es transitorio) y `Unavailable`. Sin `error.type` reconocible se clasifica por código HTTP.*
   - *`Infrastructure/Pey`: `PeyClient` + `AddPeyGateway` (sección `Pey`; `Pey:ApiKey` en user-secrets, opcional al arrancar). `Microsoft.Extensions.Http.Resilience` 9.10.0 (pipeline estándar). **Reintentos por método**: GET en cualquier falla transitoria; POST solo en 503/429, porque Pey no tiene llave de idempotencia y un reintento tras un 500 o un timeout podría duplicar un QR o un push. Un timeout de POST se reporta como `Unavailable` con resultado incierto.*
   - *Las reglas de Pey se validan antes de llamar (`ArgumentException`): expiración 0/≥360, centavos, `numRef` ≤ 7 dígitos, celular de 10, CLABE de 18 dígitos (el dígito verificador llega en 2.2), metadata 50/40/500. Sin `bankAccountId` el campo se omite (cobro a la cuenta de la plataforma). Logs con `requestId`, sin bodies ni API key.*
   - *60 pruebas nuevas contra WireMock (`WireMock.Net.Minimal` 2.20.0) en `tests/Local133.IntegrationTests/Pey`: cuerpos y respuestas de cada operación, los 18 `error.type`, clasificación por código, reintentos GET/POST, timeout, cancelación, validación previa y falta de API key. No usan SQL Server, así que corren en CI. 106/106 verdes en local (64 en CI).*
   - *Sin llamadas reales a Pey todavía: la primera prueba en producción ($0.01, CLABE de prueba) llega con 2.2/2.4.)*
2. **Cuenta bancaria mínima**: formulario (titular, CLABE con validación, alias, tipo de titular) → `POST /bank-account`; pantalla de estatus. ✅ *(hecho 2026-11-11:*
   - *`Domain/BankAccounts`: `Clabe` (`Normalize` quita espacios/guiones, `IsValid` con dígito verificador de pesos 3-7-1, `Mask` ⇒ `•••• 5713`) y entidad `BankAccount` (`Submit`, `Resubmit` para reintento tras `Error`, `UpdateStatus` listo para 2.3). Tabla `BankAccounts` (migración `BankAccounts`): enums como texto, CLABE `char(18)`, FK `Restrict`, **índice único en `MerchantId`** (una cuenta por vendedor hasta la Fase 4; también frena el doble envío) e índice único en `PeyBankAccountId`.*
   - *Reglas puras en `Merchant`: `CanSubmitBankAccount` (solo `PendingBankAccount` y sin cuenta o con la cuenta en `Error`) y `ApplyBankAccountStatus(PeyCodiStatus)`: durante el onboarding `Pending/Processing` ⇒ `BankAccountInReview`, `Active` ⇒ `Active`, `Error` ⇒ `PendingBankAccount`; fuera del onboarding no cambia nada (el `Error` de una cuenta activa queda para 2.3/Fase 4). 2.3 reutiliza la misma regla.*
   - *Caso de uso `IBankAccountRegistration` (comando + validador FluentValidation en `Application/BankAccounts`, implementación en `Infrastructure/BankAccounts`): valida → `RegisterBankAccountAsync` → guarda cuenta + estatus + `BusinessName` (si estaba vacío, toma el alias) en un solo `SaveChanges`. Si Pey falla no se guarda nada: `INVALID_CLABE` ⇒ error en el campo CLABE; otra validación ⇒ error general; no disponible ⇒ "intenta en unos minutos" (log: resultado incierto); auth/estado/falta de API key ⇒ mensaje genérico + log de error. Los logs llevan la CLABE enmascarada.*
   - ***Multi-tenant** (primera entidad con `MerchantId`): `ITenantContext` (Application) + filtro global en `AppDbContext` (`!IsMerchantScoped || MerchantId == CurrentMerchantId`). En el sitio, `HttpTenantContext` usa el claim `local133:merchant_id` que agrega `AppClaimsPrincipalFactory` al iniciar sesión; un `Merchant` sin claim no ve nada; admin, anónimo (webhook) y jobs no tienen alcance (`SystemTenantContext`). El alta busca la cuenta existente con `IgnoreQueryFilters()` y `MerchantId` explícito para no depender del claim.*
   - *`/app/cuenta-bancaria` (`BankAccountController`): formulario (tipo de titular en radios, titular, CLABE `inputmode=numeric`, nombre del negocio precargado) o pantalla de estatus (badge, CLABE enmascarada, fecha en hora de CDMX). PRG con mensaje. Reintento tras rechazo: aviso con el motivo y datos precargados (menos la CLABE). `[Clabe]` en cliente y servidor (adaptador en `validation.js`) y `form[data-disable-on-submit]` en `site.js` para evitar doble envío.*
   - *Inicio: "Registrar mi CLABE" / "Corregir mi cuenta" (con el motivo del rechazo) / "Ver estatus". Pestaña activa del layout del vendedor por controlador (la cuenta bancaria marca "Más"); "Más" tiene el link a Cuenta bancaria.*
   - *Pruebas: 48 unitarias (`ClabeTests`, `MerchantBankAccountStatusTests`) y 21 de integración (`BankAccountTests`: alta, activación inmediata, CLABE inválida sin llamar a Pey, errores de la pasarela, segundo envío, reintento sobre la misma fila, antiforgery, suspendido, admin, aislamiento por página y por filtro de EF). `FakePaymentGateway` reemplaza a Pey en `Local133WebFactory`: ninguna prueba del sitio llama a la API real. 198/198 verdes en local (124 en CI).*
   - *Prueba de arranque del sitio real: registro → Inicio con "Registrar mi CLABE" → formulario → CLABE con dígito incorrecto rechazada en servidor sin llamar a Pey.*
   - *Pendiente: alta real contra Pey (producción) con la CLABE de prueba, y revisión en navegador de la validación en cliente a 375 px.)*
   - ***Verificar estatus** (agregado 2026-11-11):*
     - *Con la cuenta en revisión, `/app/cuenta-bancaria` muestra el botón "Verificar estatus" (`POST /app/cuenta-bancaria/verificar`, antiforgery). Consulta `GET /bank-account/{id}` y aplica el resultado:*
       - *`Active` ⇒ vendedor `Active` + "Comenzar a vender" (`/app/cobrar`);*
       - *`Error` ⇒ formulario de reintento con el motivo;*
       - *`Pending`/`Processing` ⇒ aviso.*
     - *Freno de 1 min entre consultas manuales (`BankAccountStatusChecks.ManualMinInterval`), sobre la nueva columna `BankAccounts.StatusCheckedAt` (migración `BankAccountStatusCheckedAt`). El alta y el reintento cuentan como lectura.*
     - *La lógica es el caso de uso `IBankAccountStatusSync.SyncAsync(bankAccountId, minInterval)` (Application/BankAccounts; implementación en Infrastructure/BankAccounts): sin sesión ni claims, con `IgnoreQueryFilters()`. Guarda en un solo `SaveChanges` `BankAccount.RecordStatusCheck` + `Merchant.ApplyBankAccountStatus`. Si Pey falla no guarda nada.*
     - *Pruebas: 13 unitarias (`BankAccountStatusCheckTests`) y 14 de integración en `BankAccountTests` (incluye la llamada sin sesión, como el job, y el aislamiento entre vendedores). 225/225 verdes en local (137 en CI).*
     - *Pendiente: revisión en navegador con Playwright a 375 px y ≥ 992 px (el MCP no conectó en esta sesión) y una consulta real contra Pey con la CLABE de prueba.*
3. **Endpoint de webhook** con verificación de firma, `WebhookEvents`, manejo de `bank.account.updated` ⇒ `Active`. Job de sondeo de estatus. ✅ *(hecho 2026-11-12, junto con la 2.5:*
   - *Formato real tomado de docs.pey.mx (el OpenAPI trae `"webhooks": {}`):*
     - *`codi.qr.payment_received` = `{type, amount (number), apiKeyId, bankAccountId?, codiQrId, concept, datePaid (segundos Unix), numRef (texto), transactionId, metadata:{clabe, bankCode, accountType, description, trackingKey, externalMetadata}}`;*
     - *`bank.account.updated` = `{type, bankAccountId, codiStatus:"ACTIVE", name, clabe, entity, ownerType, ownerId, taxId?}`, sin `errorMessage`.*
   - *Firma: `Infrastructure/Webhooks/StandardWebhookVerifier`, propio y sin paquete. Calcula HMAC-SHA256 de `"{id}.{timestamp}.{body}"` con el secreto `whsec_<base64>` y acepta varias firmas `v1,…` en el header. Compara en tiempo constante y tolera ±5 min (la del estándar; Pey no documenta otra). 29 pruebas, incluido el vector oficial de Standard Webhooks; corren en CI.*
   - *`POST /api/webhooks/pey` (`PeyWebhooksController`, anónimo, body crudo, máximo 64 KB). Respuestas:*
     - *sin `Pey:WebhookSecret` ⇒ 500;*
     - *firma inválida o sin headers ⇒ 401;*
     - *si no, `IPeyWebhookProcessor` (Infrastructure/Webhooks/PeyWebhookProcessor), que guarda en `WebhookEvents` y **procesa en la misma petición** (ver §4).*
   - *Bitácora `WebhookEvents` (`Domain/Webhooks/WebhookEvent`): `WebhookId` único, tipo, payload crudo, timestamp, estatus `Received`/`Processed`/`Ignored`/`Failed`, intentos y error. No tiene filtro multi-tenant (es del admin; su pantalla llega en la Fase 6).*
     - *Un id `Processed`/`Ignored` repetido ⇒ `Duplicate`.*
     - *Uno `Failed` o a medias se reprocesa cuando Pey lo reintenta.*
     - *`codi.pn.payment_received` y `payment_link.*` ⇒ `Ignored`, con el payload guardado.*
   - *`bank.account.updated` ⇒ `IBankAccountStatusSync.ApplyWebhookAsync(peyBankAccountId, status)`: comparte la transición con la consulta, pero usa `UpdateStatus` y no toca `StatusCheckedAt` ni llama a Pey. Una cuenta desconocida ⇒ `Failed`.*
   - *Hangfire 1.8.25 (`Hangfire.AspNetCore` + `Hangfire.SqlServer` con `Microsoft.Data.SqlClient`):*
     - *almacenamiento en la misma base, esquema `HangFire`, que crea Hangfire;*
     - *`Web/Jobs/JobsSetup`, con `Jobs:Enabled` (`true` por defecto; las pruebas lo apagan) y sin reintentos automáticos;*
     - *sin dashboard todavía (Fase 6/7).*
   - *Job `sondeo-cuentas-bancarias` (`*/30 * * * *`, en hora de CDMX; `Infrastructure/Jobs/BankAccountPollingJob`): toma las cuentas `Pending`/`Processing`, las nunca consultadas y luego las de lectura más antigua. Llama `SyncAsync(id)` sin `minInterval`, un scope por cuenta; si una falla, sigue con la siguiente.*
   - *Pruebas: `PeyWebhookTests` (firma, bitácora, idempotencia, `bank.account.updated`) y `BankAccountPollingJobTests`.*
   - *Pendientes:*
     - *registrar la URL pública y el secreto en portal.mipey.mx;*
     - *recibir un `bank.account.updated` real con la CLABE de prueba;*
     - *el correo de cuenta activada/rechazada (Notificaciones).)*
4. **Venta por monto directo**: campo de monto → crea `Sale` → `POST /codi/qr` (`generateImage: false`) → vista con el QR (dibujado en el navegador con QRCode.js a partir de `qrString`) y cuenta regresiva en JS. ✅ *(hecho 2026-11-11, antes que la 2.3, porque no depende de ella:*
   - *Pey: `PeyCreateQrBody.GenerateImage` siempre `false` y explícito en el cuerpo (el OpenAPI dice que por defecto es `false` y que la imagen hace más lenta la respuesta).*
   - *`Domain/Sales`: enums `ChargeMethod`, `SaleSource`, `SaleStatus` y entidad `Sale`:*
     - *`StartDirect` valida el monto: de $0.01 a $999,999.99, con hasta 2 decimales;*
     - *`AttachQr`, `IsExpiredAt` / `RemainingSeconds` (no cambian el estatus: eso lo hará el job de la 2.5);*
     - *`NumRef = Folio % 10⁷`. ~~Concepto recortado a 40 caracteres~~ (reemplazado el 2026-11-12 por el campo Concepto obligatorio, ver abajo).*
   - *`Merchant.CanCharge(BankAccount?)`: vendedor `Active` y cuenta en `PeyCodiStatus.Active`. Ya lo exige el caso de uso; la 2.10 lo reutiliza.*
   - *Tabla `Sales` (migración `Sales`): enums como texto, `Amount decimal(18,2)` con check `>= 0`, FK `Restrict`, **índice único `(MerchantId, Folio)`** e índice único filtrado en `PeyQrId`. Filtro multi-tenant en `AppDbContext`.*
   - *Caso de uso `ISaleCharging.CreateDirectSaleAsync` (Application/Sales + Infrastructure/Sales/SaleCharging):*
     1. *valida y revisa `CanCharge`;*
     2. *inserta la venta con `Folio = max + 1` (si choca con el índice único reintenta hasta 3 veces), así ya existe `saleId`;*
     3. *`POST /codi/qr` a la cuenta del vendedor con `expiration = SystemSettings.SaleQrExpirationSeconds` (600), `numRef` = folio, concepto `"{negocio} · {concepto capturado}"` y metadata `{kind:"sale", merchantId, saleId}` (llaves en `SaleMetadata`);*
     4. *adjunta el QR.*
     *Si Pey falla se borra la venta (el folio no se salta) y se muestra un mensaje según el tipo de error. La hora de vencimiento se toma antes de llamar a Pey, así la cuenta regresiva nunca es más larga que la real.*
   - *Web: `ChargeController` en `/app/cobrar` (marca la pestaña "Cobrar"):*
     - *`GET` muestra el formulario, o un aviso según el estatus con liga a cuenta bancaria o Inicio;*
     - *`POST` recibe el monto como texto y lo convierte con cultura invariante (sin signo ni separador de miles), luego PRG;*
     - *`GET venta/{id}` muestra el QR; la venta de otro vendedor da 404 por el filtro.*
   - *Campo de monto (`ChargeInput`): `type=text inputmode=decimal`, **0.01 por defecto**, regex `^\d{1,6}(\.\d{1,2})?$` en cliente y servidor. `wwwroot/js/amount-input.js` bloquea `-`, `+`, `e`, el tercer decimal, el segundo punto y el 7.º entero; convierte la coma en punto; limpia lo pegado o autocompletado; quita ceros a la izquierda al salir; selecciona todo al enfocar.*
   - *Vista del QR: `wwwroot/js/sale-qr.js` usa QRCode.js (`qrcodejs@1.0.0` vía LibMan/cdnjs en `wwwroot/lib/qrcodejs`) con nivel de corrección M y fondo blanco. La cuenta regresiva `mm:ss` parte de los segundos que calcula el servidor; al llegar a 0 oculta el QR y muestra "Este QR expiró". Una venta ya vencida se dibuja sin QR.*
   - *Pruebas: 46 casos unitarios nuevos (`SaleTests`, `MerchantCanChargeTests`) y 27 de integración (`DirectSaleTests`: formulario, bloqueo por estatus, cuerpo enviado a Pey, montos inválidos, fallas de Pey, folios, aislamiento, vencida, antiforgery, admin). `PeyClientTests` verifica `generateImage: false`. `FakePaymentGateway` simula `CreateCodiQrAsync`. 298/298 verdes en local (183 en CI).*
   - *Revisión en navegador: el MCP de Playwright no conectó en esta sesión. Se hizo una revisión equivalente con `playwright-core` sobre Edge instalado, a 375 px y 1280 px, con 49/49 revisiones OK:*
     - *0.01 por defecto y el filtrado al teclear y al pegar;*
     - *error en cliente sin POST y error del servidor;*
     - *pestaña activa; sin scroll horizontal; nada tapado por la barra;*
     - *el QR dibujado se decodificó con jsQR y contiene exactamente `qrString` (sin BOM);*
     - *cuenta regresiva y paso a expirado; aviso de bloqueo;*
     - *consola sin errores y recursos sin 404.*
     *La venta para la vista del QR se insertó a mano en la base, sin llamar a Pey.*
   - *Pendiente:*
     - *revisión con el MCP de Playwright cuando conecte;*
     - *cobro real contra Pey ($0.01, CLABE de prueba), escaneando el QR con una app bancaria;*
     - *confirmar con Pey el límite del `concept` y que `qrString` sea solo ASCII. Si trae acentos, QRCode.js le antepone un BOM UTF-8.)*
   - ***Campo Concepto obligatorio** (agregado 2026-11-12):*
     - *`/app/cobrar` pide, debajo del monto, un **Concepto** obligatorio (placeholder "Ej. 2 tacos al pastor", ayuda "Tu cliente lo verá en su app junto con el nombre de tu negocio"). Su valor es el `concept` de `POST /codi/qr`: `Sale.BuildConcept(negocio, concepto)` = `"{negocio} · {concepto}"`, o solo el concepto si el negocio no tiene nombre. Se quitó el folio del concepto (sigue en `numRef`).*
     - *Por qué ya no se recorta a 40: el 40 era una suposición (concepto de SPEI/CoDi). El OpenAPI de Pey (revisado el 2026-11-12) define `concept` como `string` opcional **sin `maxLength` ni `pattern`**. El concepto capturado admite hasta 100 caracteres (`Sale.ConceptTextMaxLength`); lo enviado, hasta 250 (`Sale.ConceptMaxLength` = negocio 120 + `" · "` + 100). Migración `SaleConceptLength`: `Sales.Concept` de `nvarchar(40)` a `nvarchar(250)` (sigue nullable para QR fijo/push). `StartDirect` exige concepto y ya no corta en silencio (`TrimConcept` eliminado).*
     - *Caracteres: letras (con acentos, ü y ñ), números, espacios y `. , - # / ( )` (`Sale.ConceptTextPattern`, mismo patrón en .NET y JS). Validación en tres capas: atributos de `ChargeInput.Concept` (cliente + model binding), `CreateDirectSaleCommandValidator` (mensajes en `SaleConceptMessages`) y `Sale.StartDirect`. Nuevo hook `data-val-trim` en `wwwroot/js/validation.js` (normalizer de jQuery Validation): solo espacios cuenta como vacío también en cliente.*
     - *La vista del QR muestra el concepto bajo el monto (`data-sale-concept`, con salto de línea).*
     - *Pruebas: 142 unitarias (concepto: `BuildConcept`, caracteres permitidos/rechazados, máximo, obligatorio) y 187 de integración (formulario, concepto enviado con y sin negocio, conceptos inválidos sin llamar a Pey, más de 100, se conserva lo capturado). 329/329 verdes en local (206 en CI).*
     - *Revisión con el **MCP de Playwright** (Edge) a 375, 768 y 1280 px: formulario, errores en cliente (vacío, solo espacios, Enter, emoji, `@`; 0 POST), `maxlength` 100, sin scroll horizontal, nada tapado por la barra inferior, pestaña "Cobrar" activa, vista del QR con concepto corto y de 118 caracteres (QR dibujado, cuenta regresiva), consola sin errores y recursos sin 404. Se corrigió: solo espacios pasaba en cliente (⇒ `data-val-trim`) y el mensaje de caracteres partía la lista de signos en móvil (⇒ espacios de no separación). Ventas y cuenta bancaria insertadas a mano, sin llamar a Pey.*
     - *Pendiente: en el cobro real de $0.01, ver cómo muestra la app bancaria un concepto largo (Banxico podría truncarlo) y si los acentos del concepto llegan al `qrString` (QRCode.js antepondría un BOM).*
5. **Confirmación en tiempo real**: `codi.qr.payment_received` → crea `Payment` (con comisión calculada) → `Sale.Paid` → SignalR → pantalla de "Pago recibido". Expiración y cancelación de venta. ✅ *(hecho 2026-11-12:*
   - *Dominio:*
     - *`Domain/Payments/Commission`: `EffectiveRate(override, global)` y `Calculate`, que redondea a centavos con el punto medio hacia arriba. Ejemplos: $150.50 × 3 % = $4.52; $0.01 ⇒ $0.00.*
     - *`Payment`: `PeyTransactionId` único, monto, `PaidAt`, clave de rastreo, banco del pagador, `CommissionRate`, `CommissionAmount` y `StatementId` (sin FK hasta la Fase 5).*
     - *En `Sale`: `PaidAt`, `CancelledAt`, `MarkPaid`, `Cancel`, `ExpireIfDue` y `EffectiveStatus(now)`. **El pago manda**: una venta vencida o cancelada que recibe un pago queda `Paid`, porque el dinero ya llegó y CoDi no permite anular un QR. Un segundo pago se registra y la venta sigue pagada (con warning en el log).*
   - *`IPaymentRecorder` (Infrastructure/Payments/PaymentRecorder), reutilizable por la 2.9:*
     - *es idempotente por `transactionId`;*
     - *busca la venta por `PeyQrId` y luego por `saleId` de la metadata; si `merchantId` o `saleId` contradicen a la venta ⇒ `Failed`; `kind` `statement`/`static` ⇒ `Ignored` (Fase 5 / 2.7);*
     - *guarda el `Payment` y la venta pagada en un solo `SaveChanges` y, después, avisa con `ISaleNotifier`.*
   - *Tiempo real:*
     - *hub `/hubs/ventas` (`SalesHub`, rol Merchant, grupo `merchant-{id}` por el claim) con el evento `PaymentReceived {saleId}`; `/hubs` sin sesión responde 401;*
     - *`GET /api/sales/{id}` devuelve el estatus efectivo y `paidAtText`, formateado por el servidor en hora de CDMX. Responde 404 con la venta de otro vendedor y 401 sin sesión;*
     - *`sale-qr.js` escucha SignalR (`@microsoft/signalr@9.0.19` vía LibMan) y confirma con la API. Si SignalR no está conectado hace polling cada 3 s, hasta 2 min después de la expiración, y se detiene con 404/401/403. Al pagarse oculta el QR, la cuenta regresiva y los avisos, muestra «Pago recibido» y vibra.*
   - *Expiración: job `expirar-ventas` cada minuto (`ISaleExpiration`, `ExecuteUpdateAsync` condicionado a `Pending`). El índice `(Status, ExpiresAt)` lo cubre.*
   - *Cancelación: botón «Cancelar cobro» con confirmación (modal) que avisa que el QR podría seguir funcionando hasta que expire. `POST /app/cobrar/venta/{id}/cancelar` (antiforgery) ⇒ `ISaleCharging.CancelSaleAsync`, con un UPDATE condicionado que nunca pisa un pago que llega en paralelo. Una venta pagada, vencida o ya cancelada recibe un aviso; la de otro vendedor da 404.*
   - *Vista de la venta: estados pendiente, pagada, vencida y cancelada. Las vencidas y canceladas siguen escuchando, por si llega un pago tardío.*
   - *Migración `WebhooksAndPayments`: `Payments`, `WebhookEvents`, `Sales.PaidAt`/`CancelledAt` e índice `(Status, ExpiresAt)`.*
   - *Pruebas:*
     - *unitarias: `CommissionTests`, `SaleLifecycleTests`, `WebhookEventTests`;*
     - *integración: `PeyWebhookTests`, `SaleLifecycleTests`, `BankAccountPollingJobTests` y `SalesHubTests` (cliente SignalR real por long polling: el dueño recibe el evento y otro vendedor no);*
     - *456/456 verdes en local (282 en CI).*
   - *Revisión con el MCP de Playwright (Edge) a 375, 768 y 1280 px, sin llamar a Pey: ventas insertadas por SQL y webhooks firmados a mano desde PowerShell.*
     - *Con SignalR conectado, el pago cambia la pantalla a «Pago recibido» sin recargar y con una sola consulta de confirmación.*
     - *Con `/hubs` bloqueado, el polling cada 3 s detecta el pago.*
     - *Modal de cancelar y vista cancelada; un pago tardío sobre una venta cancelada también cambia a «Pago recibido».*
     - *El job de expiración vence la venta y la cuenta regresiva expira en el cliente.*
     - *En todos los tamaños: sin scroll horizontal, nada tapado por la barra y pestaña «Cobrar» activa. Consola sin errores y sin respuestas ≥ 400.*
     - *Hangfire registró los dos jobs en `HangFire.Hash` con zona `America/Mexico_City`.*
     - *Se corrigió: doble punto tras la hora del pago y formato distinto entre JS y servidor (⇒ `paidAtText`); aviso «Cancelaste el cobro» duplicado con el bloque de cancelado; polling que seguía tras un 404.*
   - *Pendientes:*
     - *pago real de $0.01 escaneando el QR con una app bancaria, con el webhook registrado en portal.mipey.mx (túnel HTTPS);*
     - *confirmar con Pey la tolerancia del timestamp, la política de reintentos del webhook y si un QR con monto acepta varios pagos.*
   - *Corrección (2026-11-13), primer pago real por el túnel: Pey cobró, pero el webhook no llegó. Hubo dos causas. Primero, el túnel no estaba hospedado (`devtunnel host`). Segundo, con el perfil `https` el sitio respondía 307 a `https://localhost:7180`. Se agregó `UseForwardedHeaders` (`Web/Hosting/ForwardedHeadersSetup`, solo loopback por defecto) y `ForwardedHeadersTests`. La venta 24 ($0.01) quedó pendiente de conciliar.)*
6. **Cobro por celular**: `POST /codi/pn` + webhook `codi.pn.payment_received`.
7. **QR fijo del puesto**: generación única, vista imprimible (PDF/imagen con logo); cada pago recibido crea venta `OpenAmount`.
8. **Historial de ventas** + resumen del día; **recibo compartible** (URL pública por token, botón WhatsApp).
9. Job de conciliación (`GET /transaction/codi/qr/{idc}`) para ventas pendientes.
10. Bloqueo: solo vendedores `Active` pueden generar cobros.

### Fase 3 — Productos y carrito (≈1.5 semanas)
1. CRUD de productos con baja lógica, foto.
2. Pantalla de venta con catálogo: búsqueda, agregar/quitar, cantidades, total → mismo flujo de QR. `SaleItems` guarda snapshot de precio.
3. Detalle de venta con productos en historial y recibo.

### Fase 4 — Mi información (≈4 días)
1. Datos generales: nombre, dirección, teléfono, email, logo y foto del local.
2. Cuenta bancaria completa: historial de estatus, mensaje de error, reintento/cambio de cuenta (invalida y regenera el QR fijo).

### Fase 5 — Estados de cuenta y cobranza (≈1.5 semanas)
1. Job de corte semanal: agrupa `Payments` sin estado de cuenta del periodo lun–dom, calcula subtotal + IVA, emite `Statement` con vencimiento.
2. Portal cliente: lista, detalle con movimientos, descarga PDF, **pagar con QR CoDi** (cuenta de la plataforma).
3. Webhook con `metadata.kind = "statement"` ⇒ `Paid`. Job de vencidos ⇒ `Overdue` ⇒ suspensión tras días de gracia; reactivación automática al pagar.
4. Correos: emitido, por vencer, suspendido.

### Fase 6 — Portal administrador (≈1.5 semanas)
1. Listado de clientes con búsqueda y filtros (estatus, fecha, adeudo); detalle con pestañas General / Cuenta / Ventas / Estados de cuenta.
2. Comisión por cliente y **parámetros globales** (comisión, IVA, días de gracia, expiración del QR), con auditoría.
3. Cobranza: marcar pagado/condonar, suspender/reactivar.
4. Tablero, transacciones globales, bitácora de webhooks con reproceso.

### Fase 7 — Endurecimiento y salida (≈1 semana)
Rate limiting en generación de cobros · pruebas de carga del webhook · respaldos · monitoreo y alertas (fallos de webhook, errores Pey) · despliegue a producción (HTTPS público para el webhook, registrar URL y secreto en portal.mipey.mx; declarar la IP o red del proxy en `ForwardedHeaders:KnownProxies`/`KnownNetworks`, o el sitio redirigirá con 307 lo que el proxy le entregue por HTTP) · prueba piloto con 3–5 vendedores.

**Total estimado:** ≈10–11 semanas para una persona.

---

## 6. Riesgos y pendientes por confirmar

- **Con soporte de Pey**: ¿un QR con monto puede pagarse más de una vez?, ¿hay webhook de rechazo/expiración de push?, política de reintentos de webhooks, límites de cuentas por API key y de peticiones, fecha del sandbox.
- **Espera de 5 días hábiles** para activar: principal fricción de adopción; mitigar con comunicación clara y notificación inmediata.
- **Cobranza**: el vendedor ya recibió el 100% de su dinero; la única palanca es la suspensión. Considerar corte semanal con vencimiento corto (3–5 días).
- **Fiscal/legal** (validar con contador/abogado): CFDI por la comisión, IVA, aviso de privacidad y términos; resguardo de CLABE y datos personales.
- **Dependencia de un solo proveedor**: aislar Pey tras `IPaymentGateway` para poder cambiar o agregar otro.

---

## 7. Verificación

- **Unitarias**: cálculo y redondeo de comisión, jerarquía de tasas, dígito verificador de CLABE, corte semanal (zona horaria, bordes de semana), transiciones de estatus.
- **Integración**: webhook con firma válida/ inválida/duplicada; `PeyClient` contra servidor simulado (WireMock) con cada `error.type`; aislamiento entre vendedores; páginas de Identity y áreas `Merchant`/`Admin` accesibles solo con el rol correcto (redirección a login / 403).
- **Extremo a extremo (producción Pey, $0.01)**:
  1. Registrarse → capturar CLABE de prueba `012180015826225713` → recibir `bank.account.updated` → estatus `Active`.
  2. Venta por monto directo → escanear QR con app bancaria → pantalla cambia a "Pago recibido" sin recargar → aparece en historial con comisión.
  3. Venta con carrito, cobro por celular y QR fijo.
  4. Forzar corte semanal → estado de cuenta emitido → pagarlo por QR → `Paid`; simular vencimiento → suspensión → pago → reactivación.
  5. Admin: cambiar comisión de un cliente y la global → verificar que solo afecta pagos nuevos.
- Exponer el webhook en local con un túnel HTTPS (ngrok / dev tunnels) y revisar "Webhook Logs" en el portal de Pey.
