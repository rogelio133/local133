# Mantenimiento: correo «Tu cuenta ya está activa» y layout de correo con marca

**Estado:** ✅ hecho 2026-11-18 · [← Plan](../PLAN_DESARROLLO.md) · rama `feature_email_accountactive`

## Alcance
Cuando la cuenta bancaria del vendedor queda activa, se le manda un correo con botón al sitio. También se crea un layout de correo con la identidad del sitio (rosa mexicano, Bungee, logo) que usan todos los correos. Cierra la parte «activada» del pendiente de la 2.3. El correo de cuenta **rechazada** sigue pendiente.

Decisiones con el usuario:
- El layout se aplica a **todos** los correos (activación y los de Identity: confirmar, restablecer y cambiar correo).
- El correo sale **siempre que el vendedor pase a activo**: webhook `bank.account.updated`, sondeo, botón «Verificar estatus» y también el alta cuando Pey responde `ACTIVE` de inmediato.
- Logo: sí, en **PNG alojado en el sitio** (no SVG, `data:` URI ni adjunto CID). Gmail, Outlook y Yahoo no muestran SVG, y Gmail bloquea los `data:` URI. El PNG trae «Local133» en Bungee como imagen, porque Gmail no carga fuentes web.

## Qué se hizo
- **Domain**: `Merchant.ActivationEmailSentAt` (migración `MerchantActivationEmail`, columna nula, sin relleno). Es el candado del aviso.
- **Application**: `IBankAccountActivationNotifier.NotifyActivatedAsync(merchantId)` (`BankAccounts/BankAccountActivation.cs`). Es idempotente y nunca lanza.
- **Infrastructure**:
  - `BankAccounts/BankAccountActivationEmailNotifier`:
    1. Toma el candado con `ExecuteUpdateAsync`, condicionado a `Status == Active && ActivationEmailSentAt == null`.
    2. Lee el correo del usuario, el negocio y la CLABE (sin filtro de tenant).
    3. Envía el correo con `IEmailSender`.

    Cualquier error se registra en el log y no se propaga, para que el webhook no responda 500 (el cambio de estatus ya está guardado).
  - Puntos de llamada, después de `SaveChanges` y solo si `ApplyBankAccountStatus` devolvió `true` y el vendedor quedó `Active`:
    - `BankAccountStatusSync.ApplyAsync` (webhook, sondeo y botón);
    - `BankAccountRegistration` (alta).
  - `Email/EmailLayout` (`EmailContent` + `Render`): documento completo con tablas y estilos en línea, 600 px máximo, encabezado `#E4007C` con el logo y título en Bungee con respaldo `'Arial Black', Arial`. Texto en Atkinson Hyperlegible (respaldo Arial), botón rosa con borde `#B8005F`, liga de respaldo en texto, pie y liga al sitio. Codifica todos los textos con `HtmlEncoder.Create(UnicodeRanges.All)`.
    - **Regla**: el botón es la primera liga (`href`) del documento, porque `FileEmailSender` y `InMemoryEmailSender.Link` toman la primera. Por eso las fuentes van con `@import` (no con `<link href>`) y el logo no lleva liga.
  - `Email/MerchantEmails.BankAccountActiveBody`: asunto «Tu cuenta ya está activa: ya puedes cobrar · Local133», saludo con el negocio, CLABE con `Clabe.Mask`, botón «Empezar a cobrar» → `{base}/app/cobrar`.
  - `Email/SiteUrlOptions` (sección `App`, `PublicBaseUrl`) define la base de las ligas de los correos que salen sin petición. En Development, si falta, usa `http://localhost:5231`. Fuera de Development, si falta, registra un error y **no manda** el correo.
  - `FileEmailSender.Document`: si el correo ya es un documento completo, mete la franja «Para/Asunto» dentro de su `<body>` en lugar de anidar otro documento.
