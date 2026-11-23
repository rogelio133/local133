# Fase 4.0 — Nombre en el registro

**Estado:** ✅ hecho 2026-11-23 · [← Plan](../PLAN_DESARROLLO.md#fase-4--mi-información-4-días)

## Alcance (del plan)
Pedir el nombre de la persona en las dos altas: correo y contraseña (`Register`) y Google/Facebook (`ExternalLogin`).
- Es obligatorio, de hasta 100 caracteres, con letras (con acentos), espacios, `.`, `-` y `'`.
- Se valida en el cliente y en el servidor.
- Se guarda en `Merchant.OwnerName` (columna nueva y nula) en la transacción de `AccountRegistration`.
- Con Google/Facebook se propone el nombre que da el proveedor.
- Se usa en el saludo de Inicio («Hola, María») y en los correos. Sin nombre, se queda el respaldo anterior.

**Demostrable:** quien se registra escribe «María José O'Brien», recibe el correo de confirmación con «¡Hola, María!», y al entrar Inicio dice «Hola, María».

## Decisiones
- **El saludo usa el primer nombre** (la primera palabra): «María José López» ⇒ «María». El nombre completo queda guardado para la 4.1 y el admin.
- **Orden del respaldo**, en un solo lugar (`Merchant.GreetingNameFor`): primer nombre, luego nombre del negocio y luego, según la pantalla, el correo (Inicio) o «¡Hola!» (correos). Los vendedores registrados antes de la 4.0 tienen `OwnerName = NULL` y conservan el saludo de antes.
- **El patrón es el mismo en .NET y en JS:** `^(?=.*[A-Za-zÀ-ÖØ-öø-ÿ])[A-Za-zÀ-ÖØ-öø-ÿ .'’\-]+$`.
  - Usa rangos Latin-1 y no `\p{L}`, porque jQuery Validation arma el `RegExp` sin la bandera `u`.
  - Exige al menos una letra, así que «...» no pasa.
  - Acepta `’` porque el teclado del iPhone convierte `'` en `’`. Al guardar se normaliza a `'`, se quitan los espacios de los extremos y se juntan los repetidos.
- La clase se llama `PersonName` y no `OwnerName`, porque dentro de `Merchant` la propiedad `OwnerName` (string) taparía el tipo.
- **Nombre del proveedor:** `ClaimTypes.Name` o, sin él, `GivenName` + `Surname`, normalizado y recortado a 100. Se propone aunque traiga caracteres no permitidos: el usuario lo corrige y la validación lo marca.
- **Correo de confirmación:** lleva «¡Hola, {primer nombre}!», y el título «¡Bienvenido a Local133!» cambió a «Te damos la bienvenida a Local133», que no tiene género. Restablecer contraseña y cambiar correo siguen sin saludo: también los usa el admin y esas páginas no cargan el `Merchant`.

## Qué se hizo
- **Dominio:**
  - `Merchants/PersonName` (`MaxLength`, `Pattern`, `Normalize`, `IsValid`, `FirstName`);
  - `Merchant.OwnerName`;
  - `Merchant.Register(userId, now, ownerName)`, que normaliza el nombre y lanza `ArgumentException` si no es válido;
  - `Merchant.GreetingName` y `GreetingNameFor(owner, business)`.
- **Infrastructure:**
  - `MerchantConfiguration` (`nvarchar(100)` nula) y la migración **`MerchantOwnerName`** (solo `AddColumn`);
  - `MerchantEmails`: el parámetro `businessName` cambió a `greetingName`, con un helper `Greeting`;
  - el correo de cuenta activa, el de pago de Pro y los recordatorios (`SubscriptionDailyJob`) saludan con `GreetingNameFor`.
- **Web:**
  - `Identity/MerchantSignUpInput`, base de los `InputModel` de Register y ExternalLogin, con `OwnerName`: `[Required]`, `[StringLength(100)]`, `[RegularExpression]` y los mensajes de `OwnerNameMessages`;
  - parcial `Areas/Identity/Pages/Account/_OwnerNameField`: primer campo del formulario, con `autocomplete="name"`, `autocapitalize="words"`, `maxlength=100`, `data-val-trim` y el texto «Así te saludaremos en Local133.»;
  - `AccountRegistration.CreateMerchantAsync(user, ownerName, password, login)` valida antes de crear nada (si no es válido, `IdentityResult.Failed`) y guarda el nombre en la misma transacción;
  - `ExternalLogin` propone el nombre del proveedor;
  - `AccountEmails.ConfirmBody(url, greetingName)`;
  - `MerchantHomeViewModel` usa `merchant.GreetingName ?? correo`.

## Pruebas
- **Unitarias** (`Merchants/PersonNameTests`, 40 casos):
  - válidos e inválidos (vacío, solo espacios, sin letras, números, emoji, `<`, `_`, `@`) y el borde de 100/101;
  - `Normalize` (espacios, tabulador, `’`) y `FirstName`;
  - `Register` normaliza y rechaza;
  - el respaldo del saludo.
- **Integración:**
  - `MerchantRegistrationTests`:
    - el registro guarda el nombre normalizado y el correo de confirmación saluda;
    - el alta externa guarda el nombre y Inicio saluda;
    - el alta externa propone «Usuario Externo»;
    - un nombre vacío, de solo espacios, con números, «...», `<script>` o de 101 caracteres da 200 con el mensaje y no crea la cuenta (también en el alta externa);
    - los atributos `data-val-*`, `autocomplete` y el orden de los campos;
    - `AccountRegistration` rechaza un nombre inválido sin crear nada;
  - `MerchantHomeTests`: «Hola, María», el respaldo al negocio y al correo con `OwnerName = NULL`, y el aislamiento entre vendedores;
  - `BankAccountTests`: el correo de cuenta activa saluda con el nombre de la persona y no con el negocio.

  Los helpers `RegisterAsync`, `ConfirmExternalAsync` y `CreateConfirmedUserAsync` mandan `WebTestBase.OwnerName` («María López»).
- `dotnet test`: 498 unitarias, 12 de la portada y 620 de integración, todas verdes. `dotnet ef migrations has-pending-model-changes`: sin cambios.

## Revisión en navegador (Playwright, Edge)
- `/Identity/Account/Register` a **375, 768 y 1280 px**:
  - el nombre es el primer campo;
  - el error y el texto de ayuda se ven igual que en el campo de contraseña;
  - sin scroll horizontal.
- **Validación en cliente** sin enviar al servidor:
  - vacío y solo espacios ⇒ «Escribe tu nombre.»;
  - «123», «María 2» y «...» ⇒ «Usa solo letras, espacios, punto, guion y apóstrofo.»;
  - 101 caracteres ⇒ «Tu nombre puede tener hasta 100 caracteres.»;
  - «O'Brien», «María José O’Brien» y «  Ana-Lucía Ma. Peña » pasan.
- **Flujo completo:**
  - el alta con «  María José   O’Brien » quedó guardada como `María José O'Brien`;
  - el alta con «Rosa Peña» generó el correo en disco con «¡Hola, Rosa!» y «Te damos la bienvenida a Local133»;
  - después de confirmar y entrar, `/app` a 375 px dice «Hola, Rosa».
- Consola sin errores ni advertencias, y ninguna petición 4xx/5xx.
- **Incidente:** el primer alta de la revisión se hizo con el sitio arrancado sin vaciar `Resend:ApiKey` (está en los user-secrets de desarrollo). Por eso el correo de confirmación de `maria.fase40@prueba.mx`, una dirección inventada, **salió de verdad por Resend**. Solo traía la liga de confirmación de una cuenta de prueba. El resto de la revisión se hizo con `Resend__ApiKey=` vacío y con una dirección de prueba de Resend. Para revisiones futuras, arrancar con `Jobs__Enabled=false Pey__ApiKey= Resend__ApiKey=`.
- Nota: `browser_fill_form` del MCP no marca la casilla legal (dice «did not change its state»); un `browser_click` directo sí la marca. Es una peculiaridad de la herramienta, no del sitio.

## Pendientes
- Formulario de `ExternalLogin` en el navegador con Google/Facebook real: ver qué nombre propone cada proveedor. Las pruebas de integración lo cubren con el proveedor falso. Se suma a la prueba real de la [1.3](fase-1.3-login-externo.md).
- La 4.1 permitirá editar el nombre (con las mismas reglas de `PersonName`).
