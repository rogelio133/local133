# Publicación en Azure

Qué hay que considerar y hacer para publicar Local133 en Azure, según lo que agregó cada subfase. Se actualiza al terminar cada subfase. Si una subfase no cambia nada, se anota explícitamente. **Nunca** poner secretos reales aquí: van en App Settings o en Key Vault.

En App Settings los niveles de configuración se separan con `__` (p. ej. `Pey__ApiKey` equivale a `Pey:ApiKey`).

> Este archivo se creó en la Fase 2.8. Lo de las fases 0.1 a 3.2 se reconstruyó a partir de `CLAUDE.md` y de las bitácoras; conviene revisarlo antes del primer despliegue (Fase 7).

## Recursos

| Recurso | Para qué | Desde |
|---|---|---|
| App Service (.NET 10) | Sitio MVC, API `/api`, webhook de Pey, hub SignalR y jobs de Hangfire (en el mismo proceso) | Fase 0.1 / 1.1 |
| Azure SQL Database | Base `Local133` (EF Core) y el esquema `HangFire` (lo crea Hangfire, no EF). Tier: ver «Mantenimiento 2026-11-18» | Fase 0.2 / 2.3 |
| Key Vault (recomendado) | API key y secreto del webhook de Pey, secretos OAuth, contraseña del admin sembrado | Fase 1.3 / 1.5 / 2.1 |
| Blob Storage | Fotos de productos. Hoy es `LocalDiskFileStorage`; falta la implementación para blob (Fase 7) | Fase 3.1 |
| Azure SignalR Service (opcional) | Solo si hay más de una instancia | Fase 2.5 |
| App Service de la portada (.NET 10) | `Local133.LandingPage` en `local133.com.mx`: sin base, sin secretos, sin jobs. Puede compartir el App Service Plan del portal | Portada 2026-11-18 |

## Configuración (App Settings)

| Llave | Notas | Desde |
|---|---|---|
| `ConnectionStrings__Default` | Cadena de Azure SQL (de preferencia con identidad administrada) | Fase 0.2 |
| `Authentication__Google__ClientId` / `__ClientSecret` | Opcional: sin ellas no aparece el botón | Fase 1.3 |
| `Authentication__Facebook__AppId` / `__AppSecret` | Opcional: sin ellas no aparece el botón | Fase 1.3 |
| `Seed__Admin__Email` / `Seed__Admin__Password` | Siembra idempotente del admin al arrancar | Fase 1.5 |
| `Pey__ApiKey` | Referencia a Key Vault. Sin ella el sitio arranca, pero fallan las llamadas a Pey | Fase 2.1 |
| `Pey__WebhookSecret` | `whsec_…`. Sin él el webhook responde 500 | Fase 2.3 |
| `Jobs__Enabled` | `true` por defecto (sondeo de cuentas cada 30 min, expiración de ventas y conciliación de pagos cada minuto) | Fase 2.3 / 2.5 / 2.9 |
| `ForwardedHeaders__KnownNetworks__0` / `__1` (o `KnownProxies__0`) | Red o IP del front-end de App Service o del proxy. En App Service: `0.0.0.0/0` y `::/0` (el proceso solo recibe tráfico del front-end de la plataforma; su IP interna no es fija). Sin esto el sitio no confía en `X-Forwarded-Proto`: la redirección a HTTPS puede ciclarse, el webhook recibe 307 y el login de Google/Facebook falla con `redirect_uri_mismatch` (callback con `http://`) | Fase 2.5 (corrección 2026-11-13) |
| `Storage__LocalDirectory` | Mientras no exista el blob: carpeta persistente (`/home/...` en Linux). No sirve con varias instancias | Fase 3.1 |
| `App__PublicBaseUrl` | Dominio público, p. ej. `https://local133.mx`. Ver Fase 2.8. **Obligatoria** desde el correo de cuenta activa: sin ella ese correo no sale | Fase 2.8 · Correo de cuenta activa 2026-11-18 |
| `Resend__ApiKey` | Referencia a Key Vault. Llave de **solo envío** restringida al dominio de envío. Sin ella los correos se guardan en disco y **no salen** | Mantenimiento 2026-11-17 |
| `Email__From` | Opcional: `appsettings.json` ya trae `Local133 <hola@notificaciones.local133.com.mx>`. Si se cambia, debe ser de un dominio verificado en Resend | Mantenimiento 2026-11-17 |

