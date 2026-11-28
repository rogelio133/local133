# Ajustes 2026-11-27: recibo y catálogo en la portada, y concepto del link de la suscripción

**Estado:** ✅ terminado 2026-11-27 · [← Plan](../PLAN_DESARROLLO.md)

Lo que se pidió:
1. Mover `/recibo/` a `Local133.LandingPage`, sin encabezado ni pie, y con un aviso de que no es un comprobante oficial.
2. Armar el concepto del link de pago de la suscripción como «Local133 plan {plan} {N} mes|meses».
3. Mover `/tienda/{slug}` a `Local133.LandingPage`, sin pie, con una barra rosa arriba y el logo de Local133 al centro, que lleva a la portada.

## Decisiones (confirmadas con el usuario)
- La portada sigue **sin base de datos**. El portal expone una **API pública de solo lectura** (`/api/public`) y la portada la consume por HTTP. El plan, la marca y la visibilidad se deciden en el portal, que también sirve las imágenes.
- Pey **no acepta concepto** en `POST /payment-link/create`; se revisó el OpenAPI y solo admite `amount`, URLs, `hasTip`, `clientReferenceId`, `metadata` e `isMultiplePayments`. Por eso el concepto va en `metadata.concept`.
- La vista previa del catálogo (`/app/tienda/vista-previa`) se queda en el portal **sin cambios**.

## Qué se hizo
- **Concepto**:
  - `SubscriptionCharge.PaymentLinkConcept` / `PaymentLinkConceptFor(plan, meses)` produce «Local133 plan Pro 1 mes» y «Local133 plan Básico 12 meses»;
  - se agregó la llave `SubscriptionChargeMetadata.ConceptKey = "concept"`, que `SubscriptionCheckout.MetadataFor` incluye;
  - la conciliación no la usa.
- **Portal (`Local133.Web`)**:
  - Contratos sin dependencias en `PublicPages/PublicPageContracts.cs`: `PublicReceipt`, `PublicStorefront`, `PublicPagesApi` (rutas e `IsReceiptToken`). La portada los enlaza.
  - `Controllers/PublicPagesApiController` (anónimo):
    - `GET /api/public/recibos/{token}` responde el recibo, o 404 ProblemDetails, con `noindex`, `no-referrer` y `no-store`;
    - `GET /api/public/tiendas/{slug}?q=&pagina=` responde 404 si la liga no existe, 200 con `available: false` y el nombre del negocio si no se ve, y 200 con todo si se ve;
    - las imágenes van con URL absoluta (`PublicSiteOptions.BaseUrlFor`).
  - La lógica sale de `Services/PublicReceiptQuery` (nuevo) y de `StorefrontQuery` (`FindMerchantAsync`, `IsVisibleAsync`, `PublicImages(slug, baseUrl)`, `ToPublic`).
  - `ReceiptsController.Show` y `StorefrontController.Show` responden **301** a la portada; el catálogo conserva el query string. Las rutas de imágenes se quedan en el portal.
  - Se borraron `Views/Receipts/Show.cshtml`, `Views/Storefront/Unavailable.cshtml`, `ReceiptViewModel` y `StorefrontUnavailableViewModel`.
  - `PublicSiteOptions`: `ReceiptUrlFor`, `StorefrontUrlPrefix` y `StorefrontUrlFor`, todas sobre `App:LandingUrl`.
  - `ReceiptLinks.UrlFor(token)` y `ShareFor(token, …)` ya no reciben la petición; `ReceiptShare` ya no tiene `Path`.
  - «Ver recibo» de `_ReceiptShare` abre la URL de la portada, igual que la liga de «Catálogo en línea».
- **Portada (`Local133.LandingPage`)**:
  - `Services/PublicPagesClient`, un `HttpClient` tipado con `Landing:AppUrl` y `AddStandardResilienceHandler`.
  - `ReceiptController` (`/recibo/{token}`):
    - el recibo va sin encabezado ni pie, con el aviso «Este recibo no es un comprobante fiscal ni oficial: solo confirma que el negocio recibió tu pago.» y sin folio;
    - un token con otra forma da 404 sin llamar al portal.
  - `StorefrontController` (`/tienda/{slug}`): el catálogo lleva `_StoreBar` (barra `$landing-logo-pink` con el logo de 48 px centrado que lleva a `/`) y no tiene pie.
  - `PublicMessage.cshtml` cubre los casos sin contenido: recibo inexistente (404), catálogo no disponible (200 o 404) y portal sin respuesta (503).
  - `_Layout` ahora acepta `NoIndex` y `BodyClass`.
  - Estilos en `_landing-public-pages.scss` (copia de las reglas del portal).
  - Copia de la fuente de Bootstrap Icons en `wwwroot/lib/bootstrap-icons`, cubierta por `SharedAssetsTests`.
  - `wwwroot/js/receipt-print.js`.

### Diferencias con lo pedido
- **El folio deja de verse en el recibo público**, aun con sesión. La portada vive en otro dominio y no recibe la cookie del portal. El vendedor lo sigue viendo en su historial.
- **El concepto no es un concepto de Pey, sino metadata.** Falta confirmar si el pagador lo ve; ver Pendientes.

