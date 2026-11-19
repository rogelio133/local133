namespace Local133.Infrastructure.Email;

/// <summary>
/// Base de las ligas de los correos que salen sin petición HTTP (webhooks y jobs). Lee la misma llave que el
/// <c>PublicSiteOptions</c> del sitio: <c>App:PublicBaseUrl</c>.
/// </summary>
public sealed class SiteUrlOptions
{
    public const string SectionName = "App";

    /// <summary>Respaldo en Development cuando falta <see cref="PublicBaseUrl"/> (perfil <c>http</c> de launchSettings).</summary>
    public const string DevelopmentFallbackBaseUrl = "http://localhost:5231";

    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Base absoluta sin diagonal final: <see cref="PublicBaseUrl"/>; si falta, el respaldo en Development, y
    /// fuera de él <c>null</c> (no se puede armar la liga).
    /// </summary>
    public string? BaseUrl(bool isDevelopment)
    {
        var configured = PublicBaseUrl?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        return isDevelopment ? DevelopmentFallbackBaseUrl : null;
    }
}
