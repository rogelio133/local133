# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado del repo
Proyecto desde cero. La fuente de verdad es [PLAN_DESARROLLO.md](PLAN_DESARROLLO.md): portal para que vendedores informales cobren con CoDi vía la API de Pey (`https://api.pey.mx`, Bearer API key, **sin sandbox**). Antes de implementar algo, leer la sección del plan correspondiente; si una decisión cambia, actualizar el plan.

## Stack
- Backend: ASP.NET Core 9 **MVC** (vistas Razor + endpoints `/api`), EF Core 9 sobre **SQL Server local** (sin Docker), ASP.NET Core Identity con **UI scaffoldeada** (Razor Pages en `Areas/Identity`; cookie, roles `Merchant`/`Admin`, Google y Facebook), FluentValidation, Serilog, ProblemDetails, Swagger, SignalR, Hangfire (storage SQL Server), Polly, ImageSharp.
- Frontend: **MVC Razor + Bootstrap 5** en `src/Local133.Web` (áreas `Merchant` y `Admin`), móvil primero. Librerías cliente (Bootstrap, cliente JS de SignalR, jQuery Validation Unobtrusive) vía **LibMan** en `wwwroot/lib`: sin npm ni build de JS. QR renderizado como SVG en servidor con **QRCoder** a partir de `qrString`. Antiforgery en todos los formularios.
- Pruebas: xUnit, WebApplicationFactory, Respawn, WireMock.

## Estructura
```
Local133.sln
src/
  Local133.Domain          entidades, enums propios y de Pey (Domain/Pey), reglas (comisión, CLABE)
  Local133.Application     casos de uso, DTOs, validadores, interfaces (IPaymentGateway, IFileStorage…)
  Local133.Infrastructure  EF Core, PeyClient, procesamiento de webhooks, storage, email, jobs Hangfire
  Local133.Web             MVC (áreas Merchant/Admin, vistas Razor), Identity scaffoldeado, endpoints /api, hub SignalR, POST /api/webhooks/pey
tests/
  Local133.UnitTests         (solo Domain; corre en CI)
  Local133.IntegrationTests  (base `Local133_Tests` en SQL Server local, recreada con migraciones, limpiada con Respawn)
```
Dependencias: Domain ← Application ← Infrastructure/Web. Pey solo se usa desde Infrastructure detrás de `IPaymentGateway`.

