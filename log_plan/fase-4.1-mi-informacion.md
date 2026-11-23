# Fase 4.1 — Mi información (datos generales)

**Estado:** ✅ hecho 2026-11-23 · [← Plan](../PLAN_DESARROLLO.md#fase-4--mi-información-4-días)

## Alcance (del plan)
Pantalla «Mi información» con los datos generales del vendedor:
- nombre (el capturado en la 4.0);
- nombre del negocio;
- dirección;
- teléfono;
- correo;
- logo;
- foto del local.

El logo y la foto se usarán en el recibo con marca y en el catálogo público (5.10).

**Demostrable:** desde «Más» ⇒ «Mi información», el vendedor:
- cambia su nombre, el del negocio, su teléfono y la ubicación del puesto;
- sube un logo PNG con fondo transparente y una foto del local;
- los ve guardados;
- puede quitarlos.

Inicio deja de pedirle «Completa tu información».

## Decisiones
Del usuario:
- **Logo en PNG con transparencia:** cabe completo en 512 × 512 px, sin recortar ni agrandar. **Foto del local en JPEG:** lado mayor de 1280 px, sobre blanco.
- **El teléfono es el celular de la cuenta** (`AspNetUsers.PhoneNumber`, el que ya editaba «Mi cuenta»). Se edita en «Mi información», y su campo se quitó de `Identity/Account/Manage/Index`. Así no hay dos teléfonos.
  - Se guarda con `UserManager.SetPhoneNumberAsync`, que renueva el security stamp. Por eso el controlador refresca la cookie (`RefreshSignInAsync`).
  - Queda `PhoneNumberConfirmed = false`, porque no se verifica por SMS.
- **Dirección:** un solo texto libre opcional, «Dirección o ubicación del puesto», de hasta 200 caracteres en 3 renglones. Sirve para quien está en un tianguis.
- **Paso opcional en Inicio**, «Completa tu información». Se muestra hasta que el negocio tiene nombre y teléfono, salvo para un vendedor dado de baja. Es una tarjeta aparte, no un paso del checklist, para no cambiar «Paso X de N».

Propias:
- **Correo en solo lectura**, con «Cambiar correo» al flujo de Identity (`/Identity/Account/Manage/Email`), que ya pide confirmación.
- **Quién edita:** cualquier plan, también el gratuito. Un vendedor `Disabled` solo puede leer (`Merchant.CanEditProfile`, igual que `CanManageProducts`).
- **El nombre del negocio es obligatorio al guardar** y usa los caracteres del concepto del cobro (`BusinessName.Pattern` = `Sale.ConceptTextPattern` con al menos una letra o número, hasta 120). Razón: va al inicio de cada concepto (`Sale.BuildConcept`).
  - Consecuencia: no puede llevar `·`, `'` ni `&`.
  - El alias de la cuenta bancaria solo llena el nombre la primera vez, y solo si cumple esa regla (normalizado).
- **Renombrar el negocio no rompe las ventas viejas.** `Sale.ConceptTextOf(concept)` ya no compara con el nombre actual: corta en el **último** ` · `.
  - Funciona porque el texto del vendedor nunca lleva `·`.
  - Los QR nuevos llevan el nombre nuevo.
  - El recibo público muestra el nombre actual.
  - El alias que tiene Pey no cambia (solo es un alias).
- **Las imágenes son privadas en esta fase:**
  - se sirven en `GET /app/mi-informacion/logo` y `/foto` solo al vendedor de la sesión;
  - llevan `private, max-age=1 año, immutable` y `?v=` con el guid de la llave;
  - la ruta pública llega con la 5.10.
- **Cada imagen tiene su propio formulario**, con su POST y su «Quitar». Así el formulario de datos no viaja con 20 MB, y un error en la imagen no se mezcla con los datos.
- La decodificación de Skia (formato por contenido, cota de 50 MP, decodificar a 2048 px y orientación EXIF) se extrajo a `Infrastructure/Imaging/SkiaImageLoader`, con el fondo como parámetro. La usan las fotos de producto (sin cambios de comportamiento) y las imágenes del vendedor.

## Qué se hizo
- **Dominio:**
  - `Merchants/BusinessName` (`Pattern`, `Normalize`, `IsValid`) y `Merchants/MerchantAddress` (`Normalize` por renglón, `IsValid`: hasta 200, 3 renglones, sin caracteres de control);
  - en `Merchant`:
    - `Address`, `LogoKey` y `ShopPhotoKey`;
    - `CanEditProfile`;
    - `UpdateProfile(owner, business, address)`, que valida y normaliza, y lanza `ArgumentException`;
    - `ReplaceLogo` y `ReplaceShopPhoto`, que devuelven la llave anterior;
    - `HasCompleteProfile(phone)`;
  - `Sale.ConceptTextOf(concept)`: un solo parámetro, corta en el último separador.
- **Application** (`Merchants/`):
  - `MerchantImageKind`;
  - `MerchantImageRules`: 10 MB, 512 px y 1280 px, llaves `merchants/{id}/logo-{guid}` y `local-{guid}`, `FileKey` y `Version`;
  - `MerchantImageMessages` e `IMerchantImageProcessor`;
  - `IMerchantProfile`, con `GetAsync`, `SaveAsync`, `SaveImageAsync` y `RemoveImageAsync`, sus resultados y `ProfileMessages`.
- **Infrastructure:**
  - `Imaging/SkiaImageLoader`, `Merchants/SkiaMerchantImageProcessor` y `Merchants/MerchantProfileService`:
    - los datos y el celular se guardan en una transacción;
    - las imágenes siguen el patrón de `ProductCatalog`: se guardan los archivos antes que la base, se borran los nuevos si la base falla y los anteriores después de guardar;
  - `SkiaProductPhotoProcessor` usa el loader;
  - `MerchantConfiguration` y la migración **`MerchantProfile`** (solo `AddColumn`);
  - `BankAccountRegistration`: el alias solo llena el nombre del negocio si es válido.
- **Web:**
  - `Areas/Merchant/Controllers/ProfileController` (`/app/mi-informacion`):
    - GET y POST de los datos;
    - POST de `logo`, `foto`, `logo/quitar` y `foto/quitar`;
    - GET de `logo` y `foto`;
  - `Models/ProfileViewModels.cs` (`ProfileInput`, `ProfileViewModel`, `ProfileImageCardViewModel` y `ProfileUrls`);
  - vistas `Profile/Index` y `_ProfileImageCard`;
  - validación en cliente:
    - `Validation/MaxLinesAttribute` + el adaptador `maxlines` en `validation.js`;
    - `[MexicanPhone]` con el mensaje propio;
  - `wwwroot/js/image-upload.js`, uno por `form[data-image-upload]`:
    - vista previa;
    - «Guardar» deshabilitado hasta elegir una imagen;
    - tipo y 10 MB;
    - reducción a 2048 px, en PNG para el logo (sin fondo blanco) y en JPEG para la foto;
  - estilos `.profile-image*`: el logo sobre cuadros, para que se note la transparencia;
  - «Más»: «Mi información» va primero, y se quitó el «Próximamente»;
  - Inicio: la tarjeta «Completa tu información · Opcional» (`data-profile-incomplete`);
  - `Manage/Index`: solo el correo, y para los vendedores la liga «Tu teléfono y los datos de tu negocio están en Mi información».

## Pruebas
- **Unitarias:**
  - `BusinessNameTests`: válidos, inválidos (`·`, `'`, `&`, emoji, sin letras), 120/121 y `Normalize`;
  - `MerchantAddressTests`: renglones, control, 200 y `Normalize`;
  - `MerchantProfileTests`: `UpdateProfile`, `Replace*`, `CanEditProfile` y `HasCompleteProfile`;
  - `SaleReceiptTests`: `ConceptTextOf` con un negocio renombrado y con `·` en un nombre viejo.
- **Integración:**
  - `ProfileTests` (28):
    - datos y correo en solo lectura;
    - guardar normalizado con el celular de la cuenta, y la sesión sigue viva después del cambio de security stamp;
    - vaciar el teléfono y la dirección;
    - 8 casos inválidos sin guardar nada, y el negocio de 121 caracteres;
    - atributos `data-val-*`;
    - **cada formulario de la página trae su token antiforgery**;
    - sin token ⇒ 400;
    - dado de baja ⇒ solo lectura;
    - Admin y anónimo no entran;
    - logo PNG con alfa de 512×256 y caché; foto JPEG enderezada y sin EXIF;
    - reemplazar o quitar borra lo anterior;
    - archivo inválido o sin archivo, y más de 10 MB;
    - aislamiento entre vendedores;
    - «Más», el paso de Inicio (y no para dados de baja) y «Mi cuenta» sin celular;
    - una venta vieja se ve bien después de renombrar el negocio, y el siguiente cobro lleva el nombre nuevo.
  - `MerchantImageProcessorTests` (sin SQL Server, corre en CI): transparencia, encaje sin recorte, sin agrandar, JPEG sobre blanco, EXIF, rechazos.
  - `BankAccountTests`: un alias inválido no llena el negocio; uno válido se normaliza.
- `dotnet test`: 555 unitarias, 12 de la portada y 658 de integración, todas verdes. `dotnet ef migrations has-pending-model-changes`: sin cambios.

## Revisión en navegador (Playwright, Edge)
- Se arrancó con `Jobs__Enabled=false Pey__ApiKey= Resend__ApiKey=`. Se usó una cuenta nueva con dirección de prueba de Resend (`delivered+fase41@resend.dev`), confirmada con el `.html` de `App_Data/emails`.
- `/app/mi-informacion` a **375, 768 y 1280 px**:
  - una columna en móvil y tablet; datos e imágenes lado a lado en escritorio;
  - pestaña «Más» activa;
  - sin scroll horizontal; el contenido termina arriba de la barra inferior.
- **Validación en cliente**, sin petición al servidor:
  - nombre de solo espacios ⇒ «Escribe tu nombre.»;
  - «Tacos · Don Ramón» ⇒ mensaje de caracteres;
  - teléfono de 9 dígitos ⇒ «Escribe un teléfono de 10 dígitos…»;
  - dirección de 4 renglones ⇒ «Escribe la dirección en 3 renglones como máximo.».
- **Flujo:**
  - guardar «  Rosa   Peña », «Tacos Doña Rosa», «55 1234 5678» y dos renglones de dirección ⇒ «Tu información se guardó.», con los datos normalizados (`5512345678`);
  - logo PNG transparente: vista previa sobre cuadros, se guarda como PNG de 512×256;
  - foto JPEG de 2400×1600: se guarda de 1280×853;
  - «Quitar logo» lo quita y vuelve el marcador «Sin logo»;
  - Inicio ya no muestra el paso opcional;
  - «Más» y «Mi cuenta» (solo el correo y la liga) a 375 px.
- Consola sin errores ni advertencias; ninguna petición 4xx/5xx, salvo la del incidente.
- **Corregido durante la revisión:** el formulario de datos no traía el token antiforgery, y guardar daba **400**.
  - Causa: con `action` explícito, el tag helper no lo agrega.
  - Las pruebas no lo vieron porque su helper toma el token de cualquier formulario de la página.
  - Se agregó `@Html.AntiForgeryToken()` y la prueba `Cada_formulario_de_la_pagina_trae_su_token_antiforgery`.
- Ajuste pedido por el usuario: el cuadro del logo va centrado en su tarjeta (`mx-auto`). Se revisó a 375, 768 y 1280 px, con la consola limpia.
- Nota: «Tomar foto» (cámara) se oculta en escritorio con `(hover: hover) and (pointer: fine)`, como en productos. En Edge emulado no se ve.

## Pendientes
- «Tomar foto» de la foto del local en un celular real (Android e iPhone), junto con el pendiente igual de la [3.1](fase-3.1-productos.md).
- Ruta pública del logo y la foto, recibo con marca y catálogo público: [5.10](../actualizaciones/suscripciones_2026_11_18.md#510-valor-visible).
- En producción, `LocalDiskFileStorage` ⇒ Blob Storage (Fase 7, ver `azure.md`).
- Mostrar en el admin (Fase 6) los datos de «Mi información» en el detalle del cliente.
