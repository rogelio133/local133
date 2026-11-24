# Fase 5.10 — Valor visible

**Estado:** ✅ hecho 2026-11-24 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [diseño](../actualizaciones/suscripciones_2026_11_18.md#510-valor-visible)

## Alcance (del plan)
Lo que hace atractivo pagar (§7 del diseño):
- recibo con marca: logo en Pro, y en el gratuito «Cobrado con Local133» con el código del vendedor;
- catálogo público con liga;
- «lo que te pierdes» y resumen mensual.

Depende de la 4.1 (logo y foto del local).

**Demostrable:** un vendedor en Pro publica su catálogo en `/app/tienda` con la liga `tacos-don-ramon`, la comparte por WhatsApp, y quien la abre ve su foto, su logo, sus productos con precio y «Pedir por WhatsApp». Sus recibos llevan su logo. Si baja al gratuito, la liga dice «Catálogo no disponible por ahora». Sus recibos dicen «Cobrado con Local133», con liga a la portada y su código: el botón de la portada lleva al registro con el código ya escrito. El día 1 recibe «En octubre cobraste $18,400 en 212 ventas. Con una terminal habrías pagado unos $747 de comisión…».

## Decisiones
**Del usuario (2026-11-24)**
- La marca del recibo se decide con el **plan al abrir la liga**, no con el plan al cobrar. No hay migración. Si baja al gratuito, sus recibos viejos dicen «Cobrado con Local133», y si vuelve a Pro recuperan el logo.
- «Cobrado con Local133» lleva a la **portada con el código** (`{App:LandingUrl}/?codigo=ABC234`). La portada acepta el código, y «Empieza a cobrar» va a `{AppUrl}/r/ABC234`, que ya existía (cookie de 30 días y registro con el código).
- El resumen mensual se manda **solo a quien tuvo al menos un cobro concretado** en el mes y no está dado de baja. Así no hay correos de «cobraste $0».

**Propias**
- **Catálogo público**
  - Se ve solo si está publicado, el plan lo permite (`CanPublishCatalog`: Pro, prueba, gracia y onboarding) y el vendedor no está suspendido ni dado de baja (`Merchant.CanShowPublicCatalog`). En cualquier otro caso, la liga responde 200 con «Catálogo no disponible por ahora», `noindex` en el encabezado y en la página, y el nombre del negocio. Un slug que no existe da la misma página con 404.
  - Publicado se puede indexar (sin `noindex`). Es la lectura de «`noindex` mientras el vendedor no lo publique».
  - **Slug**:
    - de 3 a 40 caracteres, `^[a-z0-9]+(-[a-z0-9]+)*$`, sin distinguir mayúsculas en la URL;
    - se propone con el nombre del negocio («Tacos Don Ramón» ⇒ `tacos-don-ramon`). El servidor normaliza lo que llegue, y `storefront-slug.js` lo hace al escribir;
    - es único con un índice filtrado. Se revisa antes de guardar, y la violación del índice también da «ya la usa otro negocio»;
    - al dejar de publicar se conserva y queda apartado.
  - Publicar pide el **nombre del negocio**, que es el título del catálogo.
  - «Pedir por WhatsApp» usa el **celular de la cuenta** (el de «Mi información»). La pantalla avisa que quien vea el catálogo puede ver ese número. Sin celular no hay botón.
  - **Imágenes públicas** en `/tienda/{slug}/logo|foto` y `/tienda/{slug}/productos/{id}/foto|miniatura`, con la misma regla de visibilidad y `public, max-age=3600`: si deja de publicar, dejan de servirse pronto. Solo productos de ese vendedor que no estén dados de baja.
  - **Vista previa** (`/app/tienda/vista-previa`) en cualquier plan y sin publicar, con las imágenes por las rutas privadas que ya existían.
  - Pie «Catálogo hecho con Local133» con la liga de la portada y el código del vendedor (también en Pro): cada catálogo puede traer referidos.
  - En el gratuito la pantalla no deja cambiar ni despublicar, porque es solo lectura como el catálogo de productos.
- **Recibo con marca**:
  - el logo se sirve en `/recibo/{token}/logo` solo con recibo y con `HasBrandedReceipt` (nuevo en `PlanEntitlements`), con `private, max-age=86400`;
  - el pie «Cobrado con Local133» va dentro de la tarjeta, también al imprimir.
- **Lo que te pierdes**: casi todo existía desde la 5.5. Se agregó:
  - «ocultas» al aviso del historial;
  - en Cobrar (Monto), en el gratuito con productos, la línea «Tu catálogo de N productos te espera. Con Pro cobras con ellos.»;
  - la fila «Catálogo en línea con liga» y el recibo con marca en la tabla de «Mi plan».
- **Resumen mensual**:
  - Montos y conteos: pagos con `PaidAt` en el mes, en hora de México. Las ventas cuentan una vez aunque tengan dos pagos (el criterio de `DaySummaryQuery`).
  - Comisión estimada = cobrado × `ReferenceTerminalFeeRate` × (1 + `VatRate`), redondeada a pesos. Si da menos de $1, no se menciona.
  - En Pro, prueba o gracia: «Tu plan Pro cuesta $199 al mes» (el precio de fundador si lo es), con botón a Ventas. En el gratuito: «Con Pro, por $199 al mes, cobras con tus productos, ves tu historial completo y tienes tu catálogo en línea», con botón a «Mi plan».
  - `DedupKey` `summary:{yyyy-MM}`; un scope por vendedor.

## Qué se hizo
- **Dominio**:
  - `Merchants/PublicSlug` (`Normalize`, `IsValid`, `Suggest`, `PathFor`, `Pattern`);
  - en `Merchant`: `PublicSlug`, `PublicCatalogEnabled`, `PublishCatalog`, `UnpublishCatalog` y `CanShowPublicCatalog`;
  - `PlanEntitlements.HasBrandedReceipt`;
  - `NotificationType.MonthlySummary`;
  - `Subscriptions/MonthlySummary.For`.
- **Migración `PublicCatalog`** (no destructiva): `Merchants.PublicSlug` (`varchar(40)`, nula, índice único filtrado `IX_Merchants_PublicSlug`) y `PublicCatalogEnabled` (`bit`, 0 por defecto).
- **Application**: `Merchants/PublicCatalog.cs`, con `IPublicCatalogSettings`, sus resultados y `PublicCatalogMessages`.
- **Infrastructure**:
  - `Merchants/PublicCatalogSettings`;
  - `Subscriptions/MonthlySummaryNotifier`, que reemplaza a `LoggingMonthlySummaryNotifier` (borrado). Se registra scoped, también como clase concreta;
  - `MerchantEmails.MonthlySummary`.
- **Web**:
  - `PublicSiteOptions.LandingUrl` (`App:LandingUrl`, por defecto `https://local133.com.mx`; en Development `http://localhost:5241`) y `LandingUrlFor(código)`;
  - `ReceiptsController`: logo, pie y `GET /recibo/{token}/logo`;
  - `StorefrontController` (`/tienda/{slug}` e imágenes), `Services/StorefrontQuery`, `Models/StorefrontViewModels` y las vistas `Storefront/Show` y `Unavailable`;
  - `Areas/Merchant/Controllers/StorefrontSettingsController` (`/app/tienda`, `publicar`, `despublicar`, `vista-previa`), su vista y `StorefrontSettingsViewModels`;
  - `wwwroot/js/storefront-slug.js`;
  - estilos `.receipt-logo` y `.storefront-*`;
  - la entrada «Catálogo en línea» en «Más», la línea de Cobrar, el texto de «ocultas» y la tabla de «Mi plan»;
  - `ProductUrls.Version` pasó a público.
- **Portada**: `HomeController.Index(codigo)` con `ReferralCode.cs` enlazado (`Compile Link`).

## Pruebas
- **Unitarias**:
  - `PublicSlugTests` (normalización, acentos, largo y propuesta);
  - `MerchantPublicCatalogTests` (publicar, despublicar y visibilidad por plan y estatus);
  - `MonthlySummaryTests` (18,400 ⇒ $747, fundador y ajustes);
  - `PlanEntitlementsTests` con `HasBrandedReceipt`.
- **Portada**: código válido, en minúsculas con guion, con letras no permitidas, HTML y vacío.
- **Integración**:
  - `BrandedReceiptTests`: Pro con y sin logo, gratuito con `?codigo=`, y el logo con 404 si la venta no está pagada o el token no es válido;
  - `StorefrontTests`:
    - publicar con la liga propuesta, con WhatsApp y el código en el pie;
    - normalización y slug inválido;
    - slug de otro vendedor y sin nombre del negocio;
    - gratuito (aviso de Pro, sin guardar, y una liga publicada que no muestra nada);
    - dejar de publicar, suspendido, dado de baja, inexistente (404) y mayúsculas;
    - imágenes públicas, también de otro vendedor y dadas de baja;
    - sin celular, vista previa, antiforgery, rol y «Más»;
  - `MonthlySummaryNotifierTests`:
    - bordes del mes en hora de México y venta con dos pagos;
    - texto de Pro y del gratuito;
    - solo vendedores con cobros y no dados de baja;
    - repetir el mes;
    - falla del correo;
  - `FreePlanLimitsTests`: la línea de Cobrar en el gratuito y en la prueba, «ocultas» y la tabla de «Mi plan».
- **Resultados**:
  - `dotnet build` (Debug y Release) sin advertencias;
  - `has-pending-model-changes`: sin cambios;
  - `dotnet test`: 636 unitarias, 17 de la portada y 741 de integración, en verde.

## Revisión en navegador
Playwright (Edge), con el sitio en `http://localhost:5231` (`Jobs__Enabled=false`, `Pey__ApiKey=` y `Resend__ApiKey=` vacíos) y la portada en `http://localhost:5241`. Se registró por la UI el vendedor `tienda510@prueba.mx` (Merchant 45) y se confirmó por SQL. Por SQL también se le pusieron negocio, celular, dirección, 4 productos y una venta pagada con recibo; luego el estado del plan y la cuenta bancaria falsa. El logo y la foto del local se subieron por «Mi información».
- **«Catálogo en línea»** a 375, 768 y 1280 px:
  - liga propuesta;
  - validación en el cliente («ab» da error sin POST; «Ñu Á» se escribe como `nu-a`);
  - publicar, la tarjeta «Publicado» con WhatsApp, Compartir y Copiar, y cambiar la liga;
  - en el gratuito, el aviso de Pro con «Tu catálogo de 4 productos te espera» y la nota de liga no disponible.
- **`/tienda/{slug}`** a 375, 768 y 1280 px: portada, logo, dirección en dos renglones, «Pedir por WhatsApp», búsqueda («quesa» ⇒ 1 resultado) y pie. En el gratuito, «Catálogo no disponible por ahora». También la vista previa.
- **Recibo** a 375 y 1280 px: con el logo en Pro y con «Cobrado con Local133» en el gratuito. La liga lleva a `localhost:5241/?codigo=ZX4XH7`; el botón de la portada lleva a `/r/ZX4XH7`, y de ahí al registro con el código escrito.
- **Cobrar** (Monto, gratuito) con la línea del catálogo, **Ventas** con «Tienes 2 ventas de hace más de 30 días ocultas», y la tabla de **«Mi plan»**.
- Sin scroll horizontal, pestaña «Más» activa, consola sin errores ni advertencias y ninguna respuesta 4xx o 5xx.
- **Corregido** en la revisión:
  - a 375 px el prefijo de la liga apretaba el campo, así que pasó a su propio renglón;
  - la portada del catálogo medía 320 px de alto en escritorio y ahora tiene un tope de 15rem.
- Nota: con `fill` de Playwright, después de un error hacía falta un segundo clic. El error se borra al perder el foco, el botón sube y el clic cae fuera. Con tecleo real el error se limpia al escribir y basta un clic.
- Capturas en `.playwright-mcp/fase510-*.png` (fuera de git).
- El correo del resumen mensual no se vio en navegador: su contenido lo cubren las pruebas con `InMemoryEmailSender`.

Notas:
- En la base de desarrollo quedan el usuario `tienda510@prueba.mx` (Merchant 45, `Active`, en prueba, con la liga `c-dona-rosaab` publicada), su cuenta bancaria falsa con la CLABE de prueba y sus ventas 1 a 3.
- Al arrancar, el sitio aplicó la migración `PublicCatalog` en la base local `Local133`.

## Envío real del resumen (2026-11-24, confirmado por el usuario)
- Un solo correo a `rogelio_133@outlook.com` con Resend (id `01a20ee3-5bb9-7af8-a009-713de8366abf`, respuesta 200), con la base del túnel `https://local133-pey-5231.usw3.devtunnels.ms` para que cargue el logo.
- No se corrió el job: `MonthlySummaryNotifier` le escribe a todos los vendedores con cobros en el mes, y en la base de desarrollo hay otros. Un programa de un solo uso (fuera del repo) repitió el mismo armado: `MonthlySummary.For` ⇒ `MerchantEmails.MonthlySummary` ⇒ `EmailLayout.Render` ⇒ `ResendEmailSender`. No guardó el aviso.
- Datos reales del vendedor 45: $110 en 2 ventas de octubre (se agregaron por SQL los pagos que faltaban a sus ventas 2 y 3). El aviso decía: «En octubre cobraste $110 en 2 ventas. Con una terminal habrías pagado unos $4 de comisión (estimación con 3.5 % + IVA). Tu plan Pro cuesta $199 al mes.»

## Pendientes
- Revisar cómo se ve el resumen en Outlook (enviado el 2026-11-24, ver arriba); falta Gmail y el modo oscuro, y ver el envío del job real el día 1.
- Abrir un catálogo y un recibo con marca en un celular real, y ver cómo previsualiza WhatsApp la liga del catálogo. Con `App:PublicBaseUrl` y `App:LandingUrl` en producción.
- La campana y `/app/avisos` (5.4) mostrarán el aviso del resumen, que hoy solo se guarda en `Notifications` y va por correo.
- En producción, las imágenes públicas dependen del almacenamiento en Blob (Fase 7), igual que las privadas.
- Términos y aviso de privacidad (5.11): el catálogo público muestra el celular del negocio, y el resumen usa los datos de cobros.
