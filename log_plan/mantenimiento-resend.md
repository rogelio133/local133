# Mantenimiento — Correo con Resend

**Estado:** ✅ hecho 2026-11-17 · [← Plan](../PLAN_DESARROLLO.md) · rama `feature_resend`

## Alcance
Enviar de verdad los correos de la cuenta (confirmar correo, restablecer contraseña, cambiar correo) con Resend, siguiendo la guía https://resend.com/setup.md. No es una subfase del plan: es mantenimiento que adelanta el envío real que necesita Notificaciones (Fase 5).

Decisiones con el usuario:
- Solo **correo de la app** (sin marketing ni buzón de entrada).
- Primero sin dominio, con `onboarding@resend.dev`. Después el usuario agregó y verificó `notificaciones.local133.com.mx`.
- Remitente: `Local133 <hola@notificaciones.local133.com.mx>`.
- El **HTML se queda en el código** (`Web/Identity/AccountEmails`); sin plantillas de Resend por ahora.
- La app usa una llave de **solo envío** en user-secrets. El usuario la creó directamente; no hizo falta la llave de acceso total que proponía la guía.

## Qué se hizo
- Paquete `Resend` 0.26.0 (SDK oficial, github.com/resend/resend-dotnet) en `Directory.Packages.props` y `Local133.Infrastructure`. Sus dependencias `net10.0` son 10.0.15, las mismas del resto.
- `Infrastructure/Email`:
  - `ResendEmailOptions` (sección `Resend`: `ApiKey`, `ApiUrl`, timeouts y reintentos) y `EmailOptions` (`Email:From`).
  - `ResendEmailSender : IEmailSender`: llave de idempotencia `account-email/{guid}` por envío; loguea el id de Resend, el asunto y solo el dominio del destinatario; ante `ResendException` final loguea el error y no lanza.
  - `EmailServiceCollectionExtensions.AddEmail`: Resend solo si existe `Resend:ApiKey`; si no, `FileEmailSender`. `HttpClient` tipado con `AddStandardResilienceHandler` (reintento en red, timeout, 5xx y 429 con `Retry-After`; los demás 4xx no).
  - Hallazgo: con `ApiToken` vacío el SDK usa la variable de entorno `RESEND_API_KEY`. Como en la sesión de desarrollo esa variable es la llave de acceso total, Resend solo se registra con `Resend:ApiKey`.
- `appsettings.json`: `Email:From` = `Local133 <hola@notificaciones.local133.com.mx>` (empezó con `onboarding@resend.dev`).
- `Local133WebFactory`: vacía `Resend:ApiKey` (`UseSetting` y en memoria).
- Documentación: `CLAUDE.md`, `README.md`, `azure.md` y `PLAN_DESARROLLO.md`.

## Pruebas
- `dotnet build`: 0 advertencias, 0 errores.
- `ResendEmailSenderTests` (WireMock, sin SQL Server, corre en CI): **8/8**. Cubre:
  - campos y cabeceras (`Authorization`, `Idempotency-Key`);
  - una llave por envío;
  - 503 → 200 con la misma llave;
  - 429 con `Retry-After`;
  - 422 sin reintento ni excepción;
  - 500 persistente sin excepción;
  - el log no trae el cuerpo ni el destinatario;
  - sin llave ⇒ `FileEmailSender`.
- `dotnet test` completo con SQL Server local: unitarias **312/312** e integración **454/454**.
- `dotnet list package --vulnerable --include-transitive`: sin paquetes vulnerables.

## Prueba real contra Resend (2026-11-17)
El usuario creó la llave y la guardó en user-secrets (`Resend:ApiKey`); no se usó una llave de acceso total. Se comprobó que es **de solo envío**: `GET /domains` ⇒ 401 `restricted_api_key`.

Sitio en Development con `Jobs__Enabled=false` (sin llamadas a Pey). Remitente `onboarding@resend.dev`, destinatarios de prueba de Resend:

| Flujo | Destinatario | Resultado |
|---|---|---|
| Registro | `delivered+registro@resend.dev` | 200, id `01a1ec03-83fb-7375-a1cd-26a173b397a6` (0.7 s, sin reintentos) |
| «Reenviar correo» | `delivered+registro@resend.dev` | 200, id `01a1ec03-dca7-7bd6-9908-b762619e9c7a` |
| «¿Olvidaste tu contraseña?» (usuario confirmado por SQL, porque sin llave total no se puede leer la liga del correo) | `delivered+registro@resend.dev` | 200, id `01a1ec04-2eb6-7d10-a033-fd4376c79694` |
| Registro con un destinatario que `onboarding@resend.dev` no permite | `rechazo.resend@example.com` | 422 `validation_error`, sin reintento, `ERR` en el log; el registro termina en «Revisa tu correo» |
| Idempotencia: dos `POST /emails` iguales con la misma `Idempotency-Key` (curl) | `delivered+idempotencia@resend.dev` | el mismo id las dos veces (`01a1ec04-5b92-787c-a322-77cbe96e15e2`) ⇒ un solo correo |

