# Ajustes 2026-11-26 — Bloque 2: concepto del cobro y «Mi información»

**Estado:** ✅ hecho 2026-11-26 · [← Plan](../PLAN_DESARROLLO.md) · [Documento de los ajustes](../actualizaciones/ajustes_2026_11_26.md)

Puntos 7 (quitar el nombre del negocio del concepto) y 8 («Mi información»: concepto por defecto y nombre solo con letras).

## Qué se hizo

### 7. El concepto sin el nombre del negocio
- `SaleCharging` manda a Pey y guarda **solo el concepto** que escribió el vendedor (recortado), en QR, push y carrito. Se borró `Sale.BuildConcept`.
- **Ventas viejas**: siguen guardadas como «Negocio · concepto». `Sale.ConceptTextOf` (corta en el último ` · `) y `SaleSearch` (busca después del separador, o en todo el concepto si no lo hay) no cambiaron, solo sus comentarios. Los conceptos nuevos no pueden traer `·`, así que se muestran completos.
- `BusinessName` conserva sus reglas (sin `·`): protegen a `ConceptTextOf` con las ventas viejas y sirven para el recibo, el catálogo y el alias.
- Textos:
  - Cobrar › Monto y Productos: «Tu cliente lo verá en su app bancaria.»;
  - «Mi información»: «El nombre del negocio aparece en tus recibos y en tu catálogo.»;
  - ayuda del campo del negocio: «Aparece en tus recibos y en tu catálogo en línea.» Decía «Lo ve tu cliente en su app del banco al pagarte», que dejó de ser cierto; el documento no lo listaba.

### 8a. Concepto por defecto
- `Domain/Merchants/DefaultConcept`: reutiliza `Sale.ConceptTextMaxLength`, `Sale.ConceptTextPattern` y `Sale.IsValidConceptText`. Recorta, y vacío ⇒ `null`.
- `Merchant.DefaultConcept` (`nvarchar(100)`, nulo; migración `MerchantDefaultConcept`). `UpdateProfile(owner, business, address, defaultConcept)` lo valida. El parámetro es **obligatorio**, para que nadie lo borre por omitirlo.
- `IMerchantProfile`: `MerchantProfileData.DefaultConcept` y `SaveProfileCommand.DefaultConcept`. `MerchantProfileService.Validate` revisa el largo y luego el patrón; los mensajes son los de `SaleConceptMessages`.
- «Mi información»: campo «Concepto (opcional)» debajo de la dirección, con `maxlength`, `data-val-trim`, patrón y la ayuda «Es el concepto que verá tu cliente si no escribes otro. Puedes cambiarlo en cada cobro.»
- Cobrar › Monto lo precarga solo en el `GET` (`FormAsync(..., prefillConcept: true)`, con `??=`). Al volver a pintar tras un error se respeta lo enviado. El carrito no lo precarga (decisión).
- Admin: no se muestra (era opcional, y la pestaña del cliente tampoco muestra la dirección).

### 8b. Nombre solo con letras
- `PersonName.Pattern` = `^(?=.*[A-Za-zÀ-ÖØ-öø-ÿ])[A-Za-zÀ-ÖØ-öø-ÿ ]+$`. `Normalize` ya no convierte `’`.
- `PersonName.Clean` cambia por espacio todo lo que no sea letra y luego normaliza. No valida.
- Mensaje «Escribe solo letras y espacios.» (`OwnerNameMessages.Invalid` y `ProfileMessages.OwnerNameInvalid`).
- `wwwroot/js/letters-input.js` para `input[data-letters-only]`:
  - en `beforeinput` cancela una tecla no permitida;
  - en `input` limpia lo pegado o autocompletado y conserva el cursor;
  - durante la composición (tecla muerta, IME) espera, y limpia en `compositionend`.

  Se usa en `_OwnerNameField` (Register y ExternalLogin) y en el nombre de «Mi información».
