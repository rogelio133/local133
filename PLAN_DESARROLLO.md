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
BANK_ACCOUNT_NOT_FOUND, NOT_FOUND, FORBIDDEN, AUTH_ERROR, BAD_REQUEST, REQUEST_PARSING_ERROR,
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
| Cobrar venta (carrito o monto) | `POST /codi/qr` `{amount, concept, expiration:600, numRef, bankAccountId, metadata}` → render de `qrString` como SVG en servidor (QRCoder) dentro de la vista Razor |
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
- **Frontend**: MVC con vistas Razor; `_Layout` Bootstrap 5 móvil primero (barra inferior en el área del vendedor, sidebar/Offcanvas en admin). Bootstrap, cliente JS de SignalR y demás librerías cliente vía **LibMan** en `wwwroot/lib` (sin npm ni build de JS). Validación con jQuery Validation Unobtrusive en cliente + FluentValidation en servidor; antiforgery en todos los formularios. JS ligero solo donde hace falta (teclado numérico, carrito, cuenta regresiva del QR).
- **Multi-tenant**: toda entidad de negocio lleva `MerchantId`; filtro global de EF Core + política de autorización.
- **PeyClient**: `HttpClient` tipado + Polly (reintentos en 503/5xx, timeout), mapeo de `error.type` a excepciones de dominio, logging con `requestId`.
- **Webhook** `POST /api/webhooks/pey`: leer body crudo → verificar firma Standard Webhooks → guardar en `WebhookEvents` (único por `webhook-id`) → responder 200 → procesar. Procesamiento idempotente por `transactionId`.
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
   - *Pendiente: el admin sembrado (1.5); mientras tanto el rol `Admin` se asigna a mano.)*
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
5. Inicio del portal cliente (área `Merchant`) con checklist de onboarding; seed del usuario admin.

### Fase 2 — Ventas (núcleo) (≈2.5 semanas)
1. **PeyClient** completo (`bank-account`, `codi/qr`, `codi/pn`, `transaction/*`) con enums y manejo de errores.
2. **Cuenta bancaria mínima**: formulario (titular, CLABE con validación, alias, tipo de titular) → `POST /bank-account`; pantalla de estatus.
3. **Endpoint de webhook** con verificación de firma, `WebhookEvents`, manejo de `bank.account.updated` ⇒ `Active`. Job de sondeo de estatus.
4. **Venta por monto directo**: teclado numérico → crea `Sale` → `POST /codi/qr` → vista con el QR (SVG generado en servidor con QRCoder a partir de `qrString`) y cuenta regresiva en JS.
5. **Confirmación en tiempo real**: `codi.qr.payment_received` → crea `Payment` (con comisión calculada) → `Sale.Paid` → SignalR → pantalla de "Pago recibido". Expiración y cancelación de venta.
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
Rate limiting en generación de cobros · pruebas de carga del webhook · respaldos · monitoreo y alertas (fallos de webhook, errores Pey) · despliegue a producción (HTTPS público para el webhook, registrar URL y secreto en portal.mipey.mx) · prueba piloto con 3–5 vendedores.

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