## Por subfase

### Fase 0.1 / 0.2 — Solución y base de datos
- Crear la base en Azure SQL. Las migraciones las aplica el sitio al arrancar: ver «Mantenimiento 2026-11-18 — Migraciones al arrancar».
- Serilog escribe en consola y en `logs/`: en App Service conviene la consola (Log Stream / Application Insights).

### Fase 1.1 – 1.5 — MVC, Identity, login externo, admin
- HTTPS obligatorio (cookie segura).
- Registrar los callbacks de producción en Google y Facebook: `https://<dominio>/signin-google` y `https://<dominio>/signin-facebook`.
- Correo: ver «Mantenimiento 2026-11-17 — Correo con Resend».

### Fase 2.1 / 2.2 — PeyClient y cuenta bancaria
- `Pey__ApiKey` va en Key Vault. La API key es una sola para todos los vendedores.

### Fase 2.3 — Webhook y sondeo
- Registrar `https://<dominio>/api/webhooks/pey` y su secreto en portal.mipey.mx (un webhook por API key).
- Hangfire usa la misma base (esquema `HangFire`). Con varias instancias, Hangfire reparte los jobs por la base sin duplicarlos.
- **Esquema `HangFire`**: no es una migración de EF. Lo crea o actualiza Hangfire **al arrancar el sitio** (`PrepareSchemaIfNecessary`, por defecto `true`), siempre que `Jobs__Enabled` no sea `false`. Toma un candado (`sp_getapplock`), así que varias instancias que arrancan a la vez no chocan. En el Log Stream aparece `Start installing Hangfire SQL objects...` → `Hangfire SQL objects installed.`
  - El usuario de `ConnectionStrings__Default` necesita permisos de DDL (`db_ddladmin`, además de `db_datareader`/`db_datawriter`). Sin ellos el sitio **no arranca**.
  - Si el usuario de la app solo lee y escribe, hay que correr a mano, con un usuario admin, el script `Install.sql` de la versión de `Hangfire.SqlServer` que se despliega. Es un recurso incrustado en `Hangfire.SqlServer.dll` (`Hangfire.SqlServer.Install.sql`): se reemplaza `$(HangFireSchema)` por `HangFire` y se corre de una vez (es un solo bloque, sin `GO`, y se puede repetir). Con la 1.8.25 queda `HangFire.[Schema]` en la versión 9. **Al actualizar el paquete**, volver a correr el script de la versión nueva antes de desplegar.
  - Verificar: `SELECT * FROM HangFire.[Schema];` en la base. En SSMS o en el Query editor, las tablas salen en *Tables* como `HangFire.Job`, `HangFire.Server`… (dar *Refresh* si ya estaba conectado).

### Fase 2.4 – 2.6 — Cobro, tiempo real, cobro por celular
- SignalR (`/hubs/ventas`): activar **WebSockets** en App Service. Con más de una instancia, usar ARR affinity o Azure SignalR Service.
- La zona `America/Mexico_City` debe resolverse en el App Service (jobs, historial, resumen del día). En Linux es nativa. En Windows depende de ICU: verificar.
- Forwarded headers: ver `ForwardedHeaders__…` arriba.

### Fase 3.1 / 3.2 — Productos y carrito
- Fotos: el disco local de App Service no se comparte entre instancias. Implementar `IFileStorage` sobre Blob Storage antes de escalar (Fase 7).
- SkiaSharp necesita sus binarios nativos (paquete `SkiaSharp.NativeAssets.Linux` en Linux): verificar en el plan elegido.

### Fase 2.8 — Historial de ventas y recibo compartible
- **Migración `SaleReceiptToken`**: agrega `Sales.ReceiptToken` y rellena las ventas existentes con `CRYPT_GEN_RANDOM` (disponible en Azure SQL) antes de volver la columna obligatoria. Aplicarla con el resto de migraciones; en una base con muchas ventas el `UPDATE` tarda proporcionalmente.
- **`App__PublicBaseUrl`**: las ligas de recibo se comparten por WhatsApp y deben apuntar al dominio público y estable (dominio propio con certificado). Sin esta llave se arma la URL con el host de la petición, que en App Service puede ser `*.azurewebsites.net` (o un host interno detrás de un proxy, porque `X-Forwarded-Host` no se procesa). Una liga ya enviada deja de servir si cambia el dominio.
- Recibo `/recibo/{token}`: es anónimo. Responde con `X-Robots-Tag: noindex` y `Cache-Control: private, no-store`; si hay CDN o Front Door delante, no cachear `/recibo/*`.
- Sin recursos nuevos ni secretos nuevos.

