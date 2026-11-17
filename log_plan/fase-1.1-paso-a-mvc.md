# Fase 1.1 — Limpieza y paso a MVC

**Estado:** ✅ hecho 2026-11-09 · [← Plan](../PLAN_DESARROLLO.md#fase-1--registro-y-autenticación-1-semana)

## Alcance (del plan)
Eliminar `src/Local133.CustomerPortal` (y su `.esproj` de la solución), el target `BuildSpaForPublish` / `SkipSpaBuild` del `.csproj`, el job `spa` de `.github/workflows/ci.yml` y el `MapFallbackToFile("index.html")`. Renombrar `Local133.Api` → `Local133.Web` (carpeta, `.csproj`, namespaces, `.sln`, CI, README). `AddControllersWithViews` + `MapRazorPages`; `_Layout` Bootstrap 5 (LibMan) móvil primero; áreas `Merchant` y `Admin`; rutas `/api/*` desconocidas siguen respondiendo 404 ProblemDetails.

## Qué se hizo
- Rutas por atributo en español — `/`, `/app` (+`cobrar`, `ventas`, `mas`) y `/admin` (+`clientes`, `transacciones`, `parametros`) con las mismas pantallas placeholder de la SPA (parcial `_ComingSoon`).
- Errores separados: `/api/*` ⇒ ProblemDetails, resto ⇒ vistas `/error` y `/error/{code}`.
- LibMan como herramienta local con Bootstrap 5.3.8, Bootstrap Icons, jQuery y jQuery Validation Unobtrusive, `wwwroot/lib` versionado; `MapStaticAssets` con fingerprint.
- Verificado arrancando el sitio: todas las rutas 200, `/api/nada` 404 problem+json, `/nada` 404 HTML, Swagger solo con `/api/health`.
- Sin guards por rol hasta 1.2.
