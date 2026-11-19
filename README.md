# Local133 — Portal de cobros CoDi

Portal para que vendedores informales cobren con CoDi vía la API de Pey. El plan completo está en [PLAN_DESARROLLO.md](PLAN_DESARROLLO.md) y las reglas de trabajo en [CLAUDE.md](CLAUDE.md).

- **Web**: ASP.NET Core 10 MVC (vistas Razor + Bootstrap 5, móvil primero) + EF Core 10 sobre SQL Server (`src/Local133.Web`, `Domain`, `Application`, `Infrastructure`). Un solo proyecto sirve el portal del vendedor (`/app`), el admin (`/admin`) y la API interna (`/api`).
- **Librerías cliente**: Bootstrap, Bootstrap Icons y jQuery Validation vía LibMan ([libman.json](src/Local133.Web/libman.json)), versionadas en `src/Local133.Web/wwwroot/lib`. Sin Node.js ni build de JS.

## Requisitos

| Herramienta | Versión |
|---|---|
| .NET SDK | 10.0.401 o superior de la línea 10.0 (ver [global.json](global.json)) |
| SQL Server | Instancia local, p. ej. SQL Server Express `localhost\SQLEXPRESS`, con autenticación de Windows. **Sin Docker.** |

## Primer arranque

```powershell
# 1. Herramientas locales (dotnet-ef, libman)
dotnet tool restore

# 2. Cadena de conexión en user-secrets (nunca en appsettings)
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost\SQLEXPRESS;Database=Local133;Trusted_Connection=True;TrustServerCertificate=True" --project src/Local133.Web

# 3. Crear la base Local133 y aplicar migraciones (EF crea la base si no existe)
dotnet ef database update --project src/Local133.Infrastructure --startup-project src/Local133.Web

# 4. Usuario administrador: se crea al arrancar el sitio (correo ya confirmado, rol Admin)
dotnet user-secrets set "Seed:Admin:Email" "<correo-admin>" --project src/Local133.Web
dotnet user-secrets set "Seed:Admin:Password" "<contraseña de 8+ caracteres con un número>" --project src/Local133.Web
```

El seed del admin es idempotente: si el usuario ya existe no cambia su contraseña (cámbiala en `/Identity/Account/Manage`), y nunca convierte en admin el correo de un vendedor. En producción usa las variables de entorno `Seed__Admin__Email` y `Seed__Admin__Password`. Sin ellas no se siembra nada.

Comprobar: `sqlcmd -S localhost\SQLEXPRESS -E -d Local133 -Q "SELECT * FROM SystemSettings"` devuelve la fila semilla (IVA 16 %, 5 días de gracia, QR de 600 s), y `SELECT Name FROM AspNetRoles` los roles `Merchant` y `Admin` (migración `Identity`).

## Desarrollo diario

```powershell
# Sitio en http://localhost:5231
dotnet run --project src/Local133.Web --launch-profile http
```

| URL | Qué es |
|---|---|
| `/` | Portada pública |
| `/app` (`/app/cobrar`, `/app/ventas`, `/app/mas`) | Portal del vendedor (pestañas inferiores en móvil). Inicio con el checklist de activación según su estatus |
| `/admin` (`/admin/clientes`, `/admin/transacciones`, `/admin/parametros`) | Portal administrador (sidebar / Offcanvas) |
| `/api/health` | Salud (JSON); `/api/*` inexistente ⇒ 404 ProblemDetails |
| `/swagger` | Swagger (solo en Development) |
| `/Identity/Account/Register`, `/Login`, `/ForgotPassword`, `/Manage` | Cuenta (ASP.NET Core Identity scaffoldeado, en español). Registro ⇒ rol `Merchant`; `/app` exige `Merchant` y `/admin` exige `Admin` |

**Correos**: sin `Resend:ApiKey` no se envían; `FileEmailSender` guarda cada uno como `.html` en `src/Local133.Web/App_Data/emails` (ignorada en git) y escribe la liga en el log. Ábrelo para confirmar la cuenta o restablecer la contraseña. Carpeta configurable con `EmailPickup:Directory`.

Con una llave de **solo envío** de Resend se envían de verdad:

```powershell
dotnet user-secrets set "Resend:ApiKey" "<re_...>" --project src/Local133.Web
```

El remitente es `Email:From` (`appsettings.json`): `Local133 <hola@notificaciones.local133.com.mx>`, dominio verificado en Resend. Con la llave configurada los correos llegan de verdad: para probar sin escribirle a nadie, usa las direcciones de prueba de Resend (`delivered+<etiqueta>@resend.dev`). Para volver a guardarlos en disco: `dotnet user-secrets remove "Resend:ApiKey" --project src/Local133.Web`.

**Login con Google / Facebook (opcional)**: los botones solo aparecen si hay credenciales en user-secrets (sin ellas el sitio funciona igual).

