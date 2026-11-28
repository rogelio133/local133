using Local133.Domain.Merchants;
using Local133.Web.PublicPages;

namespace Local133.Web.Hosting;

/// <summary>
/// Datos públicos del sitio (sección <c>App</c>). <see cref="PublicBaseUrl"/> fija el dominio de las ligas absolutas del
/// portal (regreso de Pey, imágenes del recibo y del catálogo que pide la portada); sin él se usa el esquema y el host
/// de la petición. El recibo y el catálogo públicos viven en la portada (<see cref="LandingUrl"/>) desde el 2026-11-27.
/// </summary>
public sealed class PublicSiteOptions
{
    public const string SectionName = "App";

    /// <summary>URL base pública, p. ej. <c>https://local133.mx</c> (sin diagonal final). Opcional.</summary>
    public string? PublicBaseUrl { get; set; }

    public const string DefaultLandingUrl = "https://local133.com.mx";

    /// <summary>
    /// Portada pública (<c>Local133.LandingPage</c>), sin diagonal final. Ahí viven el recibo y el catálogo públicos
    /// (2026-11-27) y los textos legales; también la invitación con el código del vendedor.
    /// </summary>
    public string LandingUrl { get; set; } = DefaultLandingUrl;

    /// <summary>
    /// Portada con el código de invitación del vendedor: <c>{LandingUrl}/?codigo=ABC234</c>. Su botón «Empieza a cobrar»
    /// lleva a <c>/r/{código}</c> (registro con el código).
    /// </summary>
    public string LandingUrlFor(string referralCode) => $"{Landing}/?codigo={Uri.EscapeDataString(referralCode)}";

    /// <summary>Rutas de los textos legales en la portada (<c>Local133.LandingPage/Controllers/LegalController</c>).</summary>
    public const string TermsPath = "/terminos";
    public const string PrivacyNoticePath = "/aviso-de-privacidad";

    /// <summary>
    /// Términos y condiciones, en la portada desde el 2026-11-25. A ella van el pie del portal, la casilla del registro
    /// y la ruta vieja <c>/terminos</c> del portal (301).
    /// </summary>
    public string TermsUrl => Landing + TermsPath;

    /// <summary>Aviso de privacidad, en la portada (igual que <see cref="TermsUrl"/>).</summary>
    public string PrivacyNoticeUrl => Landing + PrivacyNoticePath;

    /// <summary>
    /// Recibo público, en la portada desde el 2026-11-27 (<c>{LandingUrl}/recibo/{token}</c>). Es la liga que se comparte
    /// por WhatsApp; la ruta vieja <c>/recibo/{token}</c> del portal redirige aquí (301).
    /// </summary>
    public string ReceiptUrlFor(string token) => Landing + PublicPagesApi.ReceiptPathPrefix + token;

    /// <summary>Base de la liga del catálogo público en la portada (<c>{LandingUrl}/tienda/</c>, 2026-11-27).</summary>
    public string StorefrontUrlPrefix => Landing + PublicSlug.PathPrefix;

    /// <summary>Catálogo público en la portada; la ruta vieja <c>/tienda/{slug}</c> del portal redirige aquí (301).</summary>
    public string StorefrontUrlFor(string slug) => Landing + PublicSlug.PathFor(slug);

    /// <summary><see cref="LandingUrl"/> sin espacios ni diagonal final; la de producción si viene vacía.</summary>
    private string Landing =>
        string.IsNullOrWhiteSpace(LandingUrl) ? DefaultLandingUrl : LandingUrl.Trim().TrimEnd('/');

    /// <summary>
    /// Base absoluta de las ligas que salen del sitio (sin diagonal final): <see cref="PublicBaseUrl"/> si está
    /// configurada; si no, el esquema (ya corregido por los forwarded headers) y el host de la petición.
    /// </summary>
    public string BaseUrlFor(HttpRequest request)
    {
        var configured = PublicBaseUrl?.Trim().TrimEnd('/');
        return string.IsNullOrEmpty(configured)
            ? $"{request.Scheme}://{request.Host}{request.PathBase}"
            : configured;
    }
}
