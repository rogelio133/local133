# Fase 5.11 — Precios y textos legales

**Estado:** ✅ textos legales hechos el 2026-11-24 · sección «Planes» de la portada **pendiente** · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [diseño](../actualizaciones/suscripciones_2026_11_18.md#511-precios-y-textos-legales)

## Alcance (del plan)
- **Portada**: planes, con «Próximamente» y lugares de fundador.
- **Términos**: uso justo, referidos, fundador y cancelación.
- **Aviso de privacidad** y revisión legal.

**Demostrable:** `/terminos` y `/aviso-de-privacidad` (versión `2026-11-24`) explican el modelo de suscripción completo con sus números, y un vendedor nuevo acepta esa versión al registrarse.

## Decisiones
**Del usuario (2026-11-24)**
- **La sección «Planes» de la portada queda pendiente.** La portada (`Local133.LandingPage`) no tiene base, así que no puede leer los precios ni contar los lugares de fundador. Las opciones son una API pública del portal, valores fijos en la portada o una página de planes en el portal. No se tocó la portada.
- **Solo los registros nuevos** aceptan la versión nueva. A los vendedores existentes no se les pide nada, porque todavía no hay piloto (§11.8 del diseño).
- **Conservación**: si una cuenta pasa **2 años sin actividad** (sin entrar ni cobrar), se avisa por correo y se elimina, salvo lo que la ley obligue a conservar (§11.7). Solo está en el texto: no hay job que lo haga.
- **Responsable y contacto**: marcadores resaltados (`<mark>`) `[razón social y domicilio]` y `[correo de contacto]`, que se resuelven en la revisión legal. `hola@notificaciones.local133.com.mx` no recibe correo (no tiene MX).

**Propias** (con los valores por defecto de §11 del diseño)
- **Reembolsos**: no se reembolsan periodos pagados, ni completos ni en parte, y se puede dejar de pagar cuando sea. Se aclara que no hay cargos automáticos (§11.6).
- **Fundador** (§11.1):
  - los primeros 100 **cuyo pago se confirme**;
  - de por vida, aunque dejen de pagar un tiempo, mientras la cuenta no se dé de baja, y no es transferible;
  - si al confirmarse el pago ya no hay lugar, se abona igual y los pagos siguientes son a precio normal. Así se porta `SubscriptionPaymentRecorder` desde la 5.6.
- **Los números van escritos** en el texto: es un documento versionado y no debe cambiar solo. `LegalPagesTests` compara cada número con `SystemSettings.Default*`. Si el admin cambia un parámetro (6.2), hay que publicar otra versión.
- **Proveedores con nombre** en el aviso: Pey, Resend, Microsoft Azure, Google y Facebook (login) y Google Fonts (recibe la IP al cargar las tipografías).
- **Tarjeta**: «el proveedor puede cobrarte una comisión, que te muestra antes de pagar». La redacción es prudente porque está por confirmar con Pey quién paga el `fee`.
- **CLABE única**: los términos ya la anuncian, aunque la regla en el código llega con la 5.2.
- **Resumen mensual**: va como finalidad primaria, porque informa de los cobros del vendedor, con la opción de pedir que no se mande escribiendo al contacto. No hay casilla en el sitio. La revisión legal decide si es finalidad secundaria.
- Las dos páginas tienen un **índice con anclas**. Como la barra del sitio es fija (`sticky-top`), `.legal-document h2` lleva `scroll-margin-top: 5rem` (al final de `Styles/site.scss`).

## Qué se hizo
- **`Views/Home/Terms.cshtml`**: 16 secciones con índice:
  - servicio y registro, que dice que la CLABE solo puede estar en una cuenta y que no se permiten varias cuentas;
  - planes, con lo «Próximamente» fuera de lo que se paga;
  - prueba de 30 días;
  - límites del gratuito: 100 concretados, 300 generados, historial de 30 días y catálogo de solo lectura;
  - uso justo de 5,000;
  - precios y pago:
    - $199 al mes y $1,990 al año con IVA, paquetes y pago por adelantado con link;
    - sin cargos automáticos;
    - el pago cuenta con la confirmación del proveedor;
    - un link viejo o un pago doble se abonan;
    - un pago incompleto queda en revisión;
    - la factura se pide aparte;
  - 5 días de gracia y baja al gratuito;
  - fundador a $149 y $1,490;
  - anual congelado;
  - «Invita y gana»: cookie de 30 días, 30 %, 10 días, tope de 12 y premio pendiente;
  - catálogo en línea y recibos, con los datos que quedan públicos;
  - cancelación, reembolsos y baja;
  - uso permitido y suspensión: la falta de pago no suspende;
  - responsabilidad;
  - cambios y contacto.
- **`Views/Home/PrivacyNotice.cshtml`**: 9 secciones con índice:
  - responsable;
  - datos que recabamos: negocio, financieros, operación, celular de los clientes del vendedor, pagos de la suscripción (los datos de la tarjeta no llegan a Local133), referidos, avisos, Google y Facebook, y datos técnicos;
  - finalidades: suscripción, referidos (a quien invitó solo se le muestra el primer nombre), avisos y resumen mensual, catálogo en línea y prevención de abuso;
  - datos que se vuelven públicos: catálogo y recibo;
  - con quién se comparten;
  - cookies: sesión, antiforgery y `l133_ref` por 30 días, y el carrito en `sessionStorage`; ninguna de publicidad ni analítica;
  - conservación de 2 años;
  - derechos ARCO;
  - cambios.
- **`Domain/Legal/LegalDocuments`**: las dos versiones pasan a `2026-11-24`. `AccountRegistration` ya guarda la vigente en cada alta, y los usuarios existentes conservan la suya.
- **Pruebas**: `tests/Local133.IntegrationTests/Legal/LegalPagesTests.cs` (nuevo). La teoría de páginas públicas se pasó ahí desde `MerchantRegistrationTests`.

## Pruebas
- **`LegalPagesTests`** (`Category=SqlServer`):
  - las dos páginas son públicas y muestran su versión y el aviso de borrador;
  - los términos traen las frases con los números de `SystemSettings.Default*` (precios, fundador, prueba, gracia, límites, uso justo, premios y descuento) y los días de `ReferralCookie.Lifetime`;
  - el aviso nombra `ReferralCookie.Name` con sus días, la conservación de 2 años, «solo tu primer nombre» y el resumen mensual;
  - cada liga del índice tiene su `id`;
  - los marcadores del responsable siguen presentes. Esta prueba debe cambiar cuando se pongan los datos reales.
- **`MerchantRegistrationTests`** sigue verificando que el alta guarda la versión vigente.
- **Resultados**: `dotnet build` sin advertencias; las 25 pruebas de `LegalPagesTests` y `MerchantRegistrationTests` en verde. Suite completa con `dotnet test`: 636 unitarias, 17 de la portada y 747 de integración, todas en verde.

## Revisión en navegador
Playwright (Edge), con el sitio en `http://localhost:5231` (`Jobs__Enabled=false`, y `Pey__ApiKey=` y `Resend__ApiKey=` vacíos), sin sesión:
- `/terminos` y `/aviso-de-privacidad` a 375, 768 y 1280 px:
  - sin scroll horizontal;
  - las ligas del índice («Precio de fundador», «Cookies») dejan el título debajo de la barra fija (título a 80 px, barra hasta 58 px);
  - los cuatro marcadores se ven resaltados;
  - el pie trae las dos ligas.
- Register: la casilla sigue abriendo `/terminos` y `/aviso-de-privacidad` en pestaña nueva.
- Consola sin errores ni advertencias y ninguna respuesta 4xx o 5xx.
- Capturas en `.playwright-mcp/5.11-*.png` (fuera de git).

**Corrección (2026-11-24, reportada por el usuario):** en Chrome y Edge de escritorio, al hacer clic en el índice la página bajaba a la sección y regresaba sola al índice. No se reprodujo en el Edge automatizado, donde el clic no deja el foco en la liga. La causa probable: el clic real deja el foco en la liga del índice, y al terminar el desplazamiento suave el navegador vuelve al elemento con foco. Con `wwwroot/js/section-index.js` (para `nav[data-section-index]`), el foco pasa al título de la sección (`tabindex="-1"`, `preventScroll`) antes del salto, que sigue haciendo el navegador. Así se ve:
- el foco queda en el título, y mostrar el elemento con foco ya no mueve la página;
- con el ratón no se dibuja contorno (`:focus-visible` falso);
- consola sin errores.

`LegalPagesTests` revisa que las dos páginas tengan el atributo y el script. **Falta que el usuario lo confirme en su Chrome o Edge.**

## Pendientes
- **Sección «Planes» de la portada** (pospuesta por el usuario): tabla de §2, «Prueba todo gratis 30 días», lugares de fundador y lo «Próximamente». Falta decidir de dónde saca los datos.
- **Revisión legal** de los dos textos, que quita el aviso de borrador; **razón social, domicilio y correo de contacto**, que reemplazan los marcadores (y cambian `LegalPagesTests`); un buzón que reciba correo.
- **Eliminar cuentas tras 2 años sin actividad**: el aviso lo promete y no hay job. Falta definir qué es «actividad» en datos (último login y último cobro), el aviso previo y qué se conserva por ley.
- **6.2 (parámetros globales)**: un cambio de precio, límite, prueba, gracia, uso justo o premio que aparezca en los términos exige publicar una versión nueva (texto + `LegalDocuments` + `LegalPagesTests`). Conviene que la pantalla lo advierta.
- **Reaceptación**: si antes del piloto hay usuarios reales con la versión `2026-11-18`, decidir si se les pide aceptar la nueva.
- Confirmar con Pey la comisión con tarjeta (ajustar la sección 7 de los términos y «Mi plan»).
