# Plan de desarrollo — Portal de cobros CoDi para vendedores informales (Pey API)

## 1. Contexto

Plataforma para que vendedores informales (tianguis, mercados, bazares, puestos) cobren con CoDi usando la API de Pey. El vendedor se registra, da de alta su cuenta bancaria, y cobra generando un QR (carrito o monto directo). El negocio cobra una comisión configurable (3% por defecto) mediante estados de cuenta semanales.

Repositorio vacío (`c:\personalprojects\local133`): proyecto desde cero.

**Decisiones confirmadas**
- **ASP.NET Core 10 MVC** (vistas Razor + Bootstrap 5, móvil primero) en un solo proyecto web que también expone la API REST interna, el webhook y el hub SignalR; portal cliente y admin como áreas MVC. *(Cambio 2026-11-09: reemplaza la SPA React original. Cambio 2026-11-16: de .NET 9 a .NET 10 LTS, [bitácora](log_plan/mantenimiento-net10.md).)*
- **SQL Server** con EF Core 10.
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
| Confirmar pago | Webhooks `codi.qr.payment_received` (match por `codiQrId`) y `codi.pn.payment_received` (sin `codiQrId`: match por el `transactionId` que devolvió `POST /codi/pn`, guardado en `Sales.PeyTransactionId`) |
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
- **Archivos**: `IFileStorage` (disco local en dev, blob en prod), redimensionado con SkiaSharp. *(Cambio 2026-11-14: antes ImageSharp; ver 3.1.)*
- **Secretos**: API key y secreto de webhook de Pey en user-secrets / variables de entorno. CLABE enmascarada en UI.

### Modelo de datos (tablas principales)

`Merchants` (perfil, estatus, `CommissionRateOverride`) · `BankAccounts` (titular, CLABE, alias, entity, `PeyBankAccountId`, `CodiStatus`, `ErrorMessage`) · `Products` (nombre, descripción, precio, foto, `IsDeleted`) · `Sales` (folio, método, origen, monto, estatus, `PeyQrId`, `QrString`, `PeyTransactionId`, expiración, token público de recibo) · `SaleItems` (snapshot de nombre/precio/cantidad) · `Payments` (`PeyTransactionId` único, `SaleId`, monto, `DatePaid`, `TrackingKey`, `CommissionRate`, `CommissionAmount`, `StatementId`) · `StaticQrs` · `Statements` (periodo, subtotal, IVA, total, estatus, vencimiento, `PeyQrId`, `PaidAt`) · `SystemSettings` (comisión global, IVA, días de gracia) · `WebhookEvents` · `AuditLogs` · tablas de Identity.

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
5. **Confirmación en tiempo real**: `codi.qr.payment_received` → crea `Payment` (con comisión calculada) → `Sale.Paid` → SignalR → pantalla de "Pago recibido". Expiración y cancelación de venta. ✅ *(hecho 2026-11-12; corrección de forwarded headers el 2026-11-13)* — [bitácora](log_plan/fase-2.5-tiempo-real.md)
6. **Cobro por celular**: `POST /codi/pn` + webhook `codi.pn.payment_received`. ✅ *(hecho 2026-11-13)* — [bitácora](log_plan/fase-2.6-cobro-por-celular.md)
7. **QR fijo del puesto**: generación única, vista imprimible (PDF/imagen con logo); cada pago recibido crea venta `OpenAmount`.
8. **Historial de ventas** + resumen del día; **recibo compartible** (URL pública por token, botón WhatsApp). ✅ *(hecho 2026-11-14, junto con la 3.3)* — [bitácora](log_plan/fase-2.8-historial-y-recibo.md)
9. Job de conciliación (`GET /transaction/codi/qr/{idc}`) para ventas pendientes.
10. Bloqueo: solo vendedores `Active` pueden generar cobros.

### Fase 3 — Productos y carrito (≈1.5 semanas)
1. CRUD de productos con baja lógica, foto. ✅ *(hecho 2026-11-14; búsqueda asíncrona el mismo día)* — [bitácora](log_plan/fase-3.1-productos.md)
2. Pantalla de venta con catálogo: búsqueda, agregar/quitar, cantidades, total → mismo flujo de QR. `SaleItems` guarda snapshot de precio. ✅ *(hecho 2026-11-14)* — [bitácora](log_plan/fase-3.2-carrito.md)
3. Detalle de venta con productos en historial y recibo. ✅ *(hecho 2026-11-14, dentro de la 2.8)* — [bitácora](log_plan/fase-2.8-historial-y-recibo.md)

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

