using Local133.Domain.Merchants;
using Local133.LandingPage.Hosting;
using Local133.LandingPage.Models;
using Local133.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Local133.LandingPage.Controllers;

/// <summary>Portada pública y página de error.</summary>
public class HomeController(IOptions<HomeHeroOptions> heroOptions, IOptions<LandingOptions> landingOptions) : Controller
{
    /// <summary>Página del portal (Local133.Web) a la que lleva «Iniciar sesión».</summary>
    public const string LoginPath = "/Identity/Account/Login";

    /// <param name="codigo">
    /// Código de invitación (5.10: «Cobrado con Local133» del recibo y pie del catálogo público). Si tiene la forma de
    /// un código, los botones de registro van a <c>{AppUrl}/r/{código}</c>, que lo guarda y lleva al registro; si no
    /// existe, el portal manda al registro sin código. «Iniciar sesión» y las ligas legales (<see cref="LegalController"/>)
    /// nunca lo llevan.
    /// </param>
    [HttpGet("")]
    public IActionResult Index(string? codigo)
    {
        var appUrl = landingOptions.Value.AppUrl;
        var signUpUrl = SignUpUrl(appUrl, codigo);
        return View(new LandingViewModel(
            heroOptions.Value.GetWords(),
            signUpUrl,
            SiteChromeViewModel.For(appUrl, signUpUrl, homeUrl: "")));
    }

    /// <summary>Destino de los botones de registro: el portal, o su liga de invitación si llegó un código válido.</summary>
    public static string SignUpUrl(string appUrl, string? referralCode)
    {
        var code = ReferralCode.Normalize(referralCode);
        return ReferralCode.IsValid(code) ? PortalUrl(appUrl, ReferralCode.PathFor(code)) : appUrl;
    }

    /// <summary>Una página del portal (<paramref name="path"/> empieza con <c>/</c>).</summary>
    public static string PortalUrl(string appUrl, string path) => appUrl.TrimEnd('/') + path;

    /// <summary>Destino de <c>UseExceptionHandler("/error")</c>.</summary>
    [Route("error")]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View();
    }
}
