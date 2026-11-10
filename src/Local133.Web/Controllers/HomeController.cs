using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Controllers;

/// <summary>Sitio público: portada y páginas de error.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
public class HomeController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    /// <summary>Destino de <c>UseExceptionHandler("/error")</c> para peticiones fuera de /api.</summary>
    [Route("error")]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View();
    }

    /// <summary>Destino de <c>UseStatusCodePagesWithReExecute("/error/{0}")</c>.</summary>
    [Route("error/{code:int}")]
    public IActionResult StatusCodePage(int code)
    {
        Response.StatusCode = code;
        return code == StatusCodes.Status404NotFound ? View("NotFound") : View("Error");
    }
}