## Pruebas
- Unitarias: `SubscriptionChargeTests.El_concepto_del_link_lleva_el_plan_y_los_meses` (los dos planes, 1 y 12 meses).
- Integración:
  - `MyPlanTests` revisa `metadata["concept"]`;
  - `ReceiptTests`, `BrandedReceiptTests` y `StorefrontTests` se reescribieron contra la API JSON: datos, marca, 404, `available: false`, búsqueda y página, nada interno (CLABE, celular, folio, ids de Pey), URLs absolutas de imágenes y los 301;
  - `PublicPageContractsTests` es nueva: las constantes de los contratos son iguales a las del portal;
  - `ReceiptLinksTests` y `SalesHistoryTests` usan la URL de la portada;
  - se quitaron de `PwaTests` los casos del recibo y la tienda, que ya no son del portal.
- Portada: `PublicPagesTests` es nueva, con un doble del portal, y corre en CI. Cubre:
  - marcado del recibo;
  - «Cobrado con» con `?codigo=`;
  - logo con marca;
  - token inválido sin llamar al portal;
  - 404 y 503;
  - barra rosa con el logo a `/` y sin pie;
  - búsqueda y paginación;
  - catálogo no disponible e inexistente;
  - rutas de la API.
- `dotnet test` completo: 790 unitarias, 77 de la portada y 947 de integración ✅.

## Revisión en navegador (Playwright)
Portal en `http://localhost:5231` (`Pey__ApiKey=`, `Resend__ApiKey=` vacíos, `Jobs__Enabled=false`) y portada en `http://localhost:5241`, con los datos locales del vendedor 4 (Pro, catálogo publicado, venta de carrito pagada). Capturas en `.playwright-mcp/recibo-{375,768,1280}.png`, `tienda-{375,768,1280}.png` y `tienda-no-disponible-375.png`.
- `/recibo/{token}` y `/tienda/{slug}` del portal redirigen (301) a la portada.
- Recibo, revisado en 375, 768 y 1280:
  - sin scroll horizontal, sin `<header>` ni `<footer>`;
  - el aviso de «no oficial» se ve;
  - el logo con marca carga desde el portal;
  - los íconos usan la fuente `bootstrap-icons`;
  - la consola está limpia.
- Catálogo, revisado en 375, 768 y 1280:
  - barra rosa `rgb(244, 2, 132)` con el logo centrado (desfase 0);
  - sin pie y sin scroll horizontal;
  - la foto, el logo y las miniaturas cargan desde el portal (estas, al hacer scroll, porque son `lazy`);
  - la búsqueda «torta» da 2 resultados con «Ver todos»;
  - el logo de la barra lleva a la portada.
- Liga inexistente: «Catálogo no disponible por ahora» con barra y 404. Recibo inexistente: «No encontramos este recibo» con 404. El único error de consola es el propio 404 del documento.
- No se revisó con sesión del vendedor la pantalla de «Catálogo en línea» ni «Ver recibo» (sus ligas cambian de dominio y están cubiertas por `StorefrontTests` y `SalesHistoryTests`), ni la paginación con datos reales (el vendedor tiene 3 productos; la cubre `PublicPagesTests`).

## Protección de `/api/public` (mismo día, a pedido del usuario)
Riesgo: la API es anónima y el catálogo hace varias consultas (búsqueda con `LIKE '%…%'`), así que se podía consultar en masa. Se hicieron los puntos 1 y 2 de lo propuesto; quedan para la Fase 7 el límite de las imágenes del portal, la caché de salida del catálogo y bajar los reintentos.
- **Llave compartida**: `PublicApi:Key` en los dos sitios. La portada la manda en `X-Local133-Key` (`PublicPagesClient`) y el portal la valida con `Web/PublicPages/PublicApiKeyFilter` (`[TypeFilter]` en `PublicPagesApiController`, `CryptographicOperations.FixedTimeEquals`): 401 ProblemDetails si falta o es otra, 500 si el portal no la tiene. CORS, `Referer`/`Origin` y las restricciones por IP de App Service no sirven para esto (llamadas de servidor, encabezados falsificables, restricción de todo el sitio).
- **Límite por IP en la portada** (`Hosting/PublicPagesRateLimit`): ventana deslizante de 60 s en 6 segmentos, 30 recibos y 60 páginas de catálogo por IP, configurable en `RateLimiting:*`; 429 con `Retry-After` sin llamar al portal. La ventana deslizante no informa `RetryAfter`, así que se manda la duración de un segmento.
- En desarrollo se guardó una llave aleatoria en los user-secrets de los dos proyectos.
- Pruebas:
  - integración: `ReceiptTests.Sin_la_llave_de_la_portada_la_API_responde_401_sin_datos` (sin encabezado, vacío, otra y una con un carácter de más) y `Si_el_portal_no_tiene_la_llave_la_API_queda_cerrada`; las pruebas de la API usan `WebTestBase.LandingClient()`;
  - portada: `Manda_la_llave_al_portal`, `Sin_la_llave_el_portal_la_rechaza…` (503), `Al_pasarse_del_limite_por_IP_responde_429_sin_llamar_al_portal` y `El_limite_del_recibo_no_gasta_el_del_catalogo_ni_el_de_la_portada`.
  - `dotnet test`: 790 unitarias, 81 de la portada y 952 de integración ✅.
- Prueba manual con los dos sitios: la API directa sin llave o con una inventada da 401; el recibo y el catálogo de la portada, 200; una ráfaga de recibos da 429 después de 30 en el minuto, con `Retry-After: 10`.

## Pendientes
- Confirmar con Pey si la página del link de pago muestra al pagador `metadata.concept`.
