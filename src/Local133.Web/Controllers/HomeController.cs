using Local133.Web.Hosting;
using Local133.Web.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Local133.Web.Controllers;

/// <summary>Sitio público: portada y páginas de error.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
public class HomeController(IOptions<HomeHeroOptions> heroOptions, IOptions<PublicSiteOptions> site) : Controller
{
    /// <summary>
    /// Home sin sesión: el modelo son las palabras que rotan en el título (la primera la pinta el servidor). Con sesión
    /// de vendedor o admin redirige a su inicio, así que a «/» solo se llega antes del login o al cerrar sesión (y la PWA,
    /// que abre en «/», cae en la pantalla correcta). Con sesión pero sin rol se ve la home.
    /// </summary>
    [HttpGet("")]
    public IActionResult Index()
    {
        var home = PostLoginRedirect.HomeUrlFor(User);
        return home != "~/" ? LocalRedirect(home) : View(heroOptions.Value.GetWords());
    }

    /// <summary>
    /// Términos y condiciones: viven en la portada desde el 2026-11-25 (<c>App:LandingUrl</c>). La ruta vieja redirige
    /// para no romper ligas guardadas.
    /// </summary>
    [HttpGet("terminos")]
    public IActionResult Terms() => RedirectPermanent(site.Value.TermsUrl);

    /// <summary>Aviso de privacidad: en la portada, igual que <see cref="Terms"/>.</summary>
    [HttpGet("aviso-de-privacidad")]
    public IActionResult PrivacyNotice() => RedirectPermanent(site.Value.PrivacyNoticeUrl);

    /// <summary>Destino de <c>UseExceptionHandler("/error")</c> para peticiones fuera de /api.</summary>
    [Route("error")]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View();
    }

    /// <summary>Destino de <c>UseStatusCodePagesWithReExecute("/error/{0}")</c>.</summary>
    /// <remarks>
    /// El código se lee de la ruta y no por model binding: al re-ejecutar un POST cuyo formulario no se puede leer (p. ej.
    /// una foto que pasa el límite de la petición) MVC intenta leer el formulario, el enlazado falla y quedaría
    /// <c>code = 0</c> (⇒ excepción y 500).
    /// </remarks>
    [Route("error/{code:int:range(400,599)}")]
    public IActionResult StatusCodePage()
    {
        var code = int.Parse((string)RouteData.Values["code"]!, System.Globalization.CultureInfo.InvariantCulture);
        Response.StatusCode = code;
        return code == StatusCodes.Status404NotFound ? View("NotFound") : View("Error");
    }
}