```powershell
dotnet user-secrets set "Authentication:Google:ClientId" "<client-id>" --project src/Local133.Web
dotnet user-secrets set "Authentication:Google:ClientSecret" "<client-secret>" --project src/Local133.Web
dotnet user-secrets set "Authentication:Facebook:AppId" "<app-id>" --project src/Local133.Web
dotnet user-secrets set "Authentication:Facebook:AppSecret" "<app-secret>" --project src/Local133.Web
```

Registra las URI de redirección en Google Cloud Console (cliente OAuth "Aplicación web") y en Meta for Developers (Inicio de sesión con Facebook): `https://localhost:7180/signin-google` y `https://localhost:7180/signin-facebook`. Arranca con el perfil `https` (`--launch-profile https`); Facebook exige HTTPS. Reglas: si conservas el correo del proveedor, la cuenta queda confirmada y entras de inmediato; si escribes otro, se confirma por correo; si el correo ya tiene cuenta, no se liga solo: entra con contraseña y liga desde `/Identity/Account/Manage/ExternalLogins`.

**Pey (CoDi)**: el cliente (`IPaymentGateway`, en `src/Local133.Infrastructure/Pey`) usa una sola API key de la plataforma. Sin ella el sitio arranca igual, pero cualquier llamada a Pey falla con un mensaje que indica cómo configurarla. Pey **no tiene sandbox**: las pruebas reales son en producción con $0.01 y la CLABE de prueba `012180015826225713`.

```powershell
dotnet user-secrets set "Pey:ApiKey" "<api-key>" --project src/Local133.Web
```

Opcional en la sección `Pey`: `BaseUrl` (por defecto `https://api.pey.mx`), `AttemptTimeout` (30 s), `TotalTimeout` (90 s), `MaxRetryAttempts` (3) y `RetryDelay` (1 s). Las pruebas de `PeyClient` usan un Pey simulado con WireMock y corren en CI.

**Webhook de Pey** (`POST /api/webhooks/pey`): confirma los pagos y la activación de cuentas bancarias. Pey firma con Standard Webhooks. El secreto (`whsec_…`) sale de portal.mipey.mx al registrar la URL del webhook. Sin él, el endpoint responde 500 y Pey reintenta.

```powershell
dotnet user-secrets set "Pey:WebhookSecret" "whsec_<secreto>" --project src/Local133.Web
```

Para recibirlo en local hace falta un túnel HTTPS (ngrok o dev tunnels) hacia el sitio. Registra esa URL en portal.mipey.mx y revisa sus entregas en "Webhook Logs".

**Jobs (Hangfire)**: el sitio programa el sondeo de cuentas bancarias en revisión (cada 30 min) y la expiración de ventas (cada minuto). Usan la misma base de datos, en el esquema `HangFire`, que Hangfire crea solo al arrancar. Para apagarlos: `Jobs:Enabled=false` (variable `Jobs__Enabled=false`).

## Comandos

| Tarea | Comando |
|---|---|
| Compilar | `dotnet build` (advertencias = errores) |
| Pruebas | `dotnet test` (las de integración usan la base `Local133_Tests` en `localhost\SQLEXPRESS`, creada con migraciones y limpiada con Respawn; otra instancia con la variable `LOCAL133_TEST_DB`) · una prueba: `dotnet test --filter "FullyQualifiedName~<Clase>.<Metodo>"` · como CI: `dotnet test --filter "Category!=SqlServer"` |
| Nueva migración | `dotnet ef migrations add <Nombre> --project src/Local133.Infrastructure --startup-project src/Local133.Web` |
| Aplicar migraciones | `dotnet ef database update --project src/Local133.Infrastructure --startup-project src/Local133.Web` |
| Librerías cliente | editar `src/Local133.Web/libman.json` y, desde esa carpeta, `dotnet tool run libman restore` (versionar el resultado en `wwwroot/lib`) |
| Publicar | `dotnet publish src/Local133.Web -c Release -o <carpeta>` |

## Smart App Control (Windows)

En equipos con Smart App Control activo, Windows puede bloquear DLL recién compiladas (`0x800711C7`). Por eso `UseAppHost=false`: todo corre vía `dotnet.exe` (`dotnet run` o `dotnet src/Local133.Web/bin/Debug/net10.0/Local133.Web.dll`). Detalles y qué hacer si una DLL queda bloqueada en [CLAUDE.md](CLAUDE.md#smart-app-control-este-equipo). No desactivar Smart App Control.

## Integración continua

[.github/workflows/ci.yml](.github/workflows/ci.yml) corre en cada PR y en push a `main`: restore, build Release, verificación de que las migraciones están al día con el modelo (`dotnet ef migrations has-pending-model-changes`) y `dotnet test --filter "Category!=SqlServer"` (las pruebas de integración marcadas `Category=SqlServer` necesitan SQL Server y solo corren en local).

CI no usa SQL Server ni llama a Pey (las pruebas de `PeyClient` van contra WireMock).
