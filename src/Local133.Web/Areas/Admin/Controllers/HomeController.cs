using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Areas.Admin.Controllers;

/// <summary>Portal administrador (/admin). Los guards por rol llegan con Identity (paso 1.2).</summary>
[Area("Admin")]
[Route("admin")]
[ApiExplorerSettings(IgnoreApi = true)]
public class HomeController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("clientes")]
    public IActionResult Customers() => View();

    [HttpGet("transacciones")]
    public IActionResult Transactions() => View();

    [HttpGet("parametros")]
    public IActionResult Settings() => View();
}
