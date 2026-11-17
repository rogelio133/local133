# Fase 2.4 — Venta por monto directo

**Estado:** ✅ hecho 2026-11-11, antes que la 2.3, porque no depende de ella · [← Plan](../PLAN_DESARROLLO.md#fase-2--ventas-núcleo-25-semanas)

## Alcance (del plan)
**Venta por monto directo**: campo de monto → crea `Sale` → `POST /codi/qr` (`generateImage: false`) → vista con el QR (dibujado en el navegador con QRCode.js a partir de `qrString`) y cuenta regresiva en JS.

## Qué se hizo
- Pey: `PeyCreateQrBody.GenerateImage` siempre `false` y explícito en el cuerpo (el OpenAPI dice que por defecto es `false` y que la imagen hace más lenta la respuesta).
- `Domain/Sales`: enums `ChargeMethod`, `SaleSource`, `SaleStatus` y entidad `Sale`:
  - `StartDirect` valida el monto: de $0.01 a $999,999.99, con hasta 2 decimales;
  - `AttachQr`, `IsExpiredAt` / `RemainingSeconds` (no cambian el estatus: eso lo hará el job de la 2.5);
  - `NumRef = Folio % 10⁷`. ~~Concepto recortado a 40 caracteres~~ (reemplazado el 2026-11-12 por el campo Concepto obligatorio, ver abajo).
- `Merchant.CanCharge(BankAccount?)`: vendedor `Active` y cuenta en `PeyCodiStatus.Active`. Ya lo exige el caso de uso; la 2.10 lo reutiliza.
- Tabla `Sales` (migración `Sales`): enums como texto, `Amount decimal(18,2)` con check `>= 0`, FK `Restrict`, **índice único `(MerchantId, Folio)`** e índice único filtrado en `PeyQrId`. Filtro multi-tenant en `AppDbContext`.
- Caso de uso `ISaleCharging.CreateDirectSaleAsync` (Application/Sales + Infrastructure/Sales/SaleCharging):
  1. valida y revisa `CanCharge`;
  2. inserta la venta con `Folio = max + 1` (si choca con el índice único reintenta hasta 3 veces), así ya existe `saleId`;
  3. `POST /codi/qr` a la cuenta del vendedor con `expiration = SystemSettings.SaleQrExpirationSeconds` (600), `numRef` = folio, concepto `"{negocio} · {concepto capturado}"` y metadata `{kind:"sale", merchantId, saleId}` (llaves en `SaleMetadata`);
  4. adjunta el QR.

  Si Pey falla se borra la venta (el folio no se salta) y se muestra un mensaje según el tipo de error. La hora de vencimiento se toma antes de llamar a Pey, así la cuenta regresiva nunca es más larga que la real.
- Web: `ChargeController` en `/app/cobrar` (marca la pestaña "Cobrar"):
  - `GET` muestra el formulario, o un aviso según el estatus con liga a cuenta bancaria o Inicio;
  - `POST` recibe el monto como texto y lo convierte con cultura invariante (sin signo ni separador de miles), luego PRG;
  - `GET venta/{id}` muestra el QR; la venta de otro vendedor da 404 por el filtro.
- Campo de monto (`ChargeInput`): `type=text inputmode=decimal`, **0.01 por defecto**, regex `^\d{1,6}(\.\d{1,2})?$` en cliente y servidor. `wwwroot/js/amount-input.js` bloquea `-`, `+`, `e`, el tercer decimal, el segundo punto y el 7.º entero; convierte la coma en punto; limpia lo pegado o autocompletado; quita ceros a la izquierda al salir; selecciona todo al enfocar.
- Vista del QR: `wwwroot/js/sale-qr.js` usa QRCode.js (`qrcodejs@1.0.0` vía LibMan/cdnjs en `wwwroot/lib/qrcodejs`) con nivel de corrección M y fondo blanco. La cuenta regresiva `mm:ss` parte de los segundos que calcula el servidor; al llegar a 0 oculta el QR y muestra "Este QR expiró". Una venta ya vencida se dibuja sin QR.

## Pruebas
- 46 casos unitarios nuevos (`SaleTests`, `MerchantCanChargeTests`) y 27 de integración (`DirectSaleTests`: formulario, bloqueo por estatus, cuerpo enviado a Pey, montos inválidos, fallas de Pey, folios, aislamiento, vencida, antiforgery, admin). `PeyClientTests` verifica `generateImage: false`. `FakePaymentGateway` simula `CreateCodiQrAsync`. 298/298 verdes en local (183 en CI).

## Revisión en navegador
- El MCP de Playwright no conectó en esta sesión. Se hizo una revisión equivalente con `playwright-core` sobre Edge instalado, a 375 px y 1280 px, con 49/49 revisiones OK:
  - 0.01 por defecto y el filtrado al teclear y al pegar;
  - error en cliente sin POST y error del servidor;
  - pestaña activa; sin scroll horizontal; nada tapado por la barra;
  - el QR dibujado se decodificó con jsQR y contiene exactamente `qrString` (sin BOM);
  - cuenta regresiva y paso a expirado; aviso de bloqueo;
  - consola sin errores y recursos sin 404.

  La venta para la vista del QR se insertó a mano en la base, sin llamar a Pey.

## Pendientes
- ~~revisión con el MCP de Playwright cuando conecte~~ (cubierta el 2026-11-12, ver «Campo Concepto obligatorio»);
- cobro real contra Pey ($0.01, CLABE de prueba), escaneando el QR con una app bancaria;
- confirmar con Pey el límite del `concept` y que `qrString` sea solo ASCII. Si trae acentos, QRCode.js le antepone un BOM UTF-8.

## Campo Concepto obligatorio (agregado 2026-11-12)
- `/app/cobrar` pide, debajo del monto, un **Concepto** obligatorio (placeholder "Ej. 2 tacos al pastor", ayuda "Tu cliente lo verá en su app junto con el nombre de tu negocio"). Su valor es el `concept` de `POST /codi/qr`: `Sale.BuildConcept(negocio, concepto)` = `"{negocio} · {concepto}"`, o solo el concepto si el negocio no tiene nombre. Se quitó el folio del concepto (sigue en `numRef`).
- Por qué ya no se recorta a 40: el 40 era una suposición (concepto de SPEI/CoDi). El OpenAPI de Pey (revisado el 2026-11-12) define `concept` como `string` opcional **sin `maxLength` ni `pattern`**. El concepto capturado admite hasta 100 caracteres (`Sale.ConceptTextMaxLength`); lo enviado, hasta 250 (`Sale.ConceptMaxLength` = negocio 120 + `" · "` + 100). Migración `SaleConceptLength`: `Sales.Concept` de `nvarchar(40)` a `nvarchar(250)` (sigue nullable para QR fijo/push). `StartDirect` exige concepto y ya no corta en silencio (`TrimConcept` eliminado).
- Caracteres: letras (con acentos, ü y ñ), números, espacios y `. , - # / ( )` (`Sale.ConceptTextPattern`, mismo patrón en .NET y JS). Validación en tres capas: atributos de `ChargeInput.Concept` (cliente + model binding), `CreateDirectSaleCommandValidator` (mensajes en `SaleConceptMessages`) y `Sale.StartDirect`. Nuevo hook `data-val-trim` en `wwwroot/js/validation.js` (normalizer de jQuery Validation): solo espacios cuenta como vacío también en cliente.
- La vista del QR muestra el concepto bajo el monto (`data-sale-concept`, con salto de línea).
- Pruebas: 142 unitarias (concepto: `BuildConcept`, caracteres permitidos/rechazados, máximo, obligatorio) y 187 de integración (formulario, concepto enviado con y sin negocio, conceptos inválidos sin llamar a Pey, más de 100, se conserva lo capturado). 329/329 verdes en local (206 en CI).
- Revisión con el **MCP de Playwright** (Edge) a 375, 768 y 1280 px: formulario, errores en cliente (vacío, solo espacios, Enter, emoji, `@`; 0 POST), `maxlength` 100, sin scroll horizontal, nada tapado por la barra inferior, pestaña "Cobrar" activa, vista del QR con concepto corto y de 118 caracteres (QR dibujado, cuenta regresiva), consola sin errores y recursos sin 404. Se corrigió: solo espacios pasaba en cliente (⇒ `data-val-trim`) y el mensaje de caracteres partía la lista de signos en móvil (⇒ espacios de no separación). Ventas y cuenta bancaria insertadas a mano, sin llamar a Pey.
- Pendiente: en el cobro real de $0.01, ver cómo muestra la app bancaria un concepto largo (Banxico podría truncarlo) y si los acentos del concepto llegan al `qrString` (QRCode.js antepondría un BOM).
