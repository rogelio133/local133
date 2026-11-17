# Fase 0.2 — Base de datos local y CI

**Estado:** ✅ hecho 2026-11-09 · [← Plan](../PLAN_DESARROLLO.md#fase-0--cimientos-3-días)

## Alcance (del plan)
Base de datos en el **SQL Server local del equipo** (sin Docker): crear la base `Local133`, cadena de conexión en user-secrets (`ConnectionStrings:Default`, p. ej. `Server=localhost\SQLEXPRESS;Database=Local133;Trusted_Connection=True;TrustServerCertificate=True`) y aplicar migraciones con `dotnet ef database update`; `README` con arranque local y requisitos (instancia de SQL Server instalada); CI (build + tests).

## Qué se hizo
- Base `Local133` en `localhost\SQLEXPRESS` con `Initial` y semilla.
- `README.md`.
- GitHub Actions `.github/workflows/ci.yml` con job .NET (build Release, `dotnet ef migrations has-pending-model-changes`, `dotnet test`).
- Los proyectos de prueba se crean en Fase 1 (CI no usa SQL Server todavía).

## Nota: SPA descartada (2026-11-09)
El antiguo punto 0.2 (SPA React en `src/Local133.CustomerPortal`) se construyó y quedó **descartado** al cambiar el frontend a MVC Razor. Su código, el target `BuildSpaForPublish` y el job `spa` de CI se eliminaron en el paso 1.1.
