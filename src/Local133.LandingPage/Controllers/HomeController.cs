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
    /// <param name="codigo">
    /// Código de invitación (5.10: «Cobrado con Local133» del recibo y pie del catálogo público). Si tiene la forma de
    /// un código, «Empieza a cobrar» va a <c>{AppUrl}/r/{código}</c>, que lo guarda y lleva al registro; si no existe,
    /// el portal manda al registro sin código.
    /// </param>
    [HttpGet("")]
    public IActionResult Index(string? codigo) =>
        View(new LandingViewModel(heroOptions.Value.GetWords(), SignUpUrl(landingOptions.Value.AppUrl, codigo)));

    /// <summary>Destino de «Empieza a cobrar»: el portal, o su liga de invitación si llegó un código válido.</summary>
    public static string SignUpUrl(string appUrl, string? referralCode)
    {
        var code = ReferralCode.Normalize(referralCode);
        return ReferralCode.IsValid(code) ? appUrl.TrimEnd('/') + ReferralCode.PathFor(code) : appUrl;
    }

    /// <summary>Destino de <c>UseExceptionHandler("/error")</c>.</summary>
    [Route("error")]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View();
    }
}
