# Local133 — Portal de cobros CoDi

Portal para que vendedores informales cobren con CoDi vía la API de Pey. El plan completo está en [PLAN_DESARROLLO.md](PLAN_DESARROLLO.md) y las reglas de trabajo en [CLAUDE.md](CLAUDE.md).

- **Web**: ASP.NET Core 9 MVC (vistas Razor + Bootstrap 5, móvil primero) + EF Core 9 sobre SQL Server (`src/Local133.Web`, `Domain`, `Application`, `Infrastructure`). Un solo proyecto sirve el portal del vendedor (`/app`), el admin (`/admin`) y la API interna (`/api`).
- **Librerías cliente**: Bootstrap, Bootstrap Icons y jQuery Validation vía LibMan ([libman.json](src/Local133.Web/libman.json)), versionadas en `src/Local133.Web/wwwroot/lib`. Sin Node.js ni build de JS.

## Requisitos

| Herramienta | Versión |
|---|---|
| .NET SDK | 9.0.306 o superior de la banda 9.0.3xx (ver [global.json](global.json)) |
| SQL Server | Instancia local, p. ej. SQL Server Express `localhost\SQLEXPRESS`, con autenticación de Windows. **Sin Docker.** |

## Primer arranque

```powershell
# 1. Herramientas locales (dotnet-ef, libman)
dotnet tool restore

# 2. Cadena de conexión en user-secrets (nunca en appsettings)
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost\SQLEXPRESS;Database=Local133;Trusted_Connection=True;TrustServerCertificate=True" --project src/Local133.Web

# 3. Crear la base Local133 y aplicar migraciones (EF crea la base si no existe)
dotnet ef database update --project src/Local133.Infrastructure --startup-project src/Local133.Web
```

Comprobar: `sqlcmd -S localhost\SQLEXPRESS -E -d Local133 -Q "SELECT * FROM SystemSettings"` devuelve la fila semilla (comisión 3%, IVA 16%), y `SELECT Name FROM AspNetRoles` los roles `Merchant` y `Admin` (migración `Identity`).

## Desarrollo diario

```powershell
# Sitio en http://localhost:5231
dotnet run --project src/Local133.Web --launch-profile http
```

| URL | Qué es |
|---|---|
| `/` | Portada pública |
| `/app` (`/app/cobrar`, `/app/ventas`, `/app/mas`) | Portal del vendedor (pestañas inferiores en móvil) |
| `/admin` (`/admin/clientes`, `/admin/transacciones`, `/admin/parametros`) | Portal administrador (sidebar / Offcanvas) |
| `/api/health` | Salud (JSON); `/api/*` inexistente ⇒ 404 ProblemDetails |
| `/swagger` | Swagger (solo en Development) |
| `/Identity/Account/Register`, `/Login`, `/ForgotPassword`, `/Manage` | Cuenta (ASP.NET Core Identity scaffoldeado, en español). Registro ⇒ rol `Merchant`; `/app` exige `Merchant` y `/admin` exige `Admin` |

**Correos en desarrollo**: no se envían; `FileEmailSender` guarda cada uno como `.html` en `src/Local133.Web/App_Data/emails` (ignorada en git) y escribe la liga en el log. Ábrelo para confirmar la cuenta o restablecer la contraseña. Carpeta configurable con `EmailPickup:Directory`.

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

En equipos con Smart App Control activo, Windows puede bloquear DLL recién compiladas (`0x800711C7`). Por eso `UseAppHost=false`: todo corre vía `dotnet.exe` (`dotnet run` o `dotnet src/Local133.Web/bin/Debug/net9.0/Local133.Web.dll`). Detalles y qué hacer si una DLL queda bloqueada en [CLAUDE.md](CLAUDE.md#smart-app-control-este-equipo). No desactivar Smart App Control.

## Integración continua

[.github/workflows/ci.yml](.github/workflows/ci.yml) corre en cada PR y en push a `main`: restore, build Release, verificación de que las migraciones están al día con el modelo (`dotnet ef migrations has-pending-model-changes`) y `dotnet test --filter "Category!=SqlServer"` (las pruebas de integración marcadas `Category=SqlServer` necesitan SQL Server y solo corren en local).

CI no usa SQL Server ni llama a Pey (las pruebas de `PeyClient` van contra WireMock).
