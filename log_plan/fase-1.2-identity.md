# Fase 1.2 — Scaffold de Identity

**Estado:** ✅ hecho 2026-11-09 · [← Plan](../PLAN_DESARROLLO.md#fase-1--registro-y-autenticación-1-semana)

## Alcance (del plan)
**Scaffold de Identity** (`dotnet aspnet-codegenerator identity`) sobre `AppDbContext` + roles, solo con las páginas necesarias (Register, Login, Logout, ConfirmEmail, ForgotPassword, ResetPassword, ExternalLogin, Manage); traducir al español y adaptarlas al layout. Registro con correo/contraseña, verificación de correo, login, logout, recuperación.

## Qué se hizo
- Generador `dotnet-aspnet-codegenerator` 9.0.0 (corrió bien). Se deshicieron su `AppDbContext`/`IdentityUser` propios y sus cambios en `Program.cs`/`appsettings`.
- `ApplicationUser` y un `AppDbContext : IdentityDbContext<ApplicationUser>` en Infrastructure. Migración `Identity` con los roles `Merchant`/`Admin` sembrados con Id fijo.
- Se usa `AddIdentity` sin `AddDefaultUI`: solo existen las páginas scaffoldeadas; 2FA y datos personales dan 404. Rutas por defecto `/Identity/Account/*`.
- Páginas, validación y correos en español, más `SpanishIdentityErrorDescriber` y mensajes de model binding. Contraseña de 8+ caracteres con número; bloqueo tras 5 intentos.
- El registro asigna el rol `Merchant`. Tras el login, sin `returnUrl`, Admin va a `/admin` y Merchant a `/app`.
- Áreas con `[Authorize(Roles=…)]`. `/api/*` sin sesión responde 401/403 en vez de redirigir.
- `FileEmailSender` en dev (`App_Data/emails` + log).

## Pruebas
- `tests/Local133.IntegrationTests` con 26 pruebas (`Category=SqlServer`, base `Local133_Tests`, Respawn), todas verdes en local. CI las excluye con `--filter "Category!=SqlServer"`.
- Prueba de arranque del sitio real: redirección a login, registro y `.html` del correo generado, Swagger solo con `/api/health`.

## Notas
- El admin sembrado quedó en 1.5.