### Fase 2.9 — Conciliación de pagos
- **Migración `SalePaymentCheck`**: agrega la columna nullable `Sales.PaymentCheckedAt`, sin relleno. Se aplica con el resto de migraciones.
- **Job `conciliar-pagos`** (Hangfire, cada minuto, en el mismo proceso): se programa solo al arrancar si `Jobs__Enabled` no es `false`. Sin ventas por consultar solo hace una consulta ligera a la base (usa el índice `(Status, ExpiresAt)`), así que cabe en el tier de la base (ver «Mantenimiento 2026-11-18»).
- **Llamadas a Pey**: unas 4 por venta que no se paga (a los 2, 5 y 8 min y una final 10 min después de vencer), y un máximo de 50 ventas por corrida. Las ventas pagadas por webhook no generan llamadas. Los límites de peticiones de Pey siguen por confirmar con soporte.
- Usa la misma `Pey__ApiKey`: sin llaves de configuración ni secretos nuevos. Sin la API key, el job registra un warning por venta y no sella la consulta.
- Si el sitio se detuvo, al volver el job alcanza a hacer la consulta final de las ventas que vencieron en las últimas 24 h.

### Mantenimiento 2026-11-16 — Migración a .NET 10
- **Antes del próximo despliegue**, cambiar la pila del App Service (Linux) a **.NET 10**: Portal → *Configuración* → *Pila* `.NET 10 (LTS)`, o `az webapp config set -g local133 -n local133 --linux-fx-version "DOTNETCORE|10.0"`. Un paquete `net10.0` sobre la pila .NET 9 no arranca.
- El perfil de publicación (`ZipDeploy`, `linux-x64`, dependiente del framework) no cambia. El publish local `dotnet publish src/Local133.Web -c Release -r linux-x64` incluye `libSkiaSharp.so` y `site.css`, y su `runtimeconfig` pide `Microsoft.AspNetCore.App` 10.0.
- No hay migraciones nuevas (EF Core 10 no detecta cambios de modelo), ni llaves de configuración o secretos nuevos. `ForwardedHeaders__KnownNetworks__*` sigue igual (en código ahora se carga en `KnownIPNetworks`).
- Si el pipeline usa `dotnet ef` o un *migration bundle*, usar las herramientas 10.0.x (`.config/dotnet-tools.json`) y el SDK de `global.json` (10.0.401).

### Ajustes de UI 2026-11-17 — Portada, header y pestañas de cobro
- **`Home__RotatingWords__0`, `Home__RotatingWords__1`, …** (opcional): las palabras que rotan en el título de la portada. Por defecto vienen en `appsettings.json` (`local, puesto, tienda, kiosko, negocio, consultorio`). No son secretos: van en App Settings, sin Key Vault. Se sobrescriben **por índice**; para acortar la lista, poner en vacío los índices sobrantes (las vacías se ignoran). Máximo 10 palabras de 15 caracteres.
- El resto (footer, header, placeholder, pestañas de `/app/cobrar`) es solo vista y estilos: sin recursos, migraciones ni secretos nuevos.

### Mantenimiento 2026-11-17 — Correo con Resend
- **Sin recursos de Azure nuevos**: el correo sale por la API de Resend (`https://api.resend.com`, HTTPS saliente desde el App Service; no usa SMTP).
- **Dominio de envío**: `notificaciones.local133.com.mx`, ya verificado en Resend. Su DNS está en Akky (`ns*.akkyservicios.mx`): DKIM `resend._domainkey.notificaciones`, return path `send.notificaciones` (CNAME con MX y SPF) y DMARC `p=none` en `local133.com.mx`. No borrar esos registros. Después del piloto, considerar endurecer DMARC (`p=quarantine`) con un `rua` para recibir los reportes.
- **Llave**: una de producción, de **solo envío** y restringida a `notificaciones.local133.com.mx`, en Key Vault (`Resend__ApiKey`). No reutilizar la de desarrollo ni una de acceso total.
- **`Email__From`**: no hace falta, el valor de `appsettings.json` ya usa el dominio.
- Las ligas de los correos de Identity se arman con el host de la petición: con dominio propio y `ForwardedHeaders__…` correctos salen con `https://<dominio>`.
- Sin migraciones ni jobs nuevos.

