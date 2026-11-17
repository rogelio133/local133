# Publicación en Azure

Qué hay que considerar y hacer para publicar Local133 en Azure, según lo que agregó cada subfase. Se actualiza al terminar cada subfase. Si una subfase no cambia nada, se anota explícitamente. **Nunca** poner secretos reales aquí: van en App Settings o en Key Vault.

En App Settings los niveles de configuración se separan con `__` (p. ej. `Pey__ApiKey` equivale a `Pey:ApiKey`).

> Este archivo se creó en la Fase 2.8. Lo de las fases 0.1 a 3.2 se reconstruyó a partir de `CLAUDE.md` y de las bitácoras; conviene revisarlo antes del primer despliegue (Fase 7).

## Recursos

| Recurso | Para qué | Desde |
|---|---|---|
| App Service (.NET 10) | Sitio MVC, API `/api`, webhook de Pey, hub SignalR y jobs de Hangfire (en el mismo proceso) | Fase 0.1 / 1.1 |
| Azure SQL Database | Base `Local133` (EF Core) y el esquema `HangFire` (lo crea Hangfire, no EF) | Fase 0.2 / 2.3 |
| Key Vault (recomendado) | API key y secreto del webhook de Pey, secretos OAuth, contraseña del admin sembrado | Fase 1.3 / 1.5 / 2.1 |
| Blob Storage | Fotos de productos. Hoy es `LocalDiskFileStorage`; falta la implementación para blob (Fase 7) | Fase 3.1 |
| Azure SignalR Service (opcional) | Solo si hay más de una instancia | Fase 2.5 |

## Configuración (App Settings)

| Llave | Notas | Desde |
|---|---|---|
| `ConnectionStrings__Default` | Cadena de Azure SQL (de preferencia con identidad administrada) | Fase 0.2 |
| `Authentication__Google__ClientId` / `__ClientSecret` | Opcional: sin ellas no aparece el botón | Fase 1.3 |
| `Authentication__Facebook__AppId` / `__AppSecret` | Opcional: sin ellas no aparece el botón | Fase 1.3 |
| `Seed__Admin__Email` / `Seed__Admin__Password` | Siembra idempotente del admin al arrancar | Fase 1.5 |
| `Pey__ApiKey` | Referencia a Key Vault. Sin ella el sitio arranca, pero fallan las llamadas a Pey | Fase 2.1 |
| `Pey__WebhookSecret` | `whsec_…`. Sin él el webhook responde 500 | Fase 2.3 |
| `Jobs__Enabled` | `true` por defecto (sondeo de cuentas cada 30 min, expiración de ventas cada minuto) | Fase 2.3 / 2.5 |
| `ForwardedHeaders__KnownNetworks__0` / `__1` (o `KnownProxies__0`) | Red o IP del front-end de App Service o del proxy. En App Service: `0.0.0.0/0` y `::/0` (el proceso solo recibe tráfico del front-end de la plataforma; su IP interna no es fija). Sin esto el sitio no confía en `X-Forwarded-Proto`: la redirección a HTTPS puede ciclarse, el webhook recibe 307 y el login de Google/Facebook falla con `redirect_uri_mismatch` (callback con `http://`) | Fase 2.5 (corrección 2026-11-13) |
| `Storage__LocalDirectory` | Mientras no exista el blob: carpeta persistente (`/home/...` en Linux). No sirve con varias instancias | Fase 3.1 |
| `App__PublicBaseUrl` | Dominio público, p. ej. `https://local133.mx`. Ver Fase 2.8 | Fase 2.8 |

## Por subfase

### Fase 0.1 / 0.2 — Solución y base de datos
- Crear la base en Azure SQL y aplicar las migraciones en el pipeline (`dotnet ef database update` o un *migration bundle*), no al arrancar.
- Serilog escribe en consola y en `logs/`: en App Service conviene la consola (Log Stream / Application Insights).

### Fase 1.1 – 1.5 — MVC, Identity, login externo, admin
- HTTPS obligatorio (cookie segura).
- Registrar los callbacks de producción en Google y Facebook: `https://<dominio>/signin-google` y `https://<dominio>/signin-facebook`.
- Correo: en desarrollo se usa `FileEmailSender`. Producción necesita SMTP o un servicio de correo (Notificaciones, pendiente).

### Fase 2.1 / 2.2 — PeyClient y cuenta bancaria
- `Pey__ApiKey` va en Key Vault. La API key es una sola para todos los vendedores.

### Fase 2.3 — Webhook y sondeo
- Registrar `https://<dominio>/api/webhooks/pey` y su secreto en portal.mipey.mx (un webhook por API key).
- Hangfire usa la misma base (esquema `HangFire`). Con varias instancias, Hangfire reparte los jobs por la base sin duplicarlos.

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

### Mantenimiento 2026-11-16 — Migración a .NET 10
- **Antes del próximo despliegue**, cambiar la pila del App Service (Linux) a **.NET 10**: Portal → *Configuración* → *Pila* `.NET 10 (LTS)`, o `az webapp config set -g local133 -n local133 --linux-fx-version "DOTNETCORE|10.0"`. Un paquete `net10.0` sobre la pila .NET 9 no arranca.
- El perfil de publicación (`ZipDeploy`, `linux-x64`, dependiente del framework) no cambia. El publish local `dotnet publish src/Local133.Web -c Release -r linux-x64` incluye `libSkiaSharp.so` y `site.css`, y su `runtimeconfig` pide `Microsoft.AspNetCore.App` 10.0.
- No hay migraciones nuevas (EF Core 10 no detecta cambios de modelo), ni llaves de configuración o secretos nuevos. `ForwardedHeaders__KnownNetworks__*` sigue igual (en código ahora se carga en `KnownIPNetworks`).
- Si el pipeline usa `dotnet ef` o un *migration bundle*, usar las herramientas 10.0.x (`.config/dotnet-tools.json`) y el SDK de `global.json` (10.0.401).

### Ajustes de UI 2026-11-17 — Portada, header y pestañas de cobro
- **`Home__RotatingWords__0`, `Home__RotatingWords__1`, …** (opcional): las palabras que rotan en el título de la portada. Por defecto vienen en `appsettings.json` (`local, puesto, tienda, kiosko, negocio, consultorio`). No son secretos: van en App Settings, sin Key Vault. Se sobrescriben **por índice**; para acortar la lista, poner en vacío los índices sobrantes (las vacías se ignoran). Máximo 10 palabras de 15 caracteres.
- El resto (footer, header, placeholder, pestañas de `/app/cobrar`) es solo vista y estilos: sin recursos, migraciones ni secretos nuevos.
