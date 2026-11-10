# Local133 — Portal de cobros CoDi

Portal para que vendedores informales cobren con CoDi vía la API de Pey. El plan completo está en [PLAN_DESARROLLO.md](PLAN_DESARROLLO.md) y las reglas de trabajo en [CLAUDE.md](CLAUDE.md).

- **API**: ASP.NET Core 9 + EF Core 9 sobre SQL Server (`src/Local133.Api`, `Domain`, `Application`, `Infrastructure`).
- **SPA**: React + Vite + TypeScript + React-Bootstrap (`src/Local133.CustomerPortal`), portal del vendedor (`/app`) y admin (`/admin`). La API la sirve desde `wwwroot`.

## Requisitos

| Herramienta | Versión |
|---|---|
| .NET SDK | 9.0.306 o superior de la banda 9.0.3xx (ver [global.json](global.json)) |
| Node.js | 22 LTS o superior (CI usa 24) |
| SQL Server | Instancia local, p. ej. SQL Server Express `localhost\SQLEXPRESS`, con autenticación de Windows. **Sin Docker.** |

## Primer arranque

```powershell
# 1. Herramientas locales (dotnet-ef)
dotnet tool restore

# 2. Cadena de conexión en user-secrets (nunca en appsettings)
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost\SQLEXPRESS;Database=Local133;Trusted_Connection=True;TrustServerCertificate=True" --project src/Local133.Api

# 3. Crear la base Local133 y aplicar migraciones (EF crea la base si no existe)
dotnet ef database update --project src/Local133.Infrastructure --startup-project src/Local133.Api

# 4. Dependencias de la SPA
cd src/Local133.CustomerPortal
npm install
```

Comprobar: `sqlcmd -S localhost\SQLEXPRESS -E -d Local133 -Q "SELECT * FROM SystemSettings"` devuelve la fila semilla (comisión 3%, IVA 16%).

## Desarrollo diario

```powershell
# API en http://localhost:5231 (Swagger en /swagger, salud en /api/health)
dotnet run --project src/Local133.Api --launch-profile http

# SPA en http://localhost:5173 (otra terminal; arrancar la API antes)
cd src/Local133.CustomerPortal
npm run dev
```

Vite hace proxy de `/api` y `/hubs` a `http://localhost:5231`; para otro destino usar la variable `API_URL`.

## Comandos

| Tarea | Comando |
|---|---|
| Compilar .NET | `dotnet build` (advertencias = errores; **no** compila la SPA) |
| Pruebas | `dotnet test` · una prueba: `dotnet test --filter "FullyQualifiedName~<Clase>.<Metodo>"` |
| Nueva migración | `dotnet ef migrations add <Nombre> --project src/Local133.Infrastructure --startup-project src/Local133.Api` |
| Aplicar migraciones | `dotnet ef database update --project src/Local133.Infrastructure --startup-project src/Local133.Api` |
| SPA: build / lint | `npm run build` (publica en `src/Local133.Api/wwwroot`, ignorado en git) · `npm run lint` |
| Publicar | `dotnet publish src/Local133.Api -c Release -o <carpeta>` — ejecuta `npm ci` + `npm run build` e incluye la SPA. Omitirla con `-p:SkipSpaBuild=true`. |

## Smart App Control (Windows)

En equipos con Smart App Control activo, Windows puede bloquear DLL recién compiladas (`0x800711C7`). Por eso `UseAppHost=false`: todo corre vía `dotnet.exe` (`dotnet run` o `dotnet src/Local133.Api/bin/Debug/net9.0/Local133.Api.dll`). Detalles y qué hacer si una DLL queda bloqueada en [CLAUDE.md](CLAUDE.md#smart-app-control-este-equipo). No desactivar Smart App Control.

## Integración continua

[.github/workflows/ci.yml](.github/workflows/ci.yml) corre en cada PR y en push a `main`:

- **.NET**: restore, build Release, verificación de que las migraciones están al día con el modelo (`dotnet ef migrations has-pending-model-changes`) y `dotnet test` (los proyectos de prueba se agregan en Fase 1).
- **SPA**: `npm ci`, `npm run lint`, `npm run build`.

CI no usa SQL Server ni llama a Pey.
