# Plan: integrar identidad visual con Bootstrap SCSS en .NET Core

Nov 12, 2026 · @Rogelio

## Contexto y objetivo

Reemplaza el Bootstrap precompilado del proyecto ASP.NET Core por Bootstrap compilado desde SCSS con variables propias, para que el sitio tenga identidad visual y no se vea genérico.

El sitio es para que vendedores de mercados mexicanos cobren con códigos QR. La identidad parte del rosa mexicano (#E4007C) y de elementos del mercado: lonas azules, cempasúchil, nopal y letreros rotulados a mano. Los usuarios leen en celular, muchas veces bajo el sol y con prisa.

Instrucción para Claude Code: lee este plan completo, revisa la estructura del proyecto antes de tocar nada y ejecuta los pasos en orden. Si algo del proyecto no coincide con lo que asume el plan (por ejemplo, es Blazor y no MVC/Razor Pages), detente y pregunta.

## Reglas y restricciones

- Personaliza Bootstrap solo con variables Sass antes del import. No sobrescribas clases de Bootstrap con CSS suelto ni uses `!important`.
- No uses Node ni npm. La compilación va con el paquete NuGet `AspNetCore.SassCompiler` (usa dart-sass).
- No modifiques nada dentro de `Styles/vendor/`: es código de terceros.
- No agregues degradados, sombras grises difusas ni animaciones de entrada en cada sección.
- Conserva el JavaScript de Bootstrap (`bootstrap.bundle.min.js`) tal como está.
- Antes de borrar o sobrescribir `wwwroot/css/site.css`, mueve su contenido a `Styles/site.scss`.
- El rosa base (#E4007C) da contraste de \~4.6:1 sobre blanco. Para enlaces en texto corrido usa #B8005F. Nunca pongas texto sobre el amarillo cempasúchil en fondo blanco.

## Pasos de implementación

1. **Instalar el compilador Sass.** Ejecuta `dotnet add package AspNetCore.SassCompiler`.
2. **Descargar el código fuente SCSS de Bootstrap.** La plantilla de .NET solo trae el CSS compilado en `wwwroot/lib/bootstrap`. Ejecuta:
   - `dotnet tool install -g Microsoft.Web.LibraryManager.Cli` (si no está instalado)
   - `libman install bootstrap@5.3.3 --provider jsdelivr --destination Styles/vendor/bootstrap`
   - Si el proyecto ya usa otra versión 5.x de Bootstrap, usa esa misma para que el JS y el CSS coincidan.
3. **Crear `Styles/_custom.scss`** con el contenido exacto de la sección "Contenido de \_custom.scss" de este plan.
4. **Crear `Styles/site.scss`** con este contenido:

```scss
@import "custom";
@import "vendor/bootstrap/scss/bootstrap";

// Estilos propios del sitio (migrados desde wwwroot/css/site.css)
```

Después pega al final el contenido actual de `wwwroot/css/site.css`, quitando reglas que repitan lo que ya hacen las variables (fuentes, colores de botones).

5. **Crear `sasscompiler.json`** en la raíz del proyecto:

```json
{
  "Source": "Styles",
  "Target": "wwwroot/css",
  "Arguments": "--style=compressed --quiet-deps"
}
```

Versiones anteriores del paquete usan `SourceFolder` y `TargetFolder`. Revisa la versión instalada en el `.csproj` y el README de `koenvzeijl/AspNetCore.SassCompiler` si no compila.

6. **Activar el watcher en desarrollo.** En `Program.cs`, antes de `builder.Build()`:

```csharp
#if DEBUG
builder.Services.AddSassCompiler();
#endif
```

7. **Actualizar el layout** (`Views/Shared/_Layout.cshtml` o `Pages/Shared/_Layout.cshtml`): elimina el `<link>` a `bootstrap.min.css` y deja en el `<head>`:

```html
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Bungee&family=Atkinson+Hyperlegible:wght@400;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
```

8. **Agregar `wwwroot/css/site.css` al `.gitignore`.** El paquete lo regenera al hacer publish.
9. **Compilar y verificar** con `dotnet build` y los criterios de aceptación al final de este plan.

## Contenido de \_custom.scss

Copia este archivo tal cual en `Styles/_custom.scss`. Los nombres de las variables siguen las de Bootstrap 5.3.

```scss
// --- Paleta ---------------------------------------------------
$rosa-mexicano:   #E4007C;  // base: botones, enlaces, marca
$rosa-profundo:   #B8005F;  // hover / texto rosa sobre fondos claros
$azul-lona:       #1F3FA6;  // lonas azules del tianguis: secundario
$cempasuchil:     #F7A800;  // avisos y precios (nunca texto sobre blanco)
$verde-nopal:     #2E7D4F;  // pago recibido / éxito
$tinta:           #2A1420;  // texto: ciruela muy oscuro, no negro plano
$papel:           #FFFFFF;
$rosa-papel:      #FFF0F7;  // fondos de sección suaves

$primary:   $rosa-mexicano;
$secondary: $azul-lona;
$success:   $verde-nopal;
$warning:   $cempasuchil;
$dark:      $tinta;
$light:     $rosa-papel;

$body-color: $tinta;
$body-bg:    $papel;
$link-color: $rosa-profundo;
$link-hover-color: $tinta;
$min-contrast-ratio: 4.5;

// --- Tipografía ----------------------------------------------
$font-family-sans-serif: "Atkinson Hyperlegible", system-ui, -apple-system, "Segoe UI", sans-serif;
$headings-font-family:   "Bungee", "Atkinson Hyperlegible", system-ui, sans-serif;
$headings-font-weight:   400;
$headings-line-height:   1.1;
$font-size-base:         1.0625rem;
$line-height-base:       1.55;

$h1-font-size: $font-size-base * 2.75;
$h2-font-size: $font-size-base * 2;
$h3-font-size: $font-size-base * 1.5;
$h4-font-size: $font-size-base * 1.25;

// --- Forma ----------------------------------------------------
$border-radius:    .375rem;
$border-radius-sm: .25rem;
$border-radius-lg: .75rem;
$border-width:     2px;
$border-color:     rgba($tinta, .15);

// Sombras duras, como un letrero pegado
$box-shadow:    4px 4px 0 rgba($tinta, .9);
$box-shadow-sm: 2px 2px 0 rgba($tinta, .9);
$box-shadow-lg: 6px 6px 0 rgba($tinta, .9);
$enable-shadows: false;

// --- Botones y campos (uso con una mano, en el puesto) --------
$btn-padding-y:    .75rem;
$btn-padding-x:    1.5rem;
$btn-font-weight:  700;
$btn-padding-y-lg: 1rem;
$btn-padding-x-lg: 2rem;
$input-padding-y:  .75rem;

// --- Sin degradados decorativos -------------------------------
$enable-gradients: false;
```

## Criterios de aceptación

La tarea está terminada cuando se cumplen todos estos puntos. Reporta el resultado de cada uno.

- [ ] `dotnet build` termina sin errores y genera `wwwroot/css/site.css`.
- [ ] El layout ya no carga `bootstrap.min.css`; solo `~/css/site.css` y las fuentes de Google.
- [ ] Un `<button class="btn btn-primary">Cobrar $150</button>` se ve rosa mexicano (#E4007C), con texto blanco en Atkinson Hyperlegible y más alto que el botón estándar de Bootstrap.
- [ ] Los `h1`–`h4` usan Bungee.
- [ ] Los menús desplegables, modales y el colapso del navbar siguen funcionando (el JS de Bootstrap no se rompió).
- [ ] Al guardar un cambio en `Styles/_custom.scss` con la app corriendo en Debug, el CSS se regenera solo.
- [ ] Los estilos que había en el `site.css` original siguen aplicándose.
- [ ] No hay archivos modificados dentro de `Styles/vendor/`.

Si el botón sale azul, el navegador sigue cargando el CSS viejo o está en caché: revisa el layout y fuerza recarga.
