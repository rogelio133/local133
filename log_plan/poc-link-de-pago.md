# PoC — Link de pago de Pey (2026-11-18)

Fuera de las fases del plan, pedido por el usuario: ver cómo se ve la página de pago que hospeda Pey. El frontend es desechable. El backend se reutiliza cuando el link de pago se implemente de verdad.

## Qué dice Pey (OpenAPI)
- `POST /payment-link/create`: `amount`, `successUrl` y `backUrl` (HTTPS, obligatorias), `hasTip`, `clientReferenceId` (≤ 255) y `metadata`. Responde `{ id, url }`.
- `GET /payment-link/{id}`: `status` en minúsculas (`pending|processing|paid|partial`), `paymentMethods` y `transactions`.
- **No acepta `bankAccountId` ni concepto.** El dinero cae en la cuenta de la API key, es decir, en la de la plataforma. Eso choca con la regla de que la plataforma nunca toca el dinero del vendedor; por eso la PoC solo existe en Development.

## Qué se hizo
- Backend reutilizable:
  - `IPaymentGateway.CreatePaymentLinkAsync` / `GetPaymentLinkAsync`, con los DTOs en `Application/Payments/PaymentLinks.cs`.
  - `PeyClient`, más la validación previa en `PeyRequestGuard` (monto > 0 en centavos, URLs absolutas HTTPS, `clientReferenceId` ≤ 255, metadata).
  - Errores nuevos: `INVALID_URL`, `INVALID_HAS_TIP` e `INVALID_CLIENT_REFERENCE_ID` ⇒ validación; `PAYMENT_LINK_NOT_PENDING` ⇒ estado.
  - `POST /payment-link/update` no se implementó.
- La base pública de las ligas (`App:PublicBaseUrl` o el host de la petición) pasó a `PublicSiteOptions.BaseUrlFor`, que también usa `ReceiptLinks`.
- PoC `/app/link-de-pago` (`PaymentLinkPocController`):
  - Responde 404 fuera de Development. Sin liga en el menú: se entra por URL directa (la liga de «Más» se quitó a pedido del usuario el mismo día).
  - Pide monto y concepto, con las mismas reglas de Cobrar.
  - El concepto va en `metadata.concept` y en `clientReferenceId`.
  - Redirige directo a la página de Pey.
  - `successUrl`/`backUrl` = `/app/link-de-pago/regreso?resultado=pagado|cancelado`. Esa página consulta el link (id en `TempData`) y muestra lo que regresa el GET.
  - No guarda nada en la base.
  - Si la base no es HTTPS, avisa y no llama a Pey.

## Pruebas
- `PeyClientTests` (WireMock): crear y consultar un link, estatus inválidos, errores nuevos y validación previa.
- `PaymentLinkPocTests` (sitio, Pey simulado).
- `dotnet test`: 827 pruebas en verde.

## Revisión en navegador (Playwright, Edge)
- «Más» con la liga «Generar link de pago» y la etiqueta «Prueba»; formulario y página de regreso a 375, 768 y 1280 px.
- Sin scroll horizontal, pestaña «Más» activa y consola sin errores.
- La validación en cliente bloquea el envío con monto vacío y con un concepto con `<`.
- Con el sitio por http aparece el aviso de HTTPS.
- **No se envió el formulario**: con una API key configurada, enviar llamaría a Pey real. No se probó el aviso de HTTPS en el servidor (sí está cubierto por la validación previa y las pruebas).

## Pendientes
- Llamada real (necesita confirmación): arrancar con el perfil `https` o con túnel + `App:PublicBaseUrl`, generar un link y ver la página de Pey. Si Pey rechaza `https://localhost` con `INVALID_URL`, usar el túnel.
- Confirmar con Pey:
  1. cómo dirigir el dinero del link a la CLABE del vendedor;
  2. si el pagador ve algún concepto;
  3. el monto mínimo con tarjeta;
  4. el payload de `payment_link.status_updated`, que hoy se marca `Ignored`.
