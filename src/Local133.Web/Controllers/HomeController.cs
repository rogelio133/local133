using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Controllers;

/// <summary>Sitio público: portada y páginas de error.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
public class HomeController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    /// <summary>Términos y condiciones (públicos; se aceptan al registrarse).</summary>
    [HttpGet("terminos")]
    public IActionResult Terms() => View();

    /// <summary>Aviso de privacidad (público; se acepta al registrarse).</summary>
    [HttpGet("aviso-de-privacidad")]
    public IActionResult PrivacyNotice() => View();

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
