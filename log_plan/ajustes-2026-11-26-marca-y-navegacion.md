# Ajustes 2026-11-26 — Bloque 1: marca y navegación

**Estado:** ✅ hecho 2026-11-26 · [← Plan](../PLAN_DESARROLLO.md) · [Documento de los ajustes](../actualizaciones/ajustes_2026_11_26.md)

Puntos 1 (logo), 6 (texto «Local133»), 4 (ícono y correo de la barra), 3 (redirección del login), 5 (logo y «/» con sesión) y 2 (home sin barra), en ese orden.

## Qué se hizo

### 1. Logo nuevo
- `01.png` se movió a `tools/brand/local133-logo-source.png`. `tools/brand-assets.ps1` (Windows PowerShell 5.1 + System.Drawing, sin npm ni DLL propias) genera todo desde él: `powershell -ExecutionPolicy Bypass -File tools/brand-assets.ps1`.
  - **Aplanado**: cada píxel se lleva a la recta `#F40284` → blanco según su canal verde (rosa ≈ 3, blanco ≈ 253). Así desaparece el ruido del fondo y se conserva el antialias de los bordes.
  - **Salidas**:
    - `img/local133-logo-{48,96,144,160,320,480}.png`;
    - `apple-touch-icon.png` (180 px);
    - `favicon.ico` (16, 32 y 48 px, PNG embebidos escritos a mano);
    - `icon-192.png`, `icon-512.png` e `icon-maskable-512.png` (el logo al 78 %, dentro del círculo seguro de Android);
    - `img/email/local133-email-logo.png` (192 px, esquinas al 22 % en el PNG y transparente fuera).
  - Lo compartido (logos, `favicon.ico`, `apple-touch-icon.png`, `icon-192.png`) se copia solo a la portada.
  - Detalle: `DrawImageUnscaled` respetaba los DPI del original y encogía el dibujo. Se usa `DrawImage` con rectángulo explícito.
- **Se borraron** `local133.svg` e `img/local133-invertido.svg` en las dos apps. `_Head` (y el `_Layout` de la portada) usan `favicon.ico` + `icon-192.png` (`sizes="192x192"`).
- **Barras**: `.navbar-logo` es un cuadro de 2.25rem con esquinas al 22 % y el anillo blanco translúcido de `.logo-tile-lg`, y lleva `srcset` 1×/2×/3×.
- **Portada**: `.logo-tile img` al 100 %, con los PNG en el encabezado, la maqueta del hero (48/96/144) y el cierre (160/320/480).
- **Correo**: `EmailLayout.LogoVersion = "2"` y el logo a 96×96.

### 6. «Local133» junto al logo
- Parcial `Views/Shared/_NavbarBrand.cshtml` con `Web/Models/NavbarBrandViewModel` (`Href`, `NavbarBrandVariant` `Public | Merchant | Admin`, `HideWordmarkOnPhone`), en los tres layouts. El `alt` del logo va vacío porque el texto está escrito.
- `.navbar-wordmark`: Bungee a 1.25rem, en blanco.
- En el admin, «133» en `.navbar-wordmark-number` (`tint-color($rosa-mexicano, 35%)` = `#EE59AA`, ≈ 5.4:1 sobre `$tinta`). `$rosa-mexicano` daba ≈ 3.8:1, y a 1.25rem no cuenta como texto grande. Después va la insignia «Admin».
- **Ajustes de la revisión a 375 px**:
  - barra pública **sin sesión**: con «Entrar» y «Crear cuenta», el texto empujaba el botón a dos renglones. En `xs` se oculta el texto (`HideWordmarkOnPhone`) y el logo lleva `alt="Local133"`. Con sesión siempre se ve;
  - admin: con ☰ y «Salir» no cabía la insignia, así que en `xs` se oculta (`d-none d-sm-inline-block`).

### 4. Ícono y correo de la barra
- `_LoginPartial`:
  - vendedor ⇒ ícono + correo a `/app/mi-informacion` (`data-navbar-profile`, `title="Mi información"`);
  - admin u otro rol ⇒ `<span data-navbar-email>` con el correo, sin ícono ni liga.
- «Mi cuenta» (`bi-person-gear`, a `/Identity/Account/Manage/ChangePassword`) al final del menú del admin. La tarjeta «Contraseña y acceso» del vendedor va en «Más» (bloque 3).
- `Manage/Index` ya ocultaba la liga a «Mi información» a quien no es vendedor: no cambió.
- **Error previo corregido**: no existía `Areas/Identity/Pages/Account/Manage/_ViewStart.cshtml` (desde la 1.2). Las páginas de «Mi cuenta» usaban directo el `_Layout` público, sin el título ni el menú `_ManageNav`. Sin él no había forma de llegar a «Cuentas externas» desde Contraseña, que es la entrada que decidió el documento. Se agregó, y ahora todas las páginas de Manage traen «Mi cuenta» + el menú (Perfil, Correo, Contraseña, Cuentas externas).

### 3. Redirección después del login
- `PostLoginRedirect.ResolveAsync(userManager, user, returnUrl, isLocalUrl)` respeta el `returnUrl` solo si es local **y** del área del rol. `PostLoginRedirect.IsAllowed` es la regla pura.
  - Áreas: vendedor `/app`, admin `/admin`, ambos `/Identity/Account/Manage`.
  - Ignora mayúsculas, query, fragmento y la `~`; `/application` no cuenta.
  - Cualquier otro caso va a `HomeUrlAsync`.
