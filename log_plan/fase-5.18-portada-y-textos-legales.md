# Fase 5.18 — Portada y textos legales

**Estado:** ✅ hecho 2026-11-27, sin publicar: sale junto con la 5.12 a la 5.17 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [diseño](../actualizaciones/planes_basico_pro_2026_11_26.md#518-portada-y-textos-legales)

## Alcance (del plan)
- Precios de Prueba, Básico y Pro en la portada.
- Textos con 15 días y sin gratuito.
- Términos al día y con versión nueva.

**Demostrable:**
- `/#precios` muestra tres tarjetas (Prueba Pro, Básico y Pro), la tabla «Compara los planes» y el fundador por plan.
- `/terminos` y `/aviso-de-privacidad` están en la versión `2026-11-27`, sin plan gratuito ni paquetes de 3 y 6 meses.

## Decisiones (propias)
- **Tarjetas**:
  - las de pago traen el mensual y, debajo, el anual («o $990 al año» y «12 meses por el precio de 10: equivale a $82.50 al mes»). Así no hacen falta tarjetas separadas por duración, ni JS para cambiar entre mensual y anual;
  - Pro es la destacada, con la cinta «Recomendado», como en el checkout.
- **Tabla de funciones**:
  - repite a mano `MyPlanViewModel.FeaturesFor`, porque la portada no referencia Web;
  - los tres «Próximamente» van en un solo renglón para que la tabla no crezca en celular;
  - las celdas largas se acortaron: «Sin límite (uso justo)» y «Cobrado con Local133».
- **Botones**:
  - en las tarjetas de pago, «Empezar con 15 días gratis». «Empezar con 15 días de Pro» no cabía a 375 px por 2 px;
  - en la prueba, «Empezar mi prueba gratis»;
  - el hero y el cierre se quedan en «Crear mi cuenta gratis»: crear la cuenta sí es gratis.
- **Aviso de privacidad**: también sube a `2026-11-27`.
  - El diseño solo pedía revisarlo, pero tenía frases falsas: «paso al plan gratuito», el aviso de «límite de cobros» y «si ya pagaste Pro».
  - Ahora menciona el plan y la versión de los Términos que guarda cada pedido.
  - No hay datos personales nuevos.
- **Términos**:
  - sale «Límites del plan gratuito» y entra «7. Cambio de plan», con la conversión de días y un ejemplo con los precios de lista (20 × 99 ÷ 199, casi 10 días);
  - «Uso justo» ahora aplica a los dos planes;
  - las anclas de las demás secciones no cambian.
- **Portada, «Herramientas» y «Invita y gana»**:
  - «Herramientas» dice qué es de Pro: catálogo y carrito, logo en el recibo y Excel;
  - el premio es de «días de tu plan» (§8 del diseño).
- `SystemSettingsRules.InTerms` no cambió: ya tenía los 15 campos (5.17). Solo cambió su comentario.

## Qué se hizo
- **Portada** (`src/Local133.LandingPage`):
  - `_Header` («Prueba 15 días gratis»), `_Hero`, `_HowItWorks` y `_Closing` con 15 días y la prueba al primer cobro;
  - `_Pricing` reescrita: encabezado, tres tarjetas, puntos del carrusel, tabla «Compara los planes» (`@functions PlanCell`: `null` ⇒ ✓ «Sí», «—» ⇒ «No», con `visually-hidden`), fundador con los 4 precios y la nota sin 3 ni 6 meses;
  - `_Faq` con 8 preguntas, una de ellas nueva: «¿Qué diferencia hay entre Básico y Pro?». Las demás ya hablan de vencer sin cobros, de la gracia después de un periodo pagado, de 1 mes o anual y de la conversión al subir;
  - textos de `_Referral` y `_Tools`;
  - `Styles/_landing-pricing.scss`: `.plan-annual` y `.plan-compare*`, celular primero, con un contenedor que se desplaza solo si algo no cabe.
- **Legales**: `Terms.cshtml` (16 secciones) y `PrivacyNotice.cshtml`.
- **Dominio**: `LegalDocuments.CurrentTermsVersion` y `CurrentPrivacyNoticeVersion` pasan a `2026-11-27`, con un `remarks` que explica cada versión.
- **Documentación**:
  - `docs/claude/portada.md` (precios y prueba) y `docs/claude/identity.md` (versión);
  - `azure.md` (Fase 5.18);
  - `PLAN_DESARROLLO.md` y el diseño.

## Pruebas
- **Portada** (65, en verde):
  - `LandingPageTests`:
    - sin las constantes `Published*`;
    - precios de Básico y Pro, «12 meses por el precio de {anual ÷ mensual}», que exige una división entera;
    - fundador con los 4 precios, la prueba con `DefaultTrialDays`, «días de tu plan», 8 preguntas y 11 `data-reveal`;
    - nuevas: `Ya_no_menciona_el_gratuito_ni_los_paquetes_retirados` y `La_tabla_de_planes_compara_Basico_con_Pro`;
  - `LegalPagesTests`:
    - los 4 precios de Básico, «15 días de prueba», «empieza con tu primer cobro» y «10 días del plan que tenga»;
    - nuevas: los Términos sin gratuito ni 3 o 6 meses, y con «Cambio de plan» y el vencimiento sin cobros; el Aviso sin gratuito y con la versión de los Términos del pedido.
- **Unitarias**: 792, en verde.
- **Integración**: 950, en verde. `MerchantRegistrationTests` y `MyPlanTests` usan la constante de la versión.
- `dotnet build` de la solución: 0 advertencias.
- Unitarias e integración corrieron antes de los últimos ajustes visuales, que solo tocaron la portada. Después de esos ajustes se volvieron a correr las pruebas de la portada.
- Sin migración.

## Revisión en navegador
- **Montaje**: la portada corrió desde Bash en `http://localhost:5241` (Development). La portada no llama a Pey ni manda correos. Al terminar se detuvo el sitio.
- **Lo revisado** a 375, 768 y 1280 px:
  - `/#precios`:
    - carrusel en celular y tablet;
    - en 1280, tarjetas en fila con la subgrid alineada (bloque de precio y botón a la misma altura);
    - tabla y fundador;
  - `/terminos`: las 16 anclas del índice existen, incluida `#cambio-de-plan`; versión `2026-11-27`;
  - `/aviso-de-privacidad`: sin scroll horizontal a 375.
- **Corregido durante la revisión**:
  - a 375 px el texto del botón de las tarjetas de pago se salía 2 px, y la tabla medía 297 px en una caja de 280 px;
  - se cambió el texto del botón, se compactaron el relleno y la letra de la tabla en celular, y se acortaron las celdas largas.
  - Después, la tabla mide 292 px en su caja de 292 px, y ninguna celda ni botón se desborda.
- **Comprobaciones**: sin scroll horizontal en las tres páginas, consola sin errores ni advertencias, y sin recursos con error.
- **Capturas** en `.playwright-mcp/518-*.png` (fuera de git).

## Pendientes
- **Revisión legal** de los Términos y el Aviso `2026-11-27`, sobre todo:
  - el cambio de plan con conversión de días;
  - la aceptación en cada pedido;
  - los vendedores que aceptaron la `2026-11-24` (§13 del diseño):
    - ¿basta aceptar al pagar o hay que pedir reaceptación?
    - ¿15 días al primer cobro o se les respetan los 30?
- **Con Pey**: confirmar si el pagador con tarjeta paga una comisión encima. Los Términos lo dicen así; el resumen del pedido no lo muestra.
- **Portada**:
  - «Quedan N lugares» de fundador sigue oculto (portada sin base);
  - los precios y la tabla siguen escritos a mano. Si se cambian desde `/admin/parametros` o cambian las funciones, hay que editar `_Pricing.cshtml` y los Términos, y publicar la portada.
