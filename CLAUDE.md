# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado del repo
Proyecto desde cero. La fuente de verdad es [PLAN_DESARROLLO.md](PLAN_DESARROLLO.md): portal para que vendedores informales cobren con CoDi vía la API de Pey (`https://api.pey.mx`, Bearer API key, **sin sandbox**). Antes de implementar algo, leer la sección del plan correspondiente; si una decisión cambia, actualizar el plan. La bitácora de cada subfase terminada (qué se hizo, pruebas, revisión, pendientes) va en `log_plan/fase-<F>.<N>-<slug>.md`; en el plan solo queda ✅, fecha y link, y sus pendientes abiertos en la sección «Verificaciones pendientes».

## Stack
- Backend: ASP.NET Core 10 **MVC** (vistas Razor + endpoints `/api`), EF Core 10 sobre **SQL Server local** (sin Docker), ASP.NET Core Identity con **UI scaffoldeada** (Razor Pages en `Areas/Identity`; cookie, roles `Merchant`/`Admin`, Google y Facebook), FluentValidation, Serilog, ProblemDetails, Swagger, SignalR, Hangfire (storage SQL Server), Polly, SkiaSharp (fotos; no ImageSharp: la 3.1 tiene vulnerabilidades y la 3.2 exige llave de licencia para compilar en Release), DocumentFormat.OpenXml (exportación a Excel, 5.9).
- Frontend: **MVC Razor + Bootstrap 5** en `src/Local133.Web` (áreas `Merchant` y `Admin`), móvil primero. Librerías cliente (Bootstrap, cliente JS de SignalR, jQuery Validation Unobtrusive) vía **LibMan** en `wwwroot/lib`: sin npm ni build de JS. QR CoDi dibujado **en el navegador con QRCode.js** (`wwwroot/lib/qrcodejs`, `wwwroot/js/sale-qr.js`) a partir de `qrString`; a Pey siempre se manda `generateImage: false`. Antiforgery en todos los formularios.
- Estilos: Bootstrap compilado desde SCSS con **`AspNetCore.SassCompiler`** (dart-sass empacado, sin Node; `sasscompiler.json`: `Styles/site.scss` → `wwwroot/css/site.css`). Variables de marca en `Styles/_custom.scss`; estilos propios al final de `Styles/site.scss`. `wwwroot/css/site.css` es **generado** (build/publish; en Debug el watcher recompila al guardar, `Sass:Watch=false` lo apaga y las pruebas lo apagan): no editarlo ni versionarlo. El SCSS de Bootstrap (`twbs/bootstrap@v5.3.8` en LibMan, `Styles/vendor/bootstrap`) es de terceros: no se modifica, y su versión debe coincidir con `bootstrap.bundle.min.js`. Personalizar solo con variables Sass antes del import (sin CSS que pise clases de Bootstrap ni `!important`). `secondary` es el azul lona: para texto gris tenue usar `text-body-secondary`. Fuentes Bungee + Atkinson Hyperlegible desde Google Fonts (`Views/Shared/_Head.cshtml`). Logo e íconos: ver [infraestructura.md](docs/claude/infraestructura.md).
- Pruebas: xUnit, WebApplicationFactory, Respawn, WireMock.

## Estructura
```
Local133.sln
src/
  Local133.Domain          entidades, enums propios y de Pey (Domain/Pey), reglas (CLABE, suscripción)
  Local133.Application     casos de uso, DTOs, validadores, interfaces (IPaymentGateway, IFileStorage…)
  Local133.Infrastructure  EF Core, PeyClient, procesamiento de webhooks, storage, email, jobs Hangfire
  Local133.Web             MVC (áreas Merchant/Admin, vistas Razor), Identity scaffoldeado, endpoints /api, hub SignalR, POST /api/webhooks/pey
  Local133.LandingPage     portada pública (local133.com.mx), sitio aparte; no referencia a los demás proyectos
tests/
  Local133.UnitTests         (solo Domain; corre en CI)
  Local133.IntegrationTests  (base `Local133_Tests` en SQL Server local, recreada con migraciones, limpiada con Respawn)
  Local133.LandingPage.Tests (portada; sin SQL Server, corre en CI)
```
Dependencias: Domain ← Application ← Infrastructure/Web. Pey solo se usa desde Infrastructure detrás de `IPaymentGateway`.
Portada: detalle en [portada.md](docs/claude/portada.md).

