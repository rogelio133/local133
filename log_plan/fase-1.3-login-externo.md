# Fase 1.3 — Login externo Google y Facebook

**Estado:** ✅ hecho 2026-11-10 · [← Plan](../PLAN_DESARROLLO.md#fase-1--registro-y-autenticación-1-semana)

## Alcance (del plan)
Login externo Google y Facebook.

## Qué se hizo
- Las páginas `ExternalLogin` y `Manage/ExternalLogins` ya venían del scaffold de 1.2. Se agregaron los paquetes `Microsoft.AspNetCore.Authentication.Google`/`.Facebook` 9.0.22.
- Los proveedores se registran solo si hay credenciales (`Authentication:Google:ClientId/ClientSecret`, `Authentication:Facebook:AppId/AppSecret` en user-secrets). Sin ellas no hay botones y CI/pruebas no dependen de ellos.
- Reglas: si se conserva el correo del proveedor ⇒ cuenta confirmada y entra directo como `Merchant`. Si se escribe otro ⇒ correo de confirmación. Si el correo ya tiene cuenta ⇒ no se liga solo: mensaje para entrar con contraseña y ligar desde Mi cuenta → Cuentas externas.
- Google se pide con `prompt=select_account`: después de cerrar sesión muestra "Elige una cuenta" en lugar de volver a entrar solo con la misma cuenta (teléfonos compartidos). No pide la contraseña.
- Cancelar o fallar en el proveedor regresa a Login (o a Cuentas externas) con mensaje en español. Botones con icono (parcial `_ExternalProviderButton`).

## Pruebas
- 12 pruebas nuevas: 9 de flujo con un proveedor falso (`FakeExternalAuthHandler`, `Category=SqlServer`) y 3 de registro condicional que corren en CI. 38/38 verdes en local.

## Pendientes
- Prueba real con credenciales de Google/Facebook (crear clientes OAuth y registrar `https://localhost:7180/signin-google` y `/signin-facebook`).
