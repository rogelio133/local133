using Local133.LandingPage.Hosting;
using Local133.LandingPage.Models;
using Local133.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Local133.LandingPage.Controllers;

/// <summary>Portada pública y página de error.</summary>
public class HomeController(IOptions<HomeHeroOptions> heroOptions, IOptions<LandingOptions> landingOptions) : Controller
{
    [HttpGet("")]
    public IActionResult Index() =>
        View(new LandingViewModel(heroOptions.Value.GetWords(), landingOptions.Value.AppUrl));

    /// <summary>Destino de <c>UseExceptionHandler("/error")</c>.</summary>
    [Route("error")]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View();
    }
}