### Mantenimiento 2026-11-18 — Tier de Azure SQL
- **Medición** (24 h, sitio sin tráfico, Hangfire y jobs activos, plan Basic de 5 DTU): ~2.25 % de DTU en promedio, 3–4 % de máximo por hora y 1 % de los 2 GB. Picos de 27–80 % que duraron un minuto, por consultas manuales o reinicios.
- **Pruebas: Basic** (5 DTU, 2 GB). Sobra margen.
- **Piloto con vendedores reales: S0** (Standard, 10 DTU, 250 GB): más margen para el historial de ventas, el webhook y varios vendedores cobrando a la vez, y quita el límite de 2 GB (ventas, `WebhookEvents` e historial de Hangfire). Subir a **S1** (20 DTU) solo si el promedio pasa del 60–70 % por periodos largos.
- **No usar serverless ni la oferta gratuita de Azure SQL**: Hangfire consulta la base sin parar (heartbeat y jobs cada minuto), así que la base nunca se pausa, se paga el mínimo de vCores las 24 h (más caro que S0) y la cuota gratuita se acaba en días.
- Cambio de tier en línea (al final hay un corte de segundos): `az sql db update -g local133 -s local133server -n local133 --edition Standard --service-objective S0`.
- Alerta recomendada en la base: «DTU percentage, promedio > 70 % durante 15 min».
- Un `Execution Timeout Expired` aislado en el heartbeat de Hangfire durante un reinicio del contenedor (con la DTU < 5 %) no es saturación: Hangfire lo reintenta solo. Solo hay que revisarlo si se repite.
- Sin cambios de código, migraciones ni secretos.

### Mantenimiento 2026-11-18 — Migraciones al arrancar
- El sitio aplica las migraciones pendientes de EF Core **al arrancar** (`Web/Hosting/DatabaseMigrationSetup`), antes de sembrar el admin y de programar los jobs. El pipeline ya no necesita `dotnet ef database update` ni un *migration bundle*.
- **`Database__MigrateOnStartup`**: `true` por defecto. Ponerlo en `false` solo si las migraciones se vuelven a aplicar desde el pipeline.
- **Permisos**: el usuario de `ConnectionStrings__Default` necesita DDL (`db_ddladmin`, además de `db_datareader`/`db_datawriter`). Hangfire ya lo exige.
- **Varias instancias**: EF Core toma un candado de migración en SQL Server, así que solo una aplica los cambios y las demás esperan.
- **Migraciones largas**: cada comando espera hasta 5 min. Si una migración rellena una tabla grande, revisar el tiempo máximo de arranque del contenedor (`WEBSITES_CONTAINER_START_TIME_LIMIT`, 230 s por defecto en Linux).
- **Si una migración falla**, el sitio no arranca. El error aparece como `Fatal` («La aplicación terminó de forma inesperada») en el Log Stream. Al arrancar se registra «Aplicando N migraciones pendientes: …» o «Base de datos al día».
- Sin recursos ni secretos nuevos.

### PoC 2026-11-18 — Link de pago de Pey
- Sin cambios para Azure: la prueba de concepto (`/app/link-de-pago`) responde 404 fuera de Development y no tiene liga en el menú.
- Si el link de pago se implementa de verdad, `successUrl`/`backUrl` exigen HTTPS: fijar `App__PublicBaseUrl` con el dominio público. Antes hay que resolver con Pey cómo dirigir el dinero a la cuenta del vendedor, porque el link no acepta `bankAccountId`.