- **Nombres ya guardados** con signos: no se migran. `ProfileViewModel.From` propone `PersonName.Clean(OwnerName)` («Ma. José O'Brien» ⇒ «Ma José O Brien»), así que guardar no falla. El saludo sigue con el nombre guardado hasta que el vendedor guarde.
- ExternalLogin propone el nombre del proveedor limpio con `Clean`.

## Pruebas
- Unitarias: 702 ✅.
  - `PersonNameTests`: solo letras; rechaza «Ma.», «O'Brien», «O’Brien», «Ana-Luisa» y «Juan2»; casos de `Clean`.
  - `DefaultConceptTests` (nueva).
  - `MerchantProfileTests`: concepto por defecto vacío ⇒ `null`; inválido o de 101 ⇒ `ArgumentException`.
  - `SaleTests`, `SaleReceiptTests` y `BusinessNameTests` sin `BuildConcept`.
- Integración: 872 ✅ en local (SQL Server).
  - `DirectSaleTests`, `PushSaleTests` y `CartSaleTests`: `FakePaymentGateway` y la venta reciben el concepto sin el negocio. El concepto por defecto se precarga en Monto, no tras un error y no en el carrito.
  - `ProfileTests`: guardar y borrar el concepto por defecto; inválido o de 101; nombre con signos rechazado; nombre viejo propuesto limpio; atributos de cliente y `letters-input`; una venta vieja se sigue viendo sin el negocio.
  - `MerchantRegistrationTests`: «O'Brien», «Ma.» y «Ana-Luisa» rechazados; los formularios de alta traen `data-letters-only`.
- Portada: 61 ✅.
- `dotnet ef migrations has-pending-model-changes`: sin cambios.
- Nota: `dotnet build` en Debug no pudo copiar las DLL a `src/Local133.Web/bin/Debug`, porque las tenía abiertas una instancia del sitio lanzada desde Visual Studio. Todo se compiló y probó en **Release**, y la migración se generó con `--configuration Release`.

## Revisión en navegador (Playwright, Edge)
Sitio en Release en `http://localhost:5232` con `Jobs__Enabled=false Pey__ApiKey= Resend__ApiKey=`. Vendedor de prueba registrado y confirmado con el correo de `App_Data/emails`; luego activado por SQL con una cuenta bancaria ficticia `Active` (ver Pendientes).
- **Registro (375)**:
  - teclear «Ma. José O'Brien 2» deja «Ma José OBrien »;
  - pegar «Ana-Luisa⇥Peña #3 Ñ» deja «AnaLuisa Peña  Ñ» (el servidor junta los espacios);
  - una composición con tecla muerta no se interrumpe y termina en «José».
- **«Mi información» (375, 768, 1280)**:
  - el nombre guardado «Ma. José O'Brien» se propone «Ma José O Brien»;
  - el campo nuevo va debajo de la dirección;
  - «Taco · uno» marca el error en el cliente al salir del campo;
  - corregido a «Consumo general», un clic guarda («Tu información se guardó.»);
  - sin scroll horizontal.
  - Un primer intento no envió el formulario, porque los eventos se habían inyectado con script. Con teclado real funciona a la primera.
- **Cobrar › Monto (375, 768, 1280)**: precargado con «Consumo general» y el texto «Tu cliente lo verá en su app bancaria.»
- **Cobrar › Productos**: con un producto, el concepto va vacío (lo propone `cart.js`), con el texto nuevo y sin scroll horizontal.
- Consola sin errores ni advertencias.
- No se llamó a Pey: el concepto enviado lo cubren las pruebas de integración.

## Pendientes
- **Pey (no bloquea)**: confirmar si el pagador ve el nombre del titular de la cuenta en su app ahora que el concepto ya no lleva el negocio. En «Verificaciones pendientes».
- **Base de desarrollo**: quedó el vendedor de prueba `bloque2.playwright@prueba.test` (Id 49), activo, con una cuenta bancaria **ficticia** (`PeyBankAccountId` `PWFAKE0000049`) y el producto «Taco al pastor». Un cobro real con él fallaría en Pey. Borrarlo si estorba.
