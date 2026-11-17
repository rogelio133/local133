# Mantenimiento — Migración de .NET 9 a .NET 10

**Estado:** ✅ hecho 2026-11-16 · [← Plan](../PLAN_DESARROLLO.md) · rama `fix_netversion`

## Alcance
Pasar toda la solución a .NET 10 (LTS) y validar build, pruebas, migraciones, publish y sitio en ejecución. No es una subfase del plan: es mantenimiento. Se queda en la línea 10 aunque ya existe 11.0.0.

## Qué se hizo
- `Directory.Build.props`: `TargetFramework` `net10.0` (único lugar donde se define).
- `global.json`: SDK `10.0.401` (`rollForward: latestFeature`). CI lo toma con `setup-dotnet` (`global-json-file`), así que `ci.yml` no cambia.
- `Directory.Packages.props`:
  - Microsoft.AspNetCore.* (Authentication.Google/Facebook, Identity.*, Mvc.Testing, SignalR.Client) y Microsoft.EntityFrameworkCore.*: 9.0.22 → **10.0.15**;
  - Microsoft.Extensions.Http.Resilience 9.10.0 → 10.11.0;
  - Microsoft.VisualStudio.Web.CodeGeneration.Design 9.0.0 → 10.0.3;
  - Serilog.AspNetCore 9.0.0 → 10.0.0;
  - Swashbuckle.AspNetCore 9.0.6 → 10.3.1 (solo se usa `AddSwaggerGen`/`UseSwagger`/`UseSwaggerUI` sin opciones);
  - WireMock.Net.Minimal 2.20.0 → 2.31.0 (ver auditoría).
- `.config/dotnet-tools.json`: `dotnet-ef` 10.0.15 y `dotnet-aspnet-codegenerator` 10.0.3.
- `Web/Hosting/ForwardedHeadersSetup`: `KnownNetworks` + `Microsoft.AspNetCore.HttpOverrides.IPNetwork` son obsoletos en .NET 10 (ASPDEPR005). Se cambió a `KnownIPNetworks` + `System.Net.IPNetwork`. La llave de configuración `ForwardedHeaders:KnownNetworks` no cambia.
- Documentación: `CLAUDE.md`, `README.md`, `PLAN_DESARROLLO.md` (decisiones confirmadas y verificaciones pendientes) y `azure.md` (pila del App Service).

## Auditoría de NuGet (nuevo en el SDK 10)
Para proyectos `net10.0`, el SDK 10 audita también las dependencias **transitivas**. Con `TreatWarningsAsErrors`, el restore falló:
- `NuGet.Packaging`/`NuGet.Protocol` 6.12.1 (GHSA-g4vj-cjjj-v7hg, baja). Los arrastra `CodeGeneration.Design` 10.0.3, que es la última versión. Se fijan en **6.12.5**, la primera corregida de la 6.12, con `PackageReference` directo y `PrivateAssets=all` en `Local133.Web.csproj`. Quitarlos cuando el scaffolder los suba.
- `Microsoft.AspNetCore.WebUtilities` 2.3.0 (GHSA-6527-9fp8-r4x6, moderada). Lo arrastraba WireMock 2.20.0 vía `Microsoft.AspNetCore.Http` 2.3.9. WireMock 2.31.0 ya depende de la 2.3.14 corregida y trae destino `net10.0`.
- No se usó `NoWarn` ni se bajó el modo de auditoría.

## Pruebas
- `dotnet build` (Debug) y `dotnet build -c Release`: **0 advertencias, 0 errores**.
- `dotnet ef migrations has-pending-model-changes`: «No changes have been made to the model since the last migration». No hace falta migración.
- `dotnet test --filter "Category!=SqlServer"` (equivalente a CI): unitarias **312/312** e integración **142/142**.
- `dotnet test` completo con SQL Server local: unitarias **312/312** e integración **439/439**.
- `dotnet ef database update` contra `Local133` (dev): sin migraciones pendientes.
- `dotnet publish src/Local133.Web -c Release -r linux-x64`: correcto. Incluye `libSkiaSharp.so` y `wwwroot/css/site.css` generado, y el `runtimeconfig` pide .NET 10.
- No hubo bloqueos de Smart App Control.

## Revisión en navegador (Playwright, Edge)
Sitio arrancado con `dotnet bin/Debug/net10.0/Local133.Web.dll --urls http://localhost:5231` (Development):
- **Arranque**: Hangfire, Sass y los jobs (`expirar-ventas`, sondeo: 0 cuentas, sin llamadas a Pey) arrancaron. El log quedó sin `ERR`, `WRN` ni `FTL`.
- **Identity**: registro de un vendedor nuevo (`net10.prueba@local133.test`), con validación en cliente al enviar vacío. Confirmación con el `.html` de `App_Data/emails` y login hasta `/app`.
- **Pantallas en 375, 768 y 1280 px**: `/`, `/app`, `/app/cobrar`, `/app/cobrar/productos`, `/app/productos`, `/app/productos/nuevo`, `/app/ventas`, `/app/cuenta-bancaria`, `/terminos` y `/swagger/index.html`. Todas 200, sin scroll horizontal, con la pestaña activa correcta (Inicio, Cobrar, Ventas, Más), consola sin errores y ningún recurso ≥ 400. `site.css` se sirve con huella (`site.<hash>.css`) y aplica el color de marca.
- **Alta de producto** a 375 px: validación en cliente (nombre y precio) y guardado con regreso a `/app/productos`.
- **Capturas** en `.playwright-mcp/net10-*.png`.
- No se probó el cobro hasta Pey: el vendedor nuevo no tiene cuenta bancaria y no se llama a Pey real en esta revisión.

## Pendientes
- Cambiar la pila del App Service `local133` a **.NET 10** antes del próximo despliegue (ver `azure.md`).
- Confirmar que CI (GitHub Actions, Ubuntu) pasa con el SDK 10.0.401: aquí solo se corrió el equivalente local.