## Detalle por área
Las notas de implementación de cada módulo están en `docs/claude/`. **Antes de tocar un área, lee su archivo**, y si cambias algo de lo que describe, actualízalo ahí (no aquí).
- [portada.md](docs/claude/portada.md): `Local133.LandingPage` (secciones, ligas, palabras, precios, estilos, JS y archivos compartidos con Web).
- [identity.md](docs/claude/identity.md): Identity scaffoldeado, redirección después del login, barra y home, login externo, alta de vendedores, textos legales, nombre de la persona y admin sembrado.
- [vendedor.md](docs/claude/vendedor.md): Inicio, «Mi información», cuenta bancaria, Cobrar (monto, celular y carrito), Productos, «Más», PWA, Ventas (historial, búsqueda, Excel y corte) y recibo público.
- [suscripciones.md](docs/claude/suscripciones.md): planes y vigencia, límites del gratuito, pago con link de Pey, recordatorios y avisos, afiliados y valor visible (recibo con marca, catálogo público y resumen mensual).
- [admin.md](docs/claude/admin.md): Clientes, plan por cliente, bitácora de auditoría, parámetros, tablero, transacciones y bitácora de webhooks.
- [pagos-webhooks-jobs.md](docs/claude/pagos-webhooks-jobs.md): webhook de Pey, registro de pagos, transiciones de `Sale`, SignalR, Hangfire, conciliación, túnel local y peticiones abandonadas.
- [infraestructura.md](docs/claude/infraestructura.md): logo e íconos, archivos (`IFileStorage`), correo (Resend) y configuración del MCP de Playwright.

## Comandos (una vez creado el scaffold de Fase 0)
- Conexión: `dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost\SQLEXPRESS;Database=Local133;Trusted_Connection=True;TrustServerCertificate=True" --project src/Local133.Web` (también API key y secreto de webhook de Pey en user-secrets, nunca en appsettings: `dotnet user-secrets set "Pey:ApiKey" "<api-key>" --project src/Local133.Web` y `dotnet user-secrets set "Pey:WebhookSecret" "whsec_<secreto>" --project src/Local133.Web`; sin la API key el sitio arranca y solo fallan las llamadas a Pey; sin el secreto el webhook responde 500).
- Migraciones: `dotnet ef migrations add <Nombre> --project src/Local133.Infrastructure --startup-project src/Local133.Web` · `dotnet ef database update --project src/Local133.Infrastructure --startup-project src/Local133.Web`. El sitio aplica solo las migraciones pendientes **al arrancar** (`Web/Hosting/DatabaseMigrationSetup`, antes del admin y de Hangfire; si una falla, no arranca). `Database:MigrateOnStartup` vale `true` por defecto y las pruebas lo apagan, porque la fábrica migra su propia base.
- Build / pruebas: `dotnet build` · `dotnet test` · una prueba: `dotnet test --filter "FullyQualifiedName~<Clase>.<Metodo>"`
- Arrancar: `dotnet run --project src/Local133.Web` → `http://localhost:5231` (perfil `http`).
- Librerías cliente: `dotnet tool run libman restore` en `src/Local133.Web` (definidas en `libman.json`, salen en `wwwroot/lib`, que **se versiona**: build y CI no dependen de red).
- Rutas del sitio por **atributo** y en español (`/app/cobrar`, `/admin/clientes`); controladores de vistas con `[ApiExplorerSettings(IgnoreApi = true)]`. Errores: `/api/*` ⇒ ProblemDetails; resto ⇒ vistas `/error` y `/error/{code}`. Pantallas pendientes usan el parcial `_ComingSoon` (`ComingSoonViewModel(título, fase como texto, descripción)`).
- Publicar: `dotnet publish src/Local133.Web -c Release -o <carpeta>`.
- CI: `.github/workflows/ci.yml` (GitHub Actions): .NET build Release + `dotnet ef migrations has-pending-model-changes` + `dotnet test --filter "Category!=SqlServer"`. Sin SQL Server ni llamadas a Pey. Toda prueba que use la base lleva `[Trait("Category","SqlServer")]` (heredado de `WebTestBase`) y solo corre en local.
- Multi-tenant: `ITenantContext` (Application/Tenancy). `AppDbContext` aplica `HasQueryFilter(e => !IsMerchantScoped || e.MerchantId == CurrentMerchantId)` a cada entidad de negocio nueva (agregarla en `OnModelCreating`). En el sitio el tenant sale del claim `local133:merchant_id` (`AppClaimsPrincipalFactory` + `Web/Tenancy/HttpTenantContext`). Sin alcance: admin, anónimo, jobs (`SystemTenantContext`).
- Pruebas del sitio: `Local133WebFactory`:
  - reemplaza `IPaymentGateway` por `FakePaymentGateway` (`Factory.Gateway`: respuesta o excepción configurable, se restablece entre pruebas). Nunca se llama a Pey desde estas pruebas;
  - fija `Pey:WebhookSecret` (`Local133WebFactory.WebhookSecret`);
  - envuelve el notificador real en `Factory.Notifier` (`RecordingSaleNotifier`): cada sitio (también los de `WithWebHostBuilder`) usa `Notifier.ForHost(su SignalRSaleNotifier)`, que registra en la cola común y reenvía a su propio SignalR. No volver a un notificador real compartido: un sitio derivado ya desechado dejaba sin avisos al de la fábrica (6.4).
  - Helpers de venta y webhooks firmados en `tests/Local133.IntegrationTests/Sales/SaleFlowTestBase` (`PostWebhookAsync`, `QrPaymentBodyFor`, `ChargeByPhoneAsync`, `PushPaymentBodyFor`). `FakePaymentGateway` simula también el push (`PushRequests`, `PushException`). `SalesHubTests` usa un cliente real de SignalR (long polling con `CookieContainerHandler`).

