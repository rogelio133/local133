namespace Local133.LandingPage.Hosting;

/// <summary>Portada (sección <c>Landing</c>). No son secretos.</summary>
public sealed class LandingOptions
{
    public const string SectionName = "Landing";

    public const string DefaultAppUrl = "https://app.local133.com.mx";

    /// <summary>
    /// El portal: destino del botón «Empieza a cobrar» y de la API pública del recibo y del catálogo (2026-11-27). En
    /// desarrollo apunta a Local133.Web local.
    /// </summary>
    public string AppUrl { get; set; } = DefaultAppUrl;
}
