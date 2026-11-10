using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>Portal del vendedor (/app). Los guards por rol llegan con Identity (paso 1.2).</summary>
[Area("Merchant")]
[Route("app")]
[ApiExplorerSettings(IgnoreApi = true)]
public class HomeController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("cobrar")]
    public IActionResult Charge() => View();

    [HttpGet("ventas")]
    public IActionResult Sales() => View();

    [HttpGet("mas")]
    public IActionResult More() => View();
}