## Smart App Control (este equipo)
Windows tiene Smart App Control activo y a veces bloquea las DLL propias recién compiladas: `An Application Control policy has blocked this file. (0x800711C7)`. El bloqueo es **por archivo (hash)** y cambia de DLL con cada cambio de bytes.
- `Directory.Build.props` pone `UseAppHost=false`, así que todo corre vía `dotnet.exe` (firmado). Para arrancar: `dotnet src/Local133.Web/bin/Debug/net10.0/Local133.Web.dll` o `dotnet run`.
- Si una DLL queda bloqueada: reintentar más tarde, o modificar el código de ese proyecto y recompilar (hash nuevo). **No** usar `Deterministic=false`: genera hashes nuevos en cada build y empeora el problema.
- **No desactivar Smart App Control**: no se puede reactivar sin reinstalar Windows. Si algo no se puede ejecutar, dejarlo anotado como verificación pendiente.

## Reglas de arquitectura (no negociables)
- **La plataforma nunca toca el dinero del vendedor**: Pey deposita a la CLABE del vendedor (no hay split). El ingreso de la plataforma es la **suscripción** (gratuito con límites / Pro), que se paga aparte con **link de pago de Pey** (no acepta `bankAccountId`: cae en la cuenta de la plataforma) y se concilia por el webhook `payment_link.status_updated`. Diseño en `actualizaciones/suscripciones_2026_11_18.md`. No hay comisión por venta (se retiró en la 5.1).
- **Una API key y un webhook** para todos los vendedores; cada cobro elige destino con `bankAccountId`, **obligatorio** en `CreateCodiQrRequest` y `CreateCodiPushNotificationRequest` (lo valida `PeyRequestGuard`): ningún QR ni push cae en la cuenta de la plataforma.
- **Multi-tenant**: toda entidad de negocio lleva `MerchantId` + filtro global de EF Core + política de autorización. Probar aislamiento entre vendedores.
- **Webhook** `POST /api/webhooks/pey`: leer body crudo → verificar firma Standard Webhooks (`webhook-id`, `webhook-timestamp`, `webhook-signature`) → guardar en `WebhookEvents` (único por `webhook-id`) → procesar en la misma petición → 200 (500 solo en fallas transitorias, para que Pey reintente con el mismo id). Idempotente también por `transactionId`. Conciliar con `metadata` `{kind, merchantId, saleId}` (links de la suscripción: `{kind: "subscription", merchantId, subscriptionChargeId}`) (vuelve como string en `metadata.externalMetadata`).
- **Ventas ≠ Pagos**: un QR puede tener 0..N `Payments`; las ventas expiran por tiempo (no hay webhook de expiración).
- **Precios de la suscripción**: el monto se guarda **en cada cargo** al crearlo; cambiar un precio en `SystemSettings` nunca altera lo ya cobrado (solo los links nuevos). Precios con IVA incluido (`VatRate` sirve para desglosarlo).
- **Snapshots**: `SaleItems` copia nombre/precio; `Products` usa baja lógica (`IsDeleted`).
- **Restricciones de Pey**: `expiration` = 0 o ≥ 360 s (venta dinámica usa 600); `amount: 0` + `expiration: 0` = QR fijo; `numRef` ≤ 7 dígitos; `metadata` ≤ 50 pares escalares; `POST /bank-account` requiere `alias`, `name`, `clabe`, `entity` (PERSON/BUSINESS), sin `taxId`. Validar dígito verificador de la CLABE antes de enviar.
- `bank.account.updated` solo llega al pasar a `ACTIVE`; `ERROR` se detecta con **polling** (job cada 30 min a `GET /bank-account/{id}`).
- **PeyClient** (`Infrastructure/Pey`, registrado con `AddPeyGateway`; contratos del OpenAPI `https://docs.pey.mx/openapi.json`): `HttpClient` tipado + `AddStandardResilienceHandler` (Polly v8), mapea `error.type` a las excepciones de `Application/Payments` (`PaymentGatewayValidationException`, `InvalidClabeException`, `…NotFound…`, `…Authentication…`, `…State…`, `…Unavailable…`) y loguea `requestId` (nunca bodies ni la API key). **Reintentos por método**: GET en cualquier falla transitoria; POST **solo en 503/429**: Pey no tiene llave de idempotencia y reintentar tras un 500 o un timeout podría duplicar un QR o un push. Reglas de Pey (`Domain/Pey/PeyLimits`) se validan antes de llamar (`ArgumentException`). Pruebas contra WireMock en `tests/Local133.IntegrationTests/Pey` (sin SQL Server, corren en CI).
- Solo vendedores `Active` pueden generar cobros. Tiempo real por SignalR (grupo por vendedor, evento `PaymentReceived`) con respaldo de polling desde la vista a `GET /api/sales/{id}` cada 3 s.
- Jobs Hangfire: sondeo de cuentas, expirar ventas, conciliación (`GET /transaction/codi/qr/{idc}` y `GET /transaction/{id}` para push), suscripciones (diario 9:00 `America/Mexico_City`: recordatorios, fin de prueba y baja al gratuito tras `GraceDays`, sin crear links; la falta de pago **no suspende**) y conciliación de links de pago.
- Archivos vía `IFileStorage` (disco en dev, blob en prod). CLABE enmascarada en UI. Cambios de precios, planes o estatus quedan en `AuditLogs`.