## Verificaciones pendientes

Pendientes abiertos de las subfases terminadas (el detalle sigue en cada bitácora). Al cerrar uno: quitarlo de aquí y anotar el resultado en su bitácora.

**Pruebas reales contra Pey** (producción, $0.01 y CLABE de prueba `012180015826225713`; requieren confirmación):
- [2.2](log_plan/fase-2.2-cuenta-bancaria.md): alta real de la cuenta bancaria y una consulta real de «Verificar estatus».
- [2.3](log_plan/fase-2.3-webhook-y-sondeo.md): registrar la URL pública y el secreto en portal.mipey.mx; recibir un `bank.account.updated` real.
- [2.4](log_plan/fase-2.4-venta-monto-directo.md): cobro real por QR escaneado con app bancaria; ver cómo muestra la app un concepto largo y si los acentos llegan al `qrString` (BOM de QRCode.js).
- [2.5](log_plan/fase-2.5-tiempo-real.md): pago real con el webhook por el túnel HTTPS; la venta 24 ($0.01) quedó pendiente de conciliar.
- [2.6](log_plan/fase-2.6-cobro-por-celular.md): cobro real por push a un celular con CoDi activo y su webhook.
- [3.2](log_plan/fase-3.2-carrito.md): cobro real con carrito (QR y celular) y su webhook.

**Confirmar con soporte de Pey** (ver también §6):
- [2.4](log_plan/fase-2.4-venta-monto-directo.md): límite del `concept` y que `qrString` sea solo ASCII.
- [2.5](log_plan/fase-2.5-tiempo-real.md): tolerancia del timestamp del webhook, política de reintentos y si un QR con monto acepta varios pagos.
- [2.6](log_plan/fase-2.6-cobro-por-celular.md): qué responde si el celular no tiene CoDi (¿400 o 200 sin que llegue nada?) y si hay aviso de rechazo del pagador.

**Revisión en navegador sin hacer** (no hay evidencia de una revisión posterior que la cubra):
- [1.4](log_plan/fase-1.4-merchant-y-terminos.md): la validación en cliente bloquea el registro con la casilla de términos sin marcar.
- [1.5](log_plan/fase-1.5-inicio-y-admin.md): Inicio del vendedor (`/app`) a 375 px.
- [2.2](log_plan/fase-2.2-cuenta-bancaria.md): formulario de cuenta bancaria (validación de CLABE en cliente) y «Verificar estatus», a 375, 768 y 1280 px con el MCP de Playwright.
- [Ajustes UI 2026-11-17](actualizaciones/ajustes_2026_11_17_11_54.md#implementación): header del layout de admin (`/admin`) con el icono de usuario más grande. Usa el mismo `_LoginPartial` que ya se revisó en `_Layout` y `_MerchantLayout`, pero no se entró como admin.

**Otros**:
- [1.3](log_plan/fase-1.3-login-externo.md): prueba real con credenciales de Google/Facebook (crear clientes OAuth y registrar los callbacks `https://localhost:7180/signin-google` y `/signin-facebook`).
- [2.3](log_plan/fase-2.3-webhook-y-sondeo.md): correo de cuenta activada/rechazada (Notificaciones).
- [3.1](log_plan/fase-3.1-productos.md): «Tomar foto» en un celular real (Android e iPhone); confirmar que CI (Ubuntu) carga los binarios nativos de SkiaSharp; en producción, `LocalDiskFileStorage` ⇒ blob (Fase 7).
- [2.8](log_plan/fase-2.8-historial-y-recibo.md): confirmar en Windows que los botones de WhatsApp abren la app de escritorio (`whatsapp://`); abrir el recibo y los botones de WhatsApp («al cliente» y «a otro contacto») y «Compartir» en un celular real (Android e iPhone); cómo previsualiza WhatsApp la liga. En producción fijar `App:PublicBaseUrl` (ver `azure.md`).
- [Migración a .NET 10](log_plan/mantenimiento-net10.md): cambiar la pila del App Service a .NET 10 antes del próximo despliegue (ver `azure.md`) y confirmar que CI (GitHub Actions, Ubuntu) pasa con el SDK 10.0.401.

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