- Log: solo el id, el asunto y el dominio del destinatario, sin el cuerpo.
- No se consultó el estatus `delivered` con `GET /emails/{id}`: la llave de solo envío no lo permite. Revisarlo en el panel de Resend (Emails), con *Insights*: `https://resend.com/emails/<id>?tab=insights`.

Usuarios de prueba que quedaron en la base de desarrollo: `delivered+registro@resend.dev` (confirmado) y `rechazo.resend@example.com`.

## Revisión en navegador (Playwright, Edge)
- Registro, RegisterConfirmation, ResendEmailConfirmation, ForgotPassword y ForgotPasswordConfirmation a 375 px. RegisterConfirmation también a 768 y 1280 px. Las vistas no cambiaron; se revisó el flujo después del envío.
- Validación en cliente del registro vacío: aparecen «Escribe tu correo.», «Escribe una contraseña.» y la casilla de términos, sin enviar.
- Consola sin errores ni advertencias; todos los recursos 200/304.
- Nota: `fill_form` de Playwright no marcó la casilla de términos después del error de validación. Un clic directo la marca y quita el error, así que es del llenado automático y no de la página.

## Dominio de envío (2026-11-17)
El usuario agregó `notificaciones.local133.com.mx` en Resend y aparece como **Verified**. DNS en Akky (`ns1-3.akkyservicios.mx`), revisado con `nslookup` contra 1.1.1.1:
- DKIM: TXT `resend._domainkey.notificaciones.local133.com.mx` publicado.
- Return path: `send.notificaciones.local133.com.mx` es CNAME de `send.forge.rmta.net`, con MX `feedback.forge.rmta.net` y SPF `v=spf1 … ~all`.
- DMARC: no hay en el subdominio, pero `_dmarc.local133.com.mx` = `v=DMARC1; p=none;` y lo cubre como dominio organizacional. No hizo falta agregar nada.

Pruebas desde el dominio, a direcciones de prueba de Resend:
- `POST /emails` directo con la llave de la app: 200, id `01a1ec0c-1392-7966-a4e9-661be86571d3`. La llave puede enviar desde el dominio.
- Registro en el sitio con `Email:From` nuevo (`delivered+dominio@resend.dev`): 200, id `01a1ec0c-d814-7a9a-abbc-8091d0cffbc2`.
- `dotnet build`: correcto. El cambio es solo de configuración; las pruebas de WireMock fijan su propio `Email:From`.

## Primer correo real a un buzón propio (2026-11-17)
El correo llegó a **spam**. *Insights* de Resend señaló dos problemas:
- **Remitente `no-reply@`**: se cambió a `Local133 <hola@notificaciones.local133.com.mx>` (`appsettings.json`, valor por defecto de `EmailOptions`, documentación y la constante de las pruebas).
  - Ojo: ni `notificaciones.local133.com.mx` ni `local133.com.mx` tienen MX, así que si alguien responde a `hola@`, la respuesta rebota. Falta decidir un buzón: receiving de Resend en el subdominio, o un `Reply-To` a un buzón real.
- **Liga a `localhost`**: las ligas de Identity se arman con el host de la petición (`Url.Page(..., protocol: Request.Scheme)`). Por eso, desde el sitio local salen con `http://localhost:5231`. En producción salen con el dominio público. Para probar entregabilidad en local sin `localhost`, registrarse entrando por el túnel HTTPS (`devtunnel host local133-pey`): el sitio respeta `X-Forwarded-*` y la liga sale con la URL del túnel.
- No se pudo recompilar en ese momento: Visual Studio tenía el sitio corriendo y bloqueaba las DLL. El cambio es de configuración; hay que reiniciar el sitio, porque `IOptions` no recarga el remitente.

## Pendiente
- Restringir la llave de la app a `notificaciones.local133.com.mx` en el panel de Resend (la API no edita permisos) y crear la de producción para Key Vault.
- Repetir la prueba en un buzón propio con `hola@` y una liga que no sea `localhost` (túnel o producción), y confirmar que llega a la bandeja de entrada.
- Buzón para las respuestas a `hola@` (ver arriba).
- Después del piloto: DMARC `p=quarantine` con `rua` (ver `azure.md`).
