using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Local133.Infrastructure.Email;

/// <summary>Contenido de un correo con el layout de la marca. Todos los textos van sin HTML: se codifican al armarlo.</summary>
/// <param name="Subject">Asunto (también es el <c>&lt;title&gt;</c> del documento).</param>
/// <param name="Preheader">Resumen que muestran los clientes junto al asunto; no se ve dentro del correo.</param>
/// <param name="Title">Título grande (Bungee).</param>
/// <param name="Paragraphs">Párrafos del cuerpo, en orden.</param>
/// <param name="ActionText">Texto del botón.</param>
/// <param name="ActionUrl">Liga absoluta del botón. Es la primera liga del correo.</param>
/// <param name="Footer">Nota en gris debajo del botón (p. ej. «Si no creaste una cuenta, ignora este mensaje.»).</param>
public sealed record EmailContent(
    string Subject,
    string Preheader,
    string Title,
    IReadOnlyList<string> Paragraphs,
    string ActionText,
    string ActionUrl,
    string? Footer);

/// <summary>
/// Layout HTML de todos los correos: encabezado rosa mexicano con el logo, título en Bungee, texto en Atkinson
/// Hyperlegible y botón rosa. Lo usan los correos de la cuenta (Identity) y los avisos que salen de jobs y webhooks.
/// </summary>
/// <remarks>
/// <para>Reglas de correo, no de página web: tablas con estilos en línea, ancho máximo de 600 px, logo en PNG con URL
/// absoluta (Gmail, Outlook y Yahoo no muestran SVG ni <c>data:</c> URIs) y fuentes con respaldo (Gmail y Outlook
/// ignoran las fuentes web; el logo ya trae «Local133» en Bungee como imagen).</para>
/// <para>El botón debe ser la <b>primera</b> liga (<c>href</c>) del documento: <c>FileEmailSender</c> la escribe en el
/// log y las pruebas la leen así. Por eso las fuentes se cargan con <c>@import</c> y no con <c>&lt;link href&gt;</c>, y el
/// logo no lleva liga.</para>
/// </remarks>
public static class EmailLayout
{
    public const string Pink = "#E4007C";      // $rosa-mexicano
    public const string DeepPink = "#B8005F";  // $rosa-profundo
    public const string Ink = "#2A1420";       // $tinta
    public const string PaperPink = "#FFF0F7"; // $rosa-papel
    public const string Muted = "#6B5560";     // tinta aclarada (≈ 6.9:1 sobre blanco)

    /// <summary>
    /// Logo para correo (<c>tools/brand-assets.ps1</c>): el cuadro rosa del toldo a 2x (192 px, se muestra a 96), con las
    /// esquinas redondeadas en el PNG (no todos los clientes respetan <c>border-radius</c>) y transparente fuera.
    /// </summary>
    public const string LogoPath = "/img/email/local133-email-logo.png";

    /// <summary>Sube al cambiar el PNG: Gmail guarda en caché las imágenes por URL.</summary>
    public const string LogoVersion = "2";

    public const int LogoWidth = 96;
    public const int LogoHeight = 96;

    private const string BodyFont = "'Atkinson Hyperlegible', Arial, Helvetica, sans-serif";
    private const string HeadingFont = "'Bungee', 'Arial Black', Arial, Helvetica, sans-serif";

