# Portada nueva (Local133.LandingPage)

**Estado:** ✅ hecha el 2026-11-25 · cierra la sección «Planes» de la portada que quedó pendiente en la [5.11](fase-5.11-textos-legales.md) · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [instrucciones](../actualizaciones/landing/landing_2026_11_25.md) · [diseño en Claude Design](https://claude.ai/artifact/1UP4dewTiJmqbyemtA8X4u) (mesas «Landing — escritorio» y «Landing — celular (390 px)»)

## Alcance
Cambiar la portada de `local133.com.mx` (hero centrado con logo, palabra que rota y «Empieza a cobrar») por la landing completa del diseño. El orden es: encabezado, hero, «Hecho para», Cómo funciona, Herramientas, Tu dinero es tuyo, Precios, Invita y gana, Preguntas, cierre y pie. Solo se tocó `Local133.LandingPage` y sus pruebas.

## Decisiones
**Del usuario (2026-11-25)**
- **Logo**: un cuadro rosa redondeado con `local133-invertido.svg` (el toldo blanco), como en el diseño, y no `local133.svg`, que es rosa y no se ve sobre el rosa del cierre. Es copia de `Local133.Web/wwwroot/img/` y está en `SharedAssetsTests`. El cuadro es `#F40284`, el mismo rosa del fondo del SVG.

**Propias**
- **Precios escritos en la vista** (`_Pricing.cshtml`): la portada no tiene base y no puede leer `SystemSettings`. `LandingPageTests.Los_precios_coinciden_con_los_de_SystemSettings` compara cada número con `SystemSettings.Default*`. La prueba también revisa los 5 días de gracia de las preguntas. Por eso `Local133.LandingPage.Tests` ahora referencia `Local133.Domain`. **Si el admin cambia un precio en producción (6.2), la portada no se entera**: hay que cambiar la vista a mano.
- **Cortes**:
  - El diseño corta a 760 y 480 px. Se usaron los de Bootstrap, celular primero.
  - Menú en la barra desde `lg` (a 768 px los seis elementos no caben en una línea).
  - Pasos en fila desde `md`.
  - Herramientas en 1, 2 y 3 columnas.
  - Precios en fila desde `lg` (con `subgrid`, para que los precios queden a la misma altura). Debajo de `lg`, carrusel con tarjetas de `min(75%, 340px)`.
- **Sin JS** se usa `@media (scripting: none)`: el panel del menú queda abierto y el botón ☰ y los puntos se ocultan. No hace falta CSS ni JS en línea. Las pistas de los carruseles se deslizan con el dedo.
- **Contraste**: la cápsula «30 días de Pro gratis» usa tinta al 16 % (unos 6:1) en lugar del blanco al 18 % del diseño (unos 4.1:1 a 14 px). Las notas del hero van sin opacidad.
- **Palabra que rota**: `rotating-word.js` no se tocó; ya anima todos los `[data-rotating-words]`, así que el hero y el cierre van desfasados. En el cierre, la caja de ancho fijo es un `inline-grid` cuyo `::before` muestra `attr(data-longest)` (la palabra más larga, invisible, más el ancho del cursor). Así la palabra larga no queda como texto en el DOM.
- **«Hecho para»**: `MadeForLabels.Plural`. Si termina en vocal (también con acento) agrega `s`; si no, `es`. Lleva mayúscula inicial. No resuelve plurales irregulares («lápiz», «camión»).
- **Botones de registro** en la misma pestaña (sin `target="_blank"`). «Iniciar sesión», Términos y Aviso de privacidad se arman con `HomeController.PortalUrl(AppUrl, ruta)` y nunca llevan el código.
- **Menú**: al elegir un ancla, el panel se cierra **sin transición** antes de saltar. Si se cerraba animado, el destino quedaba 336 px abajo, la altura del panel; se encontró en la revisión en el navegador.

## Qué se hizo
- **Modelo y controlador**:
  - `LandingViewModel` suma `LoginUrl`, `TermsUrl`, `PrivacyUrl`, `MadeFor`, `LongestWord` y `RotatingWordsJson`;
  - `RotatingWordViewModel` es el modelo del parcial `_RotatingWord`;
  - `HomeController.PortalUrl`, más las constantes de las rutas del portal (`/Identity/Account/Login`, `/terminos`, `/aviso-de-privacidad`).
- **Vistas** (`Views/Home/`):
  - `Index` arma los parciales `_Header`, `_Hero`, `_HeroPhone`, `_MadeFor`, `_HowItWorks`, `_Tools`, `_YourMoney`, `_Pricing`, `_Referral`, `_Faq`, `_Closing` y `_Footer`;
  - SVG en línea copiados del diseño, con `aria-hidden`;
  - la maqueta del celular lleva `role="img"` y está en su propio parcial, para cambiarla por el video.
- **Layout**:
  - fuentes Baloo 2 + Nunito Sans, solo en la portada;
  - `meta description` desde `ViewData["Description"]` (el párrafo del hero);
  - sección `Head` opcional.
- **Estilos**:
  - `landing.scss` importa `_custom` → `_landing-variables` (tokens y la tipografía reasignada antes de Bootstrap) → Bootstrap → `_rotating-word` → `_landing-base`, `-header`, `-hero`, `-sections`, `-pricing`, `-closing` y `-motion`;
  - botones con `button-variant` de Bootstrap;
  - sin `!important`.
- **JS**: `wwwroot/js/landing.js` (`defer`):
  - menú con `aria-expanded`, la etiqueta Abrir/Cerrar, Esc que devuelve el foco y cierre al pasar a `lg`;
  - carruseles `[data-carousel]`;
  - pasos con avance cada 5 s, solo si la pista se desplaza, está a la vista y la pestaña está visible. Nunca con «reducir movimiento».
- **Archivos estáticos**: `wwwroot/img/toldo-patron.svg` (se movió de `actualizaciones/landing/`) y `wwwroot/img/local133-invertido.svg` (copia).
- `Error.cshtml` usa `.error-page`.

## Pruebas
`dotnet test tests/Local133.LandingPage.Tests`: **38 en verde**, incluida `SharedAssetsTests` con el logo nuevo.
- **`LandingPageTests`**:
  - orden de las secciones y textos principales;
  - «®» después de cada CoDi (también en `<title>` y `meta`);
  - los 7 botones de registro con y sin `?codigo=` (válido, normalizado e inválido);
  - «Iniciar sesión» y las ligas legales sin código;
  - nada con `target="_blank"`;
  - hero, cierre y «Hecho para» con palabras de configuración propia;
  - con una sola palabra, sin animación;
  - precios contra `SystemSettings.Default*`;
  - no aparecen datos pendientes («Quedan», `[N]`, `hola@notificaciones`, «Contacto»);
  - atributos del menú;
  - archivos servidos.
- **`MadeForLabelsTests`**: plurales.
- `dotnet build` de la solución: 0 advertencias. `Local133.Web` sin cambios de esta tarea.

## Revisión en navegador (Playwright, Edge, 2026-11-25)
Sitio en `http://localhost:5241` (Development, `Sass__Watch=false`). Capturas en `.playwright-mcp/` (`m375-*`, `w768-*`, `w1280-*`).
- **375, 768 y 1280 px**: sin scroll horizontal (`scrollWidth = clientWidth`), consola sin errores ni advertencias, sin 404.
- **Menú ☰** (375 y 768):
  - abre con clic y con Enter;
  - cerrado tiene `visibility: hidden` y el Tab lo salta;
  - abierto, el Tab entra al primer enlace;
  - Esc lo cierra y devuelve el foco al botón;
  - un enlace lo cierra y el destino queda en `top: 0`, después de la corrección.
- **Pasos** (375):
  - avanza a los 5, 10 y 15 s y vuelve al primero;
  - la barra del punto activo se llena;
  - tocar el punto 3 lleva al paso 3;
  - deslizar al 2 actualiza el punto y reinicia la cuenta.
- **Precios** (375):
  - la siguiente tarjeta se asoma;
  - el punto 3 lleva a la anual;
  - la etiqueta «2 meses de regalo» se ve completa;
  - los botones caben en una línea.

  Se corrigió que «al año» se partiera en dos renglones: ahora baja completo.
- **Cierre**: en 12 s de muestras con todas las palabras, incluida «consultorio», la caja de la palabra mide siempre 184.9 px en x = 109.9 y el título 107 px de alto.
- **«Reducir movimiento»**: la palabra no rota (sin `is-typing`), el carrusel no se mueve y no hay barra. Las transiciones del menú, los botones y las preguntas están en `0s`.
- **Sin JS**: el panel del menú se ve abierto (336 px), y el botón ☰ y los puntos están ocultos.
- **1280**:
  - menú en la barra;
  - precios alineados;
  - hover de herramientas (el ícono sube 4 px y crece al 110 %);
  - se corrigió la alineación del © del pie.
- **`/?codigo=ABC234`**: los 7 botones van a `{AppUrl}/r/ABC234` e «Iniciar sesión» a `/Identity/Account/Login` sin código.

## Textos legales en la portada (2026-11-25)
- **Qué se hizo**: Términos y Aviso de privacidad se movieron de `Local133.Web` a la portada.
  - Vistas en `Views/Legal/` y `LegalController`. El texto no cambió, así que la versión sigue en `2026-11-24`.
  - Llevan la tipografía (Baloo 2 + Nunito Sans), el encabezado y el pie de la portada, que pasaron a `Views/Shared` con `SiteChromeViewModel`.
  - Estilos en `_landing-legal.scss`. `section-index.js` se movió a la portada.
  - La portada enlaza `LegalDocuments.cs`.
- **Portal**:
  - el pie del `_Layout` y la casilla `_AcceptLegalCheckbox` van a `PublicSiteOptions.TermsUrl` / `PrivacyNoticeUrl` (`App:LandingUrl`);
  - `/terminos` y `/aviso-de-privacidad` responden 301 a la portada;
  - se quitó `.legal-document` de `site.scss`.
- **Pruebas**:
  - `LegalPagesTests` pasó a `Local133.LandingPage.Tests` y ahora corre en CI. Revisa además el encabezado, el pie, la fuente y las ligas;
  - `LegalLinksTests` (portal): los 301 y las ligas del pie;
  - `MerchantRegistrationTests`: la casilla enlaza a la portada.
- **Revisión en navegador** (Edge, portada en 5242 y portal en 5231 con `App__LandingUrl` apuntando a ella):
  - 375, 768 y 1280 px sin scroll horizontal, consola limpia y todos los recursos en 200;
  - el menú ☰ abre y «Precios» lleva a `/#precios`;
  - el índice salta a «Cookies» y deja el foco en el título;
  - el pie se ve al final;
  - `:5231/terminos` llega a `:5242/terminos`, y la casilla del registro y el pie del portal enlazan a la portada.

## Pendientes
- **Lugares de fundador**: «Quedan [N] lugares» está oculto, porque la portada no tiene ese dato. Necesita una fuente: una API pública del portal o un valor en la configuración.
- **Precios a mano**: si cambian en `SystemSettings` (admin, 6.2), hay que cambiar `_Pricing.cshtml`, las preguntas y la prueba.
- **Contacto**: la columna del pie está fuera hasta tener un correo que reciba y un WhatsApp. `hola@notificaciones…` no recibe correo.
- **Por confirmar con Pey o con el abogado**:
  - «con CoDi® o tarjeta», porque con tarjeta Pey podría cobrarle una comisión extra al cliente; aparece en la nota de precios y en «¿Cómo pago mi plan?»;
  - «Precio congelado si renuevas a tiempo» (Pro anual).
- **Video del hero**: todavía no existe. La maqueta está en `_HeroPhone.cshtml`; se cambia por `<video autoplay muted loop playsinline>` (también hay que respetar «reducir movimiento»).
- Revisar en un celular real (Android e iPhone): carruseles con el dedo, menú y la fuente Baloo 2.
