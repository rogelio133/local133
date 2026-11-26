using Local133.LandingPage.Hosting;
using Local133.LandingPage.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Local133.LandingPage.Controllers;

/// <summary>
/// Términos y condiciones y Aviso de privacidad (5.11; se aceptan al registrarse en el portal). Viven en la portada desde
/// el 2026-11-25; el portal redirige sus rutas viejas aquí (<c>App:LandingUrl</c>). La versión sale de
/// <c>Local133.Domain/Legal/LegalDocuments.cs</c> (enlazado).
/// </summary>
public class LegalController(IOptions<LandingOptions> landingOptions) : Controller
{
    public const string TermsPath = "/terminos";
    public const string PrivacyPath = "/aviso-de-privacidad";

    [HttpGet("terminos")]
    public IActionResult Terms() => View(Chrome());

    [HttpGet("aviso-de-privacidad")]
    public IActionResult PrivacyNotice() => View(Chrome());

    /// <summary>Encabezado y pie sin código de invitación: las ligas legales nunca lo llevan.</summary>
    private SiteChromeViewModel Chrome()
    {
        var appUrl = landingOptions.Value.AppUrl;
        return SiteChromeViewModel.For(appUrl, appUrl, homeUrl: "/");
    }
}