## Flujo de trabajo obligatorio
- Implementar **por fases en el orden del plan** (0 Cimientos → 1 Registro/auth → 2 Ventas → 3 Productos/carrito → 4 Mi información → 5 Suscripciones → 6 Admin → 7 Endurecimiento). Cada fase debe terminar en algo demostrable. La cuenta bancaria mínima se construye dentro de la Fase 2.
- **Publicación en Azure (`azure.md`)**: al terminar cada subfase, actualizar [azure.md](azure.md) (en la raíz; crearlo si no existe) con lo que hay que considerar y hacer para publicar en Azure por lo que se agregó en esa subfase: recursos (App Service, Azure SQL, Blob Storage…), configuración y secretos (App Settings / Key Vault, nombres con `__`), migraciones, jobs de Hangfire, webhooks y URLs de callback, SignalR, HTTPS/proxy, dominios, etc. Cada punto indica la subfase en que aplica (p. ej. «Fase 2.3»). Si la subfase no cambia nada para Azure, anotarlo explícitamente («Fase X.Y: sin cambios para Azure»). No poner secretos reales en el archivo.
- Módulos fuera del MVP (inventario, vendedores adicionales, CFDI de la suscripción, links de pago con tarjeta para las ventas del vendedor, PWA offline) no se implementan sin pedirlo. La exportación a Excel sí entra, como función de Pro (5.9).
- Pruebas exigidas por el plan: unitarias (CLABE, vigencia de la suscripción con prueba, gracia, periodos encadenados, bordes de mes y zona horaria; precios, fundador y descuento de bienvenida; límite mensual del gratuito; tope de premios de referidos; transiciones de estatus); integración (webhook con firma válida/inválida/duplicada, `payment_link.status_updated`, bloqueos del gratuito en el servidor, CLABE única, `PeyClient` contra WireMock por cada `error.type`, aislamiento entre vendedores, acceso a áreas e Identity según rol).
- **Validación del frontend con Playwright (obligatoria)**: toda implementación o ajuste que toque vistas Razor, páginas de Identity, layouts, CSS o JS se revisa en el navegador con el MCP de Playwright antes de darla por terminada. Las pruebas de integración no sustituyen esta revisión.
  - Configuración del MCP (una vez): ver [infraestructura.md](docs/claude/infraestructura.md).
  - Arranque: `dotnet build` y luego, desde `src/Local133.Web`, `dotnet bin/Debug/net10.0/Local133.Web.dll --urls http://localhost:5231` en segundo plano con `ASPNETCORE_ENVIRONMENT=Development Jobs__Enabled=false Pey__ApiKey= Resend__ApiKey=`. **`Resend__ApiKey=` vacío es obligatorio**: los user-secrets de desarrollo traen la llave de Resend, y sin vaciarla los correos de los usuarios de prueba salen de verdad. Con ella vacía, van a `App_Data/emails`. **Arrancarlo desde Bash** (`Resend__ApiKey= Pey__ApiKey= dotnet …`): en PowerShell `$env:Resend__ApiKey=''` **borra** la variable, y el sitio toma las llaves de los user-secrets. Así salió un correo real el 2026-11-26. Usuarios de prueba: registrar uno y confirmarlo con el `.html` de `App_Data/emails`, o usar el admin sembrado. Al terminar, detener el sitio.
  - Qué revisar en cada pantalla tocada:
    - en los **tres tamaños**: **375 px** (móvil, prioridad), **768 px** (tablet) y **1280 px** (escritorio). Revisar solo móvil no basta;
    - sin scroll horizontal y sin elementos tapados por la barra inferior;
    - pestaña activa correcta;
    - validación en cliente (los errores aparecen sin enviar al servidor);
    - flujo feliz y errores principales;
    - **consola sin errores** y recursos sin 404.
  - **Gastar poco**: comprobar primero con `browser_evaluate` (scroll horizontal, alturas, elementos ocultos) y `browser_console_messages`, y tomar **una captura por tamaño**, de la vista o del elemento tocado, nunca de la página completa. No leer los snapshots completos salvo que haga falta.
  - Reporte: capturas de lo revisado en los tres tamaños y lo que se corrigió. Si algo no se pudo validar (MCP sin configurar, Smart App Control), decirlo y anotarlo como verificación pendiente en la bitácora de la subfase (`log_plan/`) y en «Verificaciones pendientes» de `PLAN_DESARROLLO.md`. Nunca darlo por validado.
  - Durante esta revisión **no se llama a Pey real**. Las pantallas que lo requieren se prueban hasta antes del envío, o con la confirmación descrita abajo.
- **No hay sandbox**: pruebas reales contra producción de Pey con **$0.01** y la CLABE de prueba `012180015826225713` (activa de inmediato; los pagos no se devuelven). No hacer llamadas a Pey con otros montos/cuentas sin confirmación.
- Pendientes por confirmar con soporte de Pey (no asumir): si un QR con monto se paga más de una vez, webhook de rechazo/expiración de push, reintentos de webhooks, límites.