- **Web**: `AccountEmails` usa `EmailLayout` y agrega un preheader por correo. La base del logo sale del host de la liga de Identity, así que las páginas no cambiaron.
- **Logo**: `wwwroot/img/email/local133-email-logo.png`, 416×80 (se muestra a 208×40), 8 KB. Es el ícono de `local133-invertido.svg` en blanco más «Local133» en Bungee sobre `#E4007C`. Se generó con una página HTML temporal servida por el sitio y una captura de Edge con el MCP de Playwright. Para cambiarlo hay que regenerarlo igual y subir `EmailLayout.LogoVersion`.
- Ojo con el color: `local133.svg` usa `#F40284`, mientras que el rosa del sitio (`$rosa-mexicano`) es `#E4007C`. El correo usa `#E4007C`.

## Pruebas
- `dotnet build`: 0 advertencias, 0 errores. `dotnet ef migrations has-pending-model-changes`: sin cambios.
- `EmailLayoutTests` (sin SQL Server, corre en CI):
  - primera liga = botón, sin `<link>`;
  - codificación de todos los textos, con acentos legibles;
  - logo PNG absoluto con medidas y `alt`;
  - el PNG existe y pesa menos de 20 KB;
  - plantilla de cuenta activa (saludo con o sin negocio, CLABE enmascarada, liga a `/app/cobrar`);
  - `AccountEmails` sobre el layout;
  - `SiteUrlOptions.BaseUrl` (con o sin dominio, en desarrollo y fuera);
  - `FileEmailSender.Document` con documento completo y con fragmento.
- Integración con SQL Server:
  - webhook ACTIVE ⇒ un correo (destinatario, liga, CLABE enmascarada, candado lleno);
  - vendedor ya activo ⇒ ninguno;
  - dos webhooks + tres avisos concurrentes ⇒ uno;
  - correo que falla ⇒ el webhook queda `Processed` con 200;
  - sondeo ⇒ un correo por cada vendedor activado, ninguno en la segunda corrida, ninguno con Pending/Processing/Error;
  - alta con `ACTIVE` inmediato ⇒ correo con «¡Hola, Tacos Don Ramón!»; alta en revisión ⇒ ninguno;
  - «Verificar estatus» con Active ⇒ correo.
- `dotnet test` completo: unitarias **312/312**, landing **8/8**, integración **522/522**.

## Revisión en navegador (MCP de Playwright, Edge)
- Sitio en Development con `Jobs__Enabled=false` (nada llamó a Pey). Sobre una cuenta de prueba de la base de desarrollo (vendedor 2) se insertó por SQL una cuenta `Processing`. Después se envió un `bank.account.updated` firmado a mano: respuesta 200 `Processed`, vendedor `Active`, `ActivationEmailSentAt` lleno y `.html` en `App_Data/emails` con la liga `http://localhost:5231/app/cobrar`. Al terminar se restauraron los datos del vendedor 2.
- Correo de activación a **375, 768 y 1280 px**: sin scroll horizontal, logo cargado (416 → 208 px), Bungee cargada, consola sin errores.
- Simulaciones:
  - **sin fuentes web** (como Gmail): el título sale en el respaldo y el logo conserva la palabra en Bungee;
  - **con imágenes bloqueadas**: se ve el `alt` «Local133» blanco sobre el rosa.
- Correo de restablecer contraseña (pedido desde `/Identity/Account/ForgotPassword`) a 375, 768 y 1280 px: la liga larga se parte sin desbordar.
- Capturas en `.playwright-mcp/` (ignorada por git): `email-activa-{375,768,1280}.png`, `email-activa-sin-fuentes.png`, `email-activa-sin-imagenes.png`, `email-reset-{375,768,1280}.png`.

## Pendientes
- **Prueba en buzones reales** (requiere confirmación): enviar por Resend a `delivered+activa@resend.dev` y después a un buzón propio, viendo Gmail en Android y en web, Outlook y el modo oscuro. Debe hacerse con túnel + `App:PublicBaseUrl` o desde producción, porque Gmail no descarga el logo desde `localhost`.
- Correo de cuenta **rechazada** (el `ERROR` lo detecta el sondeo).
- En la 5.4 (avisos), el candado puede pasar a `Notifications.DedupKey`.
