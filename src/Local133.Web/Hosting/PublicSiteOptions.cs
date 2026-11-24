namespace Local133.Web.Hosting;

/// <summary>
/// Datos públicos del sitio (sección <c>App</c>). <see cref="PublicBaseUrl"/> fija el dominio de las ligas que salen
/// del sitio (recibos compartidos por WhatsApp); sin él se usa el esquema y el host de la petición.
/// </summary>
public sealed class PublicSiteOptions
{
    public const string SectionName = "App";

    /// <summary>URL base pública, p. ej. <c>https://local133.mx</c> (sin diagonal final). Opcional.</summary>
    public string? PublicBaseUrl { get; set; }

    public const string DefaultLandingUrl = "https://local133.com.mx";

    /// <summary>
    /// Portada pública (<c>Local133.LandingPage</c>), sin diagonal final. A ella apuntan «Cobrado con Local133» del
    /// recibo gratuito y el pie del catálogo público (5.10), con el código de invitación del vendedor.
    /// </summary>
    public string LandingUrl { get; set; } = DefaultLandingUrl;

    /// <summary>
    /// Portada con el código de invitación del vendedor: <c>{LandingUrl}/?codigo=ABC234</c>. Su botón «Empieza a cobrar»
    /// lleva a <c>/r/{código}</c> (registro con el código).
    /// </summary>
    public string LandingUrlFor(string referralCode)
    {
        var landing = string.IsNullOrWhiteSpace(LandingUrl) ? DefaultLandingUrl : LandingUrl.Trim().TrimEnd('/');
        return $"{landing}/?codigo={Uri.EscapeDataString(referralCode)}";
    }

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
