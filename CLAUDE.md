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
  Local133.UnitTests
  Local133.IntegrationTests  (base `Local133_Tests` en SQL Server local, recreada con migraciones, limpiada con Respawn)
```
Dependencias: Domain ← Application ← Infrastructure/Web. Pey solo se usa desde Infrastructure detrás de `IPaymentGateway`.

## Comandos (una vez creado el scaffold de Fase 0)
- Conexión: `dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost\SQLEXPRESS;Database=Local133;Trusted_Connection=True;TrustServerCertificate=True" --project src/Local133.Web` (también API key y secreto de webhook de Pey en user-secrets, nunca en appsettings).
- Migraciones: `dotnet ef migrations add <Nombre> --project src/Local133.Infrastructure --startup-project src/Local133.Web` · `dotnet ef database update --project src/Local133.Infrastructure --startup-project src/Local133.Web`
- Build / pruebas: `dotnet build` · `dotnet test` · una prueba: `dotnet test --filter "FullyQualifiedName~<Clase>.<Metodo>"`
- Arrancar: `dotnet run --project src/Local133.Web` → `http://localhost:5231` (perfil `http`).
- Librerías cliente: `dotnet tool run libman restore` en `src/Local133.Web` (definidas en `libman.json`, salen en `wwwroot/lib`, que **se versiona**: build y CI no dependen de red).
- Rutas del sitio por **atributo** y en español (`/app/cobrar`, `/admin/clientes`); controladores de vistas con `[ApiExplorerSettings(IgnoreApi = true)]`. Errores: `/api/*` ⇒ ProblemDetails; resto ⇒ vistas `/error` y `/error/{code}`. Pantallas pendientes usan el parcial `_ComingSoon`.
- Scaffold de Identity: `dotnet aspnet-codegenerator identity -dc Local133.Infrastructure.Persistence.AppDbContext --files "Account.Register;Account.Login;..."` desde `src/Local133.Web` (requiere `dotnet-aspnet-codegenerator` en `.config/dotnet-tools.json` y el paquete `Microsoft.VisualStudio.Web.CodeGeneration.Design`). Las páginas generadas se traducen al español y se adaptan al `_Layout`.
- Publicar: `dotnet publish src/Local133.Web -c Release -o <carpeta>`.
- CI: `.github/workflows/ci.yml` (GitHub Actions): .NET build Release + `dotnet ef migrations has-pending-model-changes` + `dotnet test`. Sin SQL Server ni llamadas a Pey.
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
- **PeyClient**: `HttpClient` tipado + Polly (reintentos en 503/5xx, timeout), mapear `error.type` a excepciones de dominio, loguear `requestId`.
- Solo vendedores `Active` pueden generar cobros. Tiempo real por SignalR (grupo por vendedor, evento `PaymentReceived`) con respaldo de polling desde la vista a `GET /api/sales/{id}` cada 3 s.
- Jobs Hangfire: sondeo de cuentas, expirar ventas, conciliación (`GET /transaction/codi/qr/{idc}`), corte semanal lunes 00:05 `America/Mexico_City`, vencimiento/suspensión.
- Archivos vía `IFileStorage` (disco en dev, blob en prod). CLABE enmascarada en UI. Cambios de comisión/estatus quedan en `AuditLogs`.

## Flujo de trabajo obligatorio
- Implementar **por fases en el orden del plan** (0 Cimientos → 1 Registro/auth → 2 Ventas → 3 Productos/carrito → 4 Mi información → 5 Estados de cuenta/cobranza → 6 Admin → 7 Endurecimiento). Cada fase debe terminar en algo demostrable. La cuenta bancaria mínima se construye dentro de la Fase 2.
- Módulos fuera del MVP (inventario, cajeros, CFDI, Excel, links de pago con tarjeta, PWA offline) no se implementan sin pedirlo.
- Pruebas exigidas por el plan: unitarias (comisión y redondeo, jerarquía de tasas, CLABE, corte semanal con zona horaria y bordes, transiciones de estatus); integración (webhook con firma válida/inválida/duplicada, `PeyClient` contra WireMock por cada `error.type`, aislamiento entre vendedores, acceso a áreas e Identity según rol).
- **No hay sandbox**: pruebas reales contra producción de Pey con **$0.01** y la CLABE de prueba `012180015826225713` (activa de inmediato; los pagos no se devuelven). No hacer llamadas a Pey con otros montos/cuentas sin confirmación.
- Pendientes por confirmar con soporte de Pey (no asumir): si un QR con monto se paga más de una vez, webhook de rechazo/expiración de push, reintentos de webhooks, límites.
