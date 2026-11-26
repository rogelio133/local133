# Mantenimiento: «Algo salió mal» al iniciar sesión (doble envío)

**Estado:** ✅ hecho 2026-11-26 · [← Plan](../PLAN_DESARROLLO.md)

## Incidente
En producción (App Service `local133`), al entrar con correo y contraseña salió la página «Algo salió mal» con la referencia `0HNQBFCSJFJ1U:00000001`. El log del contenedor (26 nov, 17:16 UTC) muestra:

```
17:16:09.47  INF  Inicio de sesión con contraseña.
17:16:09.52  POST /Identity/Account/Login  → 302   (1er envío: entró)
17:16:10.02  GET  /app                     → 499   (el navegador abandona esa carga)
17:16:10.05  POST /Identity/Account/Login  → 400   (2º envío)
17:16:10.10  POST /error/400               → 400   («Algo salió mal»)
```

No hubo excepción. El formulario se envió dos veces (Enter + clic, doble clic o el administrador de contraseñas). El segundo POST llevaba el token antiforgery del usuario **anónimo**, pero ya con la cookie de sesión del primero. El token era de otro usuario, así que la validación de Razor Pages respondió 400. La sesión sí quedó iniciada.

Causa de fondo: `form[data-disable-on-submit]` (`wwwroot/js/site.js`) solo estaba en formularios de Merchant y Admin. Ninguna página de Identity lo usaba.

## Qué se hizo
- **`data-disable-on-submit` en todos los formularios de Identity**: Login, Register, ExternalLogin, ForgotPassword, ResetPassword, ResendEmailConfirmation, Logout, Manage (ChangePassword, Email, SetPassword, ExternalLogins) y el «Salir» de `_LoginPartial`. El doble clic en «Salir» tenía el mismo problema, al revés: token del usuario y petición ya anónima.
- **`site.js`**: un botón deshabilitado no se envía. Si el botón pulsado trae `name`/`value` (los de Google/Facebook, `provider=Google`), se copia a un campo oculto `[data-submitter-value]` antes de deshabilitar.
- **Respaldo en el servidor** para cuando el JS no alcanza (dos pestañas, JS sin cargar, Enter antes de que corra el handler): `Web/Identity/SignedInAntiforgeryRedirectAttribute`, en `LoginModel` y `ExternalLoginModel`.
  - Es un `IAsyncAlwaysRunResultFilter`. La validación antiforgery es un filtro de autorización, y cuando corta la petición solo los filtros «siempre» ven su resultado.
  - Si el resultado es `IAntiforgeryValidationFailedResult` **y** la petición ya trae sesión, redirige al `returnUrl` local o a `PostLoginRedirect.HomeUrlAsync`, y lo deja en el log («Formulario de … enviado otra vez con la sesión ya iniciada»).
  - Sin sesión, el 400 se queda: el token sigue protegiendo el login.

## Pruebas
- `Identity/AccountFlowTests` (integración):
  - `Doble_envio_del_login_redirige_al_inicio_en_vez_de_error` (sin y con `returnUrl`);
  - `Doble_envio_del_login_no_sigue_un_returnUrl_externo`;
  - `Login_sin_sesion_con_token_invalido_sigue_respondiendo_400`.
- Suite de integración: 808 de 809. La que falla, `EmailLayoutTests.El_logo_existe_en_wwwroot`, falla solo porque se compiló con `--artifacts-path` fuera del repo: Visual Studio tenía bloqueado `bin/Debug`, y la prueba busca el `.sln` subiendo desde la carpeta de la build. **Repetir con `dotnet test` normal** cuando el sitio no esté abierto en Visual Studio.

## Revisión en navegador (Playwright, Edge)
Copia del sitio en `http://localhost:5239`, sin Pey ni Resend. Se dio de alta un usuario de prueba `doble-envio@prueba.mx` en la base de desarrollo.
- Doble clic en «Entrar»: un solo POST, llega a `/app`.
- Doble clic en «Cerrar sesión»: un solo POST, llega a `/`.
- Caso de producción sin JS (primer POST por `fetch`, segundo con `form.submit()` y el mismo token): llega a `/app/ventas` (`returnUrl`), y el log registra la redirección del filtro.
- Doble clic en «Entrar con Google» (petición interceptada): un solo POST con `provider=Google`.
- Validación en cliente con el formulario vacío: no hay POST y el botón sigue habilitado.
- Error de contraseña en 375, 768 y 1280 px: sin scroll horizontal, mensaje visible y botón habilitado después. Consola sin errores ni 404.

## Pendientes
- Correr la suite completa sin `--artifacts-path` (ver «Pruebas»).
