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
}