## Comandos (una vez creado el scaffold de Fase 0)
- Conexión: `dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost\SQLEXPRESS;Database=Local133;Trusted_Connection=True;TrustServerCertificate=True" --project src/Local133.Web` (también API key y secreto de webhook de Pey en user-secrets, nunca en appsettings: `dotnet user-secrets set "Pey:ApiKey" "<api-key>" --project src/Local133.Web`; sin ella el sitio arranca y solo fallan las llamadas a Pey).
- Migraciones: `dotnet ef migrations add <Nombre> --project src/Local133.Infrastructure --startup-project src/Local133.Web` · `dotnet ef database update --project src/Local133.Infrastructure --startup-project src/Local133.Web`
- Build / pruebas: `dotnet build` · `dotnet test` · una prueba: `dotnet test --filter "FullyQualifiedName~<Clase>.<Metodo>"`
- Arrancar: `dotnet run --project src/Local133.Web` → `http://localhost:5231` (perfil `http`).
- Librerías cliente: `dotnet tool run libman restore` en `src/Local133.Web` (definidas en `libman.json`, salen en `wwwroot/lib`, que **se versiona**: build y CI no dependen de red).
- Rutas del sitio por **atributo** y en español (`/app/cobrar`, `/admin/clientes`); controladores de vistas con `[ApiExplorerSettings(IgnoreApi = true)]`. Errores: `/api/*` ⇒ ProblemDetails; resto ⇒ vistas `/error` y `/error/{code}`. Pantallas pendientes usan el parcial `_ComingSoon`.
- Scaffold de Identity: `dotnet aspnet-codegenerator identity -dc Local133.Infrastructure.Persistence.AppDbContext --files "Account.Register;Account.Login;..."` desde `src/Local133.Web` (requiere `dotnet-aspnet-codegenerator` en `.config/dotnet-tools.json` y el paquete `Microsoft.VisualStudio.Web.CodeGeneration.Design`). Las páginas generadas se traducen al español y se adaptan al `_Layout`.
- Publicar: `dotnet publish src/Local133.Web -c Release -o <carpeta>`.
- CI: `.github/workflows/ci.yml` (GitHub Actions): .NET build Release + `dotnet ef migrations has-pending-model-changes` + `dotnet test --filter "Category!=SqlServer"`. Sin SQL Server ni llamadas a Pey. Toda prueba que use la base lleva `[Trait("Category","SqlServer")]` (heredado de `WebTestBase`) y solo corre en local.
- Identity: `AddIdentity` **sin** `AddDefaultUI` (páginas no scaffoldeadas ⇒ 404; para agregar una, scaffoldearla y traducirla). Roles en `Local133.Application.Auth.Roles`, sembrados en la migración `Identity`. Textos y `ErrorMessage` de cada atributo en español.
- Login externo: Google/Facebook se registran en `IdentitySetup` **solo si** existen `Authentication:Google:ClientId/ClientSecret` y `Authentication:Facebook:AppId/AppSecret` (user-secrets). Google lleva `prompt=select_account` (selector de cuentas en cada login). Callbacks `/signin-google` y `/signin-facebook` (perfil `https`, `https://localhost:7180`). Cancelar o fallar en el proveedor regresa al callback de la página con `remoteError` (`ExternalLoginErrors`). Correo del proveedor conservado ⇒ `EmailConfirmed`; correo ya existente ⇒ no se liga solo. En pruebas se usa el esquema falso `FakeExternalAuthHandler` ("Fake"), nunca Google/Facebook.
- Alta de vendedores: siempre con `AccountRegistration` (Web/Identity), que crea usuario + rol `Merchant` + cuenta externa + `Merchant` (`PendingBankAccount`) en una transacción. Los formularios de alta heredan de `LegalAcceptanceInput` (casilla única de Términos y Aviso de privacidad, parcial `_AcceptLegalCheckbox`); la versión aceptada sale de `Domain/Legal/LegalDocuments` (subirla al cambiar `/terminos` o `/aviso-de-privacidad`).
- Admin: se siembra **al arrancar** (`AdminSeeder`, Web/Identity) si existen `Seed:Admin:Email` y `Seed:Admin:Password` (dev: `dotnet user-secrets set "Seed:Admin:Email" "<correo>" --project src/Local133.Web` y lo mismo con `Seed:Admin:Password`; prod: variables `Seed__Admin__Email` / `Seed__Admin__Password`). Es idempotente: no cambia la contraseña de un admin existente y nunca promueve a un `Merchant`. Las pruebas vacían `Seed:Admin` en `Local133WebFactory`.
- Inicio del vendedor (`/app`): checklist calculado con `Domain/Merchants/OnboardingChecklist.For(MerchantStatus)`; los textos van en `Areas/Merchant/Models/MerchantHomeViewModel`. Pruebas unitarias en `tests/Local133.UnitTests` (solo Domain, corren en CI).
- Cuenta bancaria (`/app/cuenta-bancaria`): alta con `IBankAccountRegistration` (Application/BankAccounts; implementación en Infrastructure/BankAccounts). Reglas en `Domain/BankAccounts/Clabe` (dígito verificador, `Mask`) y en `Merchant.CanSubmitBankAccount` / `ApplyBankAccountStatus` (reutilizar en webhook y sondeo). La CLABE solo se muestra o loguea con `Clabe.Mask`. Una cuenta por vendedor (índice único en `MerchantId`) hasta la Fase 4.
- Multi-tenant: `ITenantContext` (Application/Tenancy). `AppDbContext` aplica `HasQueryFilter(e => !IsMerchantScoped || e.MerchantId == CurrentMerchantId)` a cada entidad de negocio nueva (agregarla en `OnModelCreating`). En el sitio el tenant sale del claim `local133:merchant_id` (`AppClaimsPrincipalFactory` + `Web/Tenancy/HttpTenantContext`). Sin alcance: admin, anónimo, jobs (`SystemTenantContext`).
- Pruebas del sitio: `Local133WebFactory` reemplaza `IPaymentGateway` por `FakePaymentGateway` (`Factory.Gateway`: respuesta o excepción configurable, se restablece entre pruebas). Nunca se llama a Pey desde estas pruebas.
- Correo en dev: `FileEmailSender` guarda `.html` en `src/Local133.Web/App_Data/emails` y escribe la liga en el log (SMTP real llega con Notificaciones).
- Webhook en local: túnel HTTPS (ngrok / dev tunnels) y revisar "Webhook Logs" en portal.mipey.mx.

