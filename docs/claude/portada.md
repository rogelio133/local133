# Portada (`Local133.LandingPage`)

Notas de implementación que estaban en `CLAUDE.md` (movidas sin cambios el 2026-11-26). Léelas antes de tocar el sitio de la portada (`src/Local133.LandingPage`) o sus pruebas, y actualízalas aquí si cambias algo de lo que describen.

**Portada (`Local133.LandingPage`)**: landing de una sola página (`/`), hecha el 2026-11-25 con base en el diseño de Claude Design, más los textos legales (`/terminos` y `/aviso-de-privacidad`). Bitácora: `log_plan/portada-nueva.md`.
- Arranca con `dotnet run --project src/Local133.LandingPage` → `http://localhost:5241`.
- **Secciones**: parciales en `Views/Home/`, de `_Hero` a `_Closing`. El encabezado y el pie son `Views/Shared/_Header` y `_Footer` (modelo `SiteChromeViewModel`: `HomeUrl` `""` en la portada y `"/"` en las legales, que llevan las anclas a `/#precios` y el logo a `/`). La portada los recibe con `LandingViewModel.Chrome`.
- **Ligas**:
  - los botones de registro van a `LandingViewModel.AppUrl`: `Landing:AppUrl` (`https://app.local133.com.mx`; en Development `http://localhost:5231`), o `{AppUrl}/r/{código}` con `?codigo=` (`HomeController.SignUpUrl`). Van en la misma pestaña;
  - «Iniciar sesión» usa `HomeController.PortalUrl` y nunca lleva el código;
  - `/terminos` y `/aviso-de-privacidad` son de la portada (`LegalController`, ligas locales) y nunca llevan el código.
- **Palabras**: `Home:RotatingWords` alimenta el hero, el cierre (caja de ancho fijo con `data-longest`) y «Hecho para» (`MadeForLabels.Plural`).
- **Precios** (5.18):
  - escritos a mano en `_Pricing.cshtml`, porque la portada no tiene base. `LandingPageTests` los compara con `SystemSettings.Default*` (por eso las pruebas referencian `Local133.Domain`);
  - tres tarjetas: «Prueba Pro» ($0 por 15 días), Básico y Pro (destacada, «Recomendado»). Las de pago muestran el mensual, «o $X al año» (`.plan-annual`) y «12 meses por el precio de 10: equivale a $X al mes»;
  - debajo, la tabla «Compara los planes» (`.plan-compare`). Repite a mano `MyPlanViewModel.FeaturesFor` del portal: si cambia una función, se cambian las dos. En la vista, `null` es ✓ y «—» es «No», con texto `visually-hidden`;
  - la franja de fundador da los 4 precios (Básico y Pro) y la nota ya no menciona 3 ni 6 meses;
  - `LandingPageTests.Ya_no_menciona_el_gratuito_ni_los_paquetes_retirados` falla si vuelve «gratuito», «3 meses», «6 meses» o «30 días»;
  - «Quedan N lugares» de fundador y la columna Contacto están pendientes.
- **Prueba** (5.18): 15 días que empiezan con el primer cobro, en el encabezado («Prueba 15 días gratis»), el hero, «Cómo funciona», precios, preguntas y cierre. Las pruebas usan `SystemSettings.DefaultTrialDays`.
- **Estilos**:
  - fuentes propias de la portada: Baloo 2 + Nunito Sans. `Styles/_landing-variables.scss` las reasigna después de `_custom` y antes de Bootstrap;
  - los componentes son clases propias en `Styles/_landing-*.scss`, celular primero; menú en la barra desde `lg`;
  - sin JS, `@media (scripting: none)` deja abierto el menú y oculta los puntos.
- **JS**: `wwwroot/js/landing.js`, con el menú y los carruseles `[data-carousel]`. Los pasos avanzan cada 5 s, nunca con «reducir movimiento». Las páginas legales cargan `landing.js` (menú) y `section-index.js`.
- **Aparecer al hacer scroll**: `data-reveal` (el elemento) y `data-reveal-stagger` (sus hijos, escalonados de 90 en 90 ms hasta el 6.º), en las secciones de la portada desde «Cómo funciona». El hero, «Hecho para», el encabezado, el pie y las páginas legales no se animan.
  - `landing.js` (`initReveal`, `IntersectionObserver`) pone `reveal-ready` en `<html>` y marca `is-revealed` / `is-revealing`. Lo que ya está en pantalla al cargar se marca antes, sin animar.
  - El estado oculto solo existe con `reveal-ready` y dentro de `@media screen and (prefers-reduced-motion: no-preference)` (`_landing-motion.scss`). Sin JS, con «reducir movimiento» o al imprimir, todo se ve.
  - Es una `animation` con `backwards`, no `transition`, para no pisar los hovers.
- **Compartido con `Local133.Web` sin copiar**:
  - el SCSS: `Styles/landing.scss` importa `../../Local133.Web/Styles/_custom`, el Bootstrap de `Styles/vendor` y `_rotating-word.scss`;
  - `HomeHeroOptions.cs` (`Compile Link`).
- **Copias** en su `wwwroot` (los static web assets no sirven archivos enlazados): `favicon.ico`, `apple-touch-icon.png`, `icon-192.png`, `img/local133-logo-{48,96,144,160,320,480}.png` (los escribe `tools/brand-assets.ps1` en las dos apps) y `js/rotating-word.js`. Al cambiar el original hay que copiarlo también; `SharedAssetsTests` falla si difieren. `.logo-tile img` va al 100 % (el PNG trae su fondo rosa y su margen).
- `wwwroot/css/landing.css` es generado (no se versiona).
- Portada: las palabras que rotan en «Cobra con CoDi desde tu …» son `Home:RotatingWords` (`Web/Hosting/HomeHeroOptions`, `wwwroot/js/rotating-word.js`). Van por defecto en `appsettings.json`; no son secretos. Se sobrescriben por índice (`dotnet user-secrets set "Home:RotatingWords:0" "puesto" --project src/Local133.Web`; en Azure `Home__RotatingWords__0`). Un índice en `""` se ignora (así se acorta la lista). Máximo 10 palabras de 15 caracteres; si no queda ninguna, «local».