    // Codifica lo que importa en HTML (< > & " ') y deja legibles los acentos y la ñ (el documento va en UTF-8).
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <param name="siteBaseUrl">Base absoluta del sitio (p. ej. <c>https://local133.com.mx</c>), para el logo y el pie.</param>
    public static string Render(EmailContent content, string siteBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteBaseUrl);
        var site = siteBaseUrl.Trim().TrimEnd('/');
        var siteHost = Uri.TryCreate(site, UriKind.Absolute, out var siteUri) ? siteUri.Authority : site;
        var logo = $"{site}{LogoPath}?v={LogoVersion}";
        var action = E(content.ActionUrl);

        var paragraphs = new StringBuilder();
        foreach (var paragraph in content.Paragraphs)
        {
            paragraphs.Append($"""<p style="margin:0 0 16px;">{E(paragraph)}</p>""").Append('\n');
        }

        var footer = string.IsNullOrWhiteSpace(content.Footer)
            ? ""
            : $"""<p style="margin:24px 0 0;font-size:14px;line-height:1.5;color:{Muted};">{E(content.Footer)}</p>""";

        return $$"""
            <!DOCTYPE html>
            <html lang="es-MX">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light">
            <meta name="supported-color-schemes" content="light">
            <title>{{E(content.Subject)}}</title>
            <style>
            @import url('https://fonts.googleapis.com/css2?family=Atkinson+Hyperlegible:wght@400;700&family=Bungee&display=swap');
            body { margin: 0; padding: 0; }
            @media (max-width: 620px) {
              .email-pad { padding: 24px 20px !important; }
              .email-head { padding: 16px 20px !important; }
              .email-title { font-size: 26px !important; }
            }
            </style>
            </head>
            <body style="margin:0;padding:0;background-color:{{PaperPink}};">
            <div style="display:none;max-height:0;max-width:0;overflow:hidden;opacity:0;mso-hide:all;">{{E(content.Preheader)}}&#8199;&#847;&#8199;&#847;&#8199;&#847;&#8199;&#847;&#8199;&#847;&#8199;&#847;</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:{{PaperPink}};">
            <tr><td align="center" style="padding:24px 12px;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:600px;background-color:#FFFFFF;border:2px solid {{Ink}};border-radius:12px;border-collapse:separate;box-shadow:4px 4px 0 {{Ink}};">
                <tr><td class="email-head" align="left" bgcolor="{{Pink}}" style="background-color:{{Pink}};padding:20px 32px;border-radius:10px 10px 0 0;">
                  <img src="{{E(logo)}}" width="{{LogoWidth}}" height="{{LogoHeight}}" alt="Local133" style="display:block;border:0;outline:none;text-decoration:none;color:#FFFFFF;font-family:{{HeadingFont}};font-size:26px;line-height:{{LogoHeight}}px;">
                </td></tr>
                <tr><td class="email-pad" style="padding:32px;font-family:{{BodyFont}};font-size:17px;line-height:1.55;color:{{Ink}};">
                  <h1 class="email-title" style="margin:0 0 20px;font-family:{{HeadingFont}};font-weight:400;font-size:30px;line-height:1.15;color:{{Ink}};">{{E(content.Title)}}</h1>
                  {{paragraphs}}
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:8px 0 24px;">
                    <tr><td align="center" bgcolor="{{Pink}}" style="background-color:{{Pink}};border-radius:6px;border-bottom:4px solid {{DeepPink}};">
                      <a href="{{action}}" target="_blank" style="display:inline-block;padding:14px 28px;font-family:{{BodyFont}};font-size:17px;font-weight:700;line-height:1.2;color:#FFFFFF;text-decoration:none;border-radius:6px;">{{E(content.ActionText)}}</a>
                    </td></tr>
                  </table>
                  <p style="margin:0 0 4px;font-size:14px;line-height:1.5;color:{{Muted}};">Si el botón no funciona, copia esta liga en tu navegador:</p>
                  <p style="margin:0;font-size:14px;line-height:1.5;word-break:break-all;"><a href="{{action}}" target="_blank" style="color:{{DeepPink}};">{{E(content.ActionUrl)}}</a></p>
                  {{footer}}
                </td></tr>
              </table>
              <p style="margin:20px 0 0;font-family:{{BodyFont}};font-size:13px;line-height:1.5;color:{{Muted}};">Local133 · Cobra con CoDi desde tu puesto · <a href="{{E(site)}}" target="_blank" style="color:{{DeepPink}};">{{E(siteHost)}}</a></p>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    private static string E(string value) => Encoder.Encode(value);
}