## Smart App Control (este equipo)
Windows tiene Smart App Control activo y a veces bloquea las DLL propias recién compiladas: `An Application Control policy has blocked this file. (0x800711C7)`. El bloqueo es **por archivo (hash)** y cambia de DLL con cada cambio de bytes.
- `Directory.Build.props` pone `UseAppHost=false`, así que todo corre vía `dotnet.exe` (firmado). Para arrancar: `dotnet src/Local133.Web/bin/Debug/net9.0/Local133.Web.dll` o `dotnet run`.
- Si una DLL queda bloqueada: reintentar más tarde, o modificar el código de ese proyecto y recompilar (hash nuevo). **No** usar `Deterministic=false`: genera hashes nuevos en cada build y empeora el problema.
- **No desactivar Smart App Control**: no se puede reactivar sin reinstalar Windows. Si algo no se puede ejecutar, dejarlo anotado como verificación pendiente.

## Reglas de arquitectura (no negociables)
- **La plataforma nunca toca el dinero del vendedor**: Pey deposita a la CLABE del vendedor (no hay split). La comisión se cobra aparte con estados de cuenta semanales pagados por QR CoDi **sin `bankAccountId`** (cae en la cuenta de la plataforma).
- **Una API key y un webhook** para todos los vendedores; cada cobro elige destino con `bankAccountId`.
- **Multi-tenant**: toda entidad de negocio lleva `MerchantId` + filtro global de EF Core + política de autorización. Probar aislamiento entre vendedores.
- **Webhook** `POST /api/webhooks/pey`: leer body crudo → verificar firma Standard Webhooks (`webhook-id`, `webhook-timestamp`, `webhook-signature`) → guardar en `WebhookEvents` (único por `webhook-id`) → responder 200 → procesar. Idempotente también por `transactionId`. Conciliar con `metadata` `{kind, merchantId, saleId | statementId}` (vuelve como string en `metadata.externalMetadata`).
- **Ventas ≠ Pagos**: un QR puede tener 0..N `Payments`; las ventas expiran por tiempo (no hay webhook de expiración).
- **Comisión**: la tasa se guarda **en cada `Payment`** al cobrarse (`CommissionRate`, `CommissionAmount`); tasa = `Merchant.CommissionRateOverride ?? SystemSettings` global (3% por defecto; IVA parametrizable). Cambiar la tasa nunca altera pagos existentes.
- **Snapshots**: `SaleItems` copia nombre/precio; `Products` usa baja lógica (`IsDeleted`).
- **Restricciones de Pey**: `expiration` = 0 o ≥ 360 s (venta dinámica usa 600); `amount: 0` + `expiration: 0` = QR fijo; `numRef` ≤ 7 dígitos; `metadata` ≤ 50 pares escalares; `POST /bank-account` requiere `alias`, `name`, `clabe`, `entity` (PERSON/BUSINESS), sin `taxId`. Validar dígito verificador de la CLABE antes de enviar.
- `bank.account.updated` solo llega al pasar a `ACTIVE`; `ERROR` se detecta con **polling** (job cada 30 min a `GET /bank-account/{id}`).
- **PeyClient** (`Infrastructure/Pey`, registrado con `AddPeyGateway`; contratos del OpenAPI `https://docs.pey.mx/openapi.json`): `HttpClient` tipado + `AddStandardResilienceHandler` (Polly v8), mapea `error.type` a las excepciones de `Application/Payments` (`PaymentGatewayValidationException`, `InvalidClabeException`, `…NotFound…`, `…Authentication…`, `…State…`, `…Unavailable…`) y loguea `requestId` (nunca bodies ni la API key). **Reintentos por método**: GET en cualquier falla transitoria; POST **solo en 503/429**: Pey no tiene llave de idempotencia y reintentar tras un 500 o un timeout podría duplicar un QR o un push. Reglas de Pey (`Domain/Pey/PeyLimits`) se validan antes de llamar (`ArgumentException`). Pruebas contra WireMock en `tests/Local133.IntegrationTests/Pey` (sin SQL Server, corren en CI).
- Solo vendedores `Active` pueden generar cobros. Tiempo real por SignalR (grupo por vendedor, evento `PaymentReceived`) con respaldo de polling desde la vista a `GET /api/sales/{id}` cada 3 s.
- Jobs Hangfire: sondeo de cuentas, expirar ventas, conciliación (`GET /transaction/codi/qr/{idc}`), corte semanal lunes 00:05 `America/Mexico_City`, vencimiento/suspensión.
- Archivos vía `IFileStorage` (disco en dev, blob en prod). CLABE enmascarada en UI. Cambios de comisión/estatus quedan en `AuditLogs`.

