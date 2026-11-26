# Ajustes 2026-11-26 — Bloque 4: PWA instalable

**Estado:** ✅ terminado 2026-11-26 (punto 12) · [← Plan](../PLAN_DESARROLLO.md) · [Documento de los ajustes](../actualizaciones/ajustes_2026_11_26.md#12-pwa-instalable)

Que `Local133.Web` se pueda instalar como app (Android, iOS y escritorio), con una página «Sin conexión» y **sin guardar datos**: el offline real sigue fuera del MVP.

## Qué se hizo
- **Manifest** `wwwroot/manifest.webmanifest`: `id` y `scope` `/`, `start_url` `/?source=pwa` (`HomeController.Index` lo redirige a `/app` o `/admin/clientes` según la sesión, o muestra la home), `display` `standalone`, `lang` `es-MX`, `theme_color` `#E4007C`, `background_color` `#F40284` e íconos 192, 512 y maskable 512 del bloque 1.
- **Service worker** `wwwroot/sw.js` (raíz, scope `/`):
  - en el `install` precarga solo `/offline.html`, su logo (160 y 320 px) y `favicon.ico` (sin red, el navegador lo pide solo y dejaba un error en la consola), en la caché `l133-offline-v1`. En `activate` borra las cachés `l133-*` viejas y toma el control;
  - en navegaciones `GET` del mismo origen va a la red y, si falla, responde `offline.html`. Los archivos precargados van a la red y, sin red, a la copia;
  - todo lo demás pasa directo a la red. **Nunca** hace `cache.put`, así que `/app`, `/admin`, `/api`, `/hubs`, `/Identity`, `/recibo` y los `POST` nunca se guardan.
- **Página sin conexión** `wwwroot/offline.html`: estática, estilos en línea con el look de la home (fondo rosa, logo con anillo blanco), «Sin conexión», «Revisa tu internet y vuelve a intentar.» y «Reintentar» (`location.reload()`).
- **Dónde va** — bandera de vista `ViewData["Pwa"] = true`, puesta en `_MerchantLayout`, `_AdminLayout`, `Views/Home/Index` y `Areas/Identity/Pages/_ViewStart` (login, registro y «Mi cuenta»):
  - `_Head` agrega `<link rel="manifest">`, `mobile-web-app-capable`, `apple-mobile-web-app-capable`, `apple-mobile-web-app-title` «Local133» y `apple-mobile-web-app-status-bar-style` `default`;
  - `_Scripts` carga `wwwroot/js/pwa.js`.

  `/recibo`, `/tienda` y las páginas de error no la llevan: un cliente que abre un recibo no ve «Instalar Local133» ni registra el service worker.
- **`pwa.js`**: registra `/sw.js` con su ruta fija (sin la huella de `MapStaticAssets`, para que el service worker no cambie de URL en cada versión). Maneja la tarjeta «Instalar app»:
  - Android y escritorio: guarda `beforeinstallprompt`, muestra la tarjeta y al tocarla lanza el diálogo del navegador. Después del diálogo (o con `appinstalled`) se oculta;
  - iOS (iPhone, iPod, iPad también en modo escritorio): la muestra y abre un modal con «Toca Compartir › Agregar a inicio» y el aviso de que hay que iniciar sesión otra vez;
  - ya instalada (`display-mode: standalone` o `navigator.standalone`) o sin soporte (Firefox, Safari de escritorio): oculta.
- **«Más»**: tarjeta `<button class="more-card more-card-install" data-more-install>` dentro de `<li data-pwa-install hidden>`, antes de «QR de tu puesto», y el modal `#pwa-ios-modal` (`data-pwa-ios-modal`). En `Styles/site.scss`, color `install` (azul cielo) en `$more-card-colors` y `button.more-card` con los mismos estados de hover, foco y transición que la liga.

### Diferencias con el documento
- El documento pedía `FileExtensionContentTypeProvider` en `UseStaticFiles`. El sitio usa `MapStaticAssets`, que ya sirve `.webmanifest` como `application/manifest+json` y los `.js`/`.html` con `Cache-Control: no-cache` (revisado en `Local133.Web.staticwebassets.endpoints.json`). No hizo falta código en `Program.cs`.
- No se precarga `site.css`: la página sin conexión trae sus estilos en línea y el service worker no sirve CSS desde caché, así que no se usaría.

## Pruebas
- `Home/PwaTests` (integración, nueva):
  - el manifest responde con su tipo, sus campos y cada ícono (192, 512 y maskable) responde `image/png`;
  - `sw.js` con `no-cache`, que menciona `offline.html` y no tiene `.put(`;
  - `offline.html` responde 200 `text/html`;
  - `/`, el login, `/app`, `/app/mas`, «Mi cuenta» y `/admin/clientes` traen el manifest (con huella, que responde), las metas de iOS y `pwa.js`;
  - el recibo (sin sesión y con la del vendedor) y la tienda pública no traen nada de eso.
- `MoreTests`: nueva `Instalar_app_es_un_boton_oculto_hasta_que_se_pueda_instalar` (botón dentro del `<li hidden>` y el modal de iOS).
- `dotnet test --filter "PwaTests|MoreTests|NavigationTests"`: 35 ✅. Suite completa: 702 unitarias, 61 de la portada y 894 de integración ✅.

## Revisión en navegador (Playwright, Edge)
Sitio en `http://localhost:5231` (localhost cuenta como contexto seguro), con `Pey__ApiKey=` y `Resend__ApiKey=` vacíos y un vendedor de prueba registrado y confirmado con el correo de `App_Data/emails`. Capturas en `.playwright-mcp/pwa-*.png`.
- **Manifest e instalabilidad** (CDP `Page.getAppManifest` y `Page.getInstallabilityErrors` en `/`): sin errores en el manifest y **sin errores de instalabilidad**. El `href` lleva la huella (`/manifest.1yd9jlcpug.webmanifest`).
- **Service worker**: activo con scope `/`; la caché `l133-offline-v1` solo trae `offline.html`, el favicon y los dos logos, también después de navegar por `/app`, `/app/mas`, `/app/ventas` y `/app/cobrar`.
- **Sin conexión** (`context.setOffline(true)`): `/app/ventas` y `/app/cobrar` muestran «Sin conexión» con el logo, a 375, 768 y 1280 px, sin scroll horizontal. Al reconectar, «Reintentar» carga Ventas.
  - **Corregido**: sin red, la consola mostraba `ERR_INTERNET_DISCONNECTED` por `/favicon.ico`, que el navegador pide solo. Se agregó a la precarga y a `offline.html` (`<link rel="icon">`). Después, sin errores.
- **«Más»**: Edge mandó un `beforeinstallprompt` real y la tarjeta «Instalar app» apareció. A 375 px quedó en 2 columnas, junto a «Contraseña y acceso» y a la misma altura (194 px). A 768 y 1280 px, en 3 columnas con alturas parejas por fila. Sin scroll horizontal. El foco con teclado (Tab) muestra el anillo rosa.
  - La consola solo trae el aviso informativo de Edge «Banner not shown: beforeinstallpromptevent.preventDefault() called»: es lo esperado, porque la tarjeta guarda el evento.
  - No se tocó la tarjeta para lanzar el diálogo nativo de instalación (es UI del navegador, no de la página). Queda para la prueba en dispositivo.
- **iOS** (contexto nuevo con UA de iPhone, táctil, misma sesión): la tarjeta aparece sin evento y abre el modal con «Toca Compartir › Agregar a inicio». Sin errores en la consola.
- **Ya instalada** (`matchMedia('(display-mode: standalone)')` forzado con un script de inicio, porque CDP no emula `display-mode`): la tarjeta se queda oculta.
- **Recibo y tienda**: `/recibo/{token}` y `/tienda/{slug}` (también sus 404) no traen `rel="manifest"` ni `pwa.js`; `/` y el login sí. Con datos reales lo cubren `PwaTests`.
- `/admin/clientes` solo cambió en el `<head>` y en los scripts: lo cubre `PwaTests.El_admin_trae_el_manifest`. No se abrió en el navegador porque las credenciales del admin sembrado están en los user-secrets.

## Pendientes
- **Dispositivo real** (no se puede emular): instalar en Android (Chrome) e iOS (Safari); en iOS la app no comparte cookies con Safari (hay que iniciar sesión otra vez); login con Google o Facebook dentro de la app instalada en iOS (puede terminar en Safari sin regresar la sesión); regreso del link de pago de la suscripción (`successUrl`) en standalone. El abono no depende de eso (webhook y conciliación), la pantalla de confirmación sí. Si algo falla, se documenta y se decide aparte.
- Lighthouse / DevTools › Application en escritorio con HTTPS de producción.
