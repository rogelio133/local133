# Ajustes 2026-11-26 — Bloque 5: menú del admin

**Estado:** ✅ terminado 2026-11-26 (punto 13) · [← Plan](../PLAN_DESARROLLO.md) · [Documento de los ajustes](../actualizaciones/ajustes_2026_11_26.md#13-menú-del-admin)

Menú lateral del admin de ancho fijo, con el ícono de cada opción en su propia columna (una etiqueta larga se parte sin pasar debajo del ícono) y que en escritorio se puede ocultar a un riel de íconos.

## Qué se hizo
- **`_AdminLayout`**:
  - cada liga lleva `admin-nav-link`, `data-admin-nav-link` y `title`, con `<i class="… admin-nav-icon">` y `<span class="admin-nav-label">`. Se quitó el `me-2`;
  - botón `data-admin-menu-toggle` en la barra (`bi-layout-sidebar`, `d-none d-lg-inline-flex`, 40×40, con `aria-controls="admin-menu"`, `aria-expanded` y «Ocultar menú» / «Mostrar menú»). El ☰ del offcanvas sigue en <lg;
  - script en línea en el `<head>` que lee `l133-admin-menu` (con `try/catch`) y pone `admin-menu-collapsed` en `<html>` antes de pintar;
  - carga `wwwroot/js/admin-menu.js` después de `_Scripts`, porque usa `bootstrap.Tooltip`.
- **`Styles/site.scss`**:
  - `.admin-nav-link` es una rejilla `1.5rem | 1fr` con `column-gap: .75rem` y `align-items: start`, y `.admin-nav-icon` va centrado en su celda;
  - en ≥lg: `.admin-sidebar` mide 15rem en `width`, `min-width` y `max-width`, con `overflow-x: hidden`. Con `.admin-menu-collapsed` mide 4rem: la liga es de una columna centrada y la etiqueta se oculta con el mixin `visually-hidden()` (no `@extend`, que no funciona dentro de un `@media`);
  - transición de ancho de .2s solo con `prefers-reduced-motion: no-preference`;
  - `.admin-menu-toggle` cuadrado.
- **`wwwroot/js/admin-menu.js`**:
  - el botón cambia la clase, guarda `collapsed`/`expanded` y sincroniza `aria-expanded`, `aria-label` y `title`;
  - crea un tooltip de Bootstrap por liga (a la derecha, con hover y foco), que solo se habilita con el riel y `min-width: 992px` (escucha el `change` del media query);
  - al cargar solo alinea el botón y los tooltips, sin escribir en el storage.
- «Mi cuenta» ya estaba al final del menú desde el bloque 1 (punto 4).

## Pruebas
- `Admin/AdminMenuTests` (nueva):
  - las 6 ligas en orden con `admin-nav-label`, `admin-nav-icon` y `data-admin-nav-link`, con «Mi cuenta» al final y Clientes activa;
  - el botón con sus atributos `aria-*`, solo en ≥lg;
  - el script del `<head>`, y `admin-menu.js` con huella responde 200;
  - el vendedor no carga nada del menú del admin.
- `CustomersTests`, `DashboardTests`, `SettingsTests`, `TransactionsTests` y `WebhookLogTests` revisaban la clase de la liga activa (`nav-link active`). Ahora esperan `nav-link admin-nav-link active`.
- Suite completa: 702 unitarias, 61 de la portada y 897 de integración ✅. Se corrió entera; después del cambio de esas 5 pruebas se volvieron a correr las 75 del admin.

## Revisión en navegador (Playwright, Edge)
Sitio en `http://localhost:5231` con `Pey__ApiKey=` y `Resend__ApiKey=` vacíos. El admin fue uno local de prueba, sembrado al arrancar con `Seed__Admin__Email`/`Seed__Admin__Password` por variable de entorno, sin usar el de los user-secrets. Capturas en `.playwright-mcp/admin-menu-*.png`.
- **1280 px, expandido**: menú de 240 px y el botón de 40×40 visible. Las 6 ligas miden 42 px, con el ícono en x=24 y la etiqueta en x=60.
  - Una etiqueta larga inyectada («Transacciones y pagos de suscripciones del mes») se partió en 4 renglones dentro de la columna (borde derecho en 214 de 240 px), con el ícono en el mismo `left` y alineado al primer renglón.
- **1280 px, colapsado**: riel de 64 px con las ligas de 48 px y los íconos centrados (x=32). Clientes sigue marcada como activa.
  - `localStorage` = `collapsed`, `aria-expanded="false"` y «Mostrar menú».
  - Tooltip «Tablero» al pasar el mouse y «Transacciones» con foco.
  - Al navegar a `/admin/tablero`, la página carga ya colapsada (clase puesta desde el `<head>`) con Tablero activo.
  - Al expandir: 240 px, `expanded` y ningún tooltip al pasar el mouse.
- **768 y 375 px**, con `localStorage` en `collapsed`: el botón de colapsar no se ve (`display: none`), ☰ abre el offcanvas con las etiquetas completas y no salen tooltips.
- En los tres tamaños, sin scroll horizontal. La consola quedó sin errores ni avisos en toda la revisión.

### Cambio pedido: el botón en el borde del menú
Para que se entienda mejor, el botón de ocultar salió de la barra superior y quedó como **pestaña en el borde derecho del menú, a media altura**:
- columna `.admin-menu-handle` (ancho 0, `d-none d-lg-block`) entre el menú y el `<main>`;
- botón redondo de 32 px (`bi-chevron-left`, fondo del cuerpo, borde y sombra), centrado sobre el borde con `margin-left: -1rem`;
- `position: sticky; top: calc(50vh + .75rem)`: queda a media altura de la parte visible del menú, debajo de la barra de 61 px, también al desplazar;
- en el riel, la flecha gira 180° (apunta a «mostrar»). El giro tiene transición, salvo con `prefers-reduced-motion: reduce`;
- hover en rosa y foco con el anillo de la marca.

`admin-menu.js` no cambió, porque sigue buscando `data-admin-menu-toggle`. `AdminMenuTests` ahora revisa que el botón esté en `admin-menu-handle`, después del menú, y no en la barra.

Revisión (Playwright, 1280 px):
- expandido: el centro del botón está en x=240, justo en el borde del menú, y en y=428 cuando el centro de lo visible del menú está en 431. Igual arriba, a media página y al fondo de `/admin/clientes`, que es larga;
- colapsado: el botón sigue al borde (x=64) y la flecha queda girada. `aria-expanded`, la etiqueta y `localStorage` cambian;
- a 768 y 375 px la columna no se muestra y ☰ sigue igual. Sin scroll horizontal y con la consola limpia.

Capturas: `.playwright-mcp/admin-menu-handle-1280-{expanded,collapsed}.png`.

## Pendientes
Ninguno.