## Flujo de trabajo obligatorio
- Implementar **por fases en el orden del plan** (0 Cimientos → 1 Registro/auth → 2 Ventas → 3 Productos/carrito → 4 Mi información → 5 Estados de cuenta/cobranza → 6 Admin → 7 Endurecimiento). Cada fase debe terminar en algo demostrable. La cuenta bancaria mínima se construye dentro de la Fase 2.
- Módulos fuera del MVP (inventario, cajeros, CFDI, Excel, links de pago con tarjeta, PWA offline) no se implementan sin pedirlo.
- Pruebas exigidas por el plan: unitarias (comisión y redondeo, jerarquía de tasas, CLABE, corte semanal con zona horaria y bordes, transiciones de estatus); integración (webhook con firma válida/inválida/duplicada, `PeyClient` contra WireMock por cada `error.type`, aislamiento entre vendedores, acceso a áreas e Identity según rol).
- **Validación del frontend con Playwright (obligatoria)**: toda implementación o ajuste que toque vistas Razor, páginas de Identity, layouts, CSS o JS se revisa en el navegador con el MCP de Playwright antes de darla por terminada. Las pruebas de integración no sustituyen esta revisión.
  - Configuración (una vez): `claude mcp add playwright --scope project -- cmd /c npx @playwright/mcp@latest --browser msedge --viewport-size "375,812"` y reiniciar la sesión. Se usa Edge instalado (firmado), no el Chromium que descarga Playwright, por Smart App Control.
  - Arranque: `dotnet build` y luego, desde `src/Local133.Web`, `dotnet bin/Debug/net9.0/Local133.Web.dll --urls http://localhost:5231` en segundo plano con `ASPNETCORE_ENVIRONMENT=Development`. Usuarios de prueba: registrar uno y confirmarlo con el `.html` de `App_Data/emails`, o usar el admin sembrado. Al terminar, detener el sitio.
  - Qué revisar en cada pantalla tocada:
    - a **375 px** (móvil, prioridad) y a ≥ 992 px;
    - sin scroll horizontal y sin elementos tapados por la barra inferior;
    - pestaña activa correcta;
    - validación en cliente (los errores aparecen sin enviar al servidor);
    - flujo feliz y errores principales;
    - **consola sin errores** y recursos sin 404.
  - Reporte: capturas de lo revisado y lo que se corrigió. Si algo no se pudo validar (MCP sin configurar, Smart App Control), decirlo y anotarlo como verificación pendiente en `PLAN_DESARROLLO.md`. Nunca darlo por validado.
  - Durante esta revisión **no se llama a Pey real**. Las pantallas que lo requieren se prueban hasta antes del envío, o con la confirmación descrita abajo.
- **No hay sandbox**: pruebas reales contra producción de Pey con **$0.01** y la CLABE de prueba `012180015826225713` (activa de inmediato; los pagos no se devuelven). No hacer llamadas a Pey con otros montos/cuentas sin confirmación.
- Pendientes por confirmar con soporte de Pey (no asumir): si un QR con monto se paga más de una vez, webhook de rechazo/expiración de push, reintentos de webhooks, límites.