- Se usa en `Login.OnPostAsync`, `ExternalLogin.RedirectAfterLoginAsync` y también en `SignedInAntiforgeryRedirectAttribute` (doble envío del login), que tenía la misma regla vieja.
- Register, ConfirmEmail y RegisterConfirmation no inician sesión con `returnUrl`: no cambian.

### 5. El logo lleva al inicio y «/» con sesión redirige
- `PostLoginRedirect.HomeUrlFor(ClaimsPrincipal)` es el inicio por los roles de la cookie, sin ir a la base. `HomeController.Index` redirige con él a `/app` o `/admin/clientes`. Con sesión pero sin rol se ve la home.
- `_Layout`: el logo apunta a ese mismo inicio (`NavbarBrandViewModel.ForPublic`). Salieron «Mi puesto» y «Admin». La home quedó sin las ramas por rol.

### 2. Home sin barra
- `_Layout` acepta `ViewData["HideNavbar"] = true` y `ViewData["BodyClass"]`.
- `.home-page` (en `Styles/site.scss`):
  - fondo `$primary`, texto blanco y `main` centrado en el alto de la ventana;
  - `--bs-secondary-color: #fff`, para que el pie (`text-body-secondary`) sea blanco sin pisar clases.
- El logo `.home-logo` mide 160 px, con radio de 32px y anillo blanco.
- **Botones**: «Crear cuenta» es `.btn-home-signup` (`button-variant` blanco con texto `$rosa-profundo`) y «Entrar» es `btn-outline-light`.
  - Se descartó `btn-light`: con `$light = $rosa-papel` el texto sale oscuro, no rosa.
- El cursor de la palabra que rota usa `currentColor`, así que en blanco no hubo que tocar el parcial compartido.

## Pruebas
- **Nuevas**:
  - `Identity/PostLoginRedirectTests.cs`:
    - `PostLoginRedirectRulesTests` (reglas de `IsAllowed`, sin SQL Server, corren en CI);
    - `PostLoginRedirectTests`: Login del vendedor y del admin con `returnUrl` de su área, de otra área, `/`, `/application`, una URL externa y Manage. También ExternalLogin con el esquema `Fake`, con el `returnUrl` en la action del formulario de confirmación.
  - `Home/NavigationTests.cs`:
    - la home sin sesión (sin barra, con el logo y los dos botones);
    - `/` con vendedor ⇒ `/app` y con admin ⇒ `/admin/clientes`;
    - la barra del vendedor y la del admin, el layout de Identity con sesión y el menú de «Mi cuenta»;
    - que los `href`/`src` del `<head>` respondan 200;
    - que se sirvan los PNG nuevos.
  - **Ojo**: `MapStaticAssets` pone huella en las URL de `~/…` (`/favicon.tp74xsnl14.ico`), así que las pruebas comparan por prefijo.
- **Actualizadas**: `SharedAssetsTests` y `LandingPageTests` (lista de archivos de la portada).
- **Resultados** (`dotnet test` normal, sin `--artifacts-path`, con el código final después de la revisión): unitarias 675/675, portada 61/61 e integración 856/856.

## Revisión en navegador (Playwright, Edge)
Sitio en `http://localhost:5231` (`Jobs__Enabled=false`, `Pey__ApiKey=` y `Resend__ApiKey=` vacíos) y portada en `http://localhost:5241`.
- **Home** sin sesión en 375, 768 y 1280 px: sin barra, sobre rosa, con el logo y su anillo, botones legibles y la palabra que rota sin mover el layout. Sin scroll horizontal y la consola limpia.
- **Registro** (barra pública sin sesión) a 375 px: primero «Crear cuenta» salía en dos renglones; se corrigió (ver punto 6).
- **Correo** de confirmación: el encabezado con el logo nuevo (96 px, esquinas redondeadas) a 375 px.
- **Login del vendedor** con `ReturnUrl=/admin/tablero` ⇒ `/app`.
  - Barra del vendedor a 375 px (logo + texto, ícono + «Salir») y a 768 px (pestañas + correo).
  - Contraseña en Identity a 1280 y 375 px, con el menú de «Mi cuenta».
- **Login del admin** con `ReturnUrl=/app/cobrar` ⇒ `/admin/clientes`.
  - Barra a 375 px: primero «Salir» bajaba de renglón; se corrigió (ver punto 6) y queda en una línea de 61 px, sin scroll horizontal.
  - A 768 y 1280 px, la barra con el correo sin liga. El menú del celular trae «Mi cuenta».
- **Portada** a 375, 768 y 1280 px: el encabezado, la maqueta del hero y el cierre con los PNG nuevos (se cargan los tres, sin 404) y la consola limpia.
- Usuarios de prueba que quedaron en la base de desarrollo:
  - `ajustes1126@prueba.mx` (vendedor);
  - `admin-ajustes1126@prueba.mx`: se registró como vendedor y por SQL se le cambió el rol a `Admin`. Conserva su fila en `Merchants`, así que aparece en Clientes del admin.

## Pendientes
- Ver el correo con el logo nuevo en buzones reales (Gmail, Outlook, modo oscuro). Desde `localhost` el logo no carga en un buzón; requiere túnel o producción.
- A 36 px el logo (raster) se ve un poco suave. Si se consigue un SVG, se regeneran los archivos desde él con el mismo script (cambiando la fuente).