### Fase 5.1 — Retiro del modelo de comisión
- **Migración `RemoveCommission` destructiva**: borra `Merchants.CommissionRateOverride`, `Payments.CommissionRate`, `Payments.CommissionAmount`, `Payments.StatementId` y `SystemSettings.CommissionRate` (con sus check constraints) y pone `SystemSettings.GraceDays` en 5. Se aplica sola al arrancar (ver «Migraciones al arrancar»). Antes de publicar, confirmar que hay un punto de restauración reciente de Azure SQL (la restauración a un punto en el tiempo viene activa en todos los tiers). Solo se pierde la comisión calculada de los pagos de prueba, que nunca se cobró.
- El `Down` de la migración regresa las columnas con valor 0, no con los datos originales: para volver atrás de verdad se restaura la base.
- Sin recursos, App Settings ni secretos nuevos.

### Portada 2026-11-18 — Proyecto `Local133.LandingPage`
- **Sitio aparte**: `dotnet publish src/Local133.LandingPage -c Release -o <carpeta>` a su propio App Service (puede ir en el mismo App Service Plan que el portal, sin costo extra). No usa base, Key Vault, Hangfire ni SignalR.
- **Dominios**: la portada en `local133.com.mx` (y `www`, redirigido a la raíz) y el portal en `app.local133.com.mx`. DNS en Akky: para la raíz, registro A + TXT `asuid` de verificación de App Service; para `www` y `app`, CNAME a `<app>.azurewebsites.net` + TXT `asuid.<sub>`. Certificado administrado gratuito de App Service en cada dominio.
- **Mover el portal a `app.local133.com.mx`** obliga a actualizar: `App__PublicBaseUrl` (ligas de recibos y correos), las URIs de redirección de Google (`https://app.local133.com.mx/signin-google`) y Facebook (`/signin-facebook`), y la URL del webhook en portal.mipey.mx (`https://app.local133.com.mx/api/webhooks/pey`).
- **App Settings de la portada** (no son secretos):

  | Llave | Notas |
  |---|---|
  | `Landing__AppUrl` | Destino del botón «Empieza a cobrar». `appsettings.json` ya trae `https://app.local133.com.mx` |
  | `Home__RotatingWords__0`… | Opcional: sobrescribe por índice las palabras que rotan (mismas reglas que el portal; `""` acorta la lista) |
  | `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true`: la portada no configura forwarded headers en código; sin esto `UseHttpsRedirection` no ve el `https` del front-end de App Service y redirige en ciclo |
- Activar «HTTPS Only» en el App Service de la portada.

### Mantenimiento 2026-11-18 — Correo de cuenta activa y layout de correos
- **`App__PublicBaseUrl` pasa a ser obligatoria**: el correo «Tu cuenta ya está activa» sale del webhook y del job de sondeo, que no tienen petición HTTP, así que la liga del botón (`/app/cobrar`) y el logo salen de esa llave. Fuera de Development, sin ella el correo **no se manda** (queda un error en el log). Si el portal se muda a `app.local133.com.mx`, cambiarla también por esto.
- **Logo de los correos**: `wwwroot/img/email/local133-email-logo.png` debe servirse por HTTPS, público y sin autenticación (los archivos estáticos ya lo son). Gmail y Outlook lo descargan con su proxy desde `App__PublicBaseUrl`: un dominio interno o `localhost` no les sirve.
- **Migración `MerchantActivationEmail`** (agrega `Merchants.ActivationEmailSentAt`, nula, no destructiva). Se aplica sola al arrancar.
- Sin recursos ni secretos nuevos. El envío sigue por Resend (`Resend__ApiKey`).

### Fase 5.3 — Planes y vigencia
- **Migración `Subscriptions`** (no destructiva). Hace tres cosas:
  - agrega 15 columnas a `SystemSettings`, con los precios, la prueba, los límites del gratuito y los referidos, y llena sus valores por defecto;
  - crea `Subscriptions` y `SubscriptionPeriods`;
  - da una **prueba de 30 días desde el momento en que se aplica** a los vendedores de la base que ya pasaron el onboarding (`Active`, `Suspended` y `Disabled`).

  Se aplica sola al arrancar. En la base de pruebas de Azure, la prueba de esos vendedores cuenta desde el día del despliegue.
- Los precios y parámetros viven en la base (`SystemSettings`), no en App Settings. Para cambiarlos antes de la Fase 6 (admin), se hace un `UPDATE` en la fila 1. Cada cargo guardará su propio monto (5.6).
- Sin recursos, App Settings, secretos ni jobs nuevos.
