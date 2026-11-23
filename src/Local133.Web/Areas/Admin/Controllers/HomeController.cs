using Local133.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Areas.Admin.Controllers;

/// <summary>Portal administrador (/admin): solo rol Admin.</summary>
[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
[Route("admin")]
[ApiExplorerSettings(IgnoreApi = true)]
public class HomeController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("transacciones")]
    public IActionResult Transactions() => View();

    [HttpGet("parametros")]
    public IActionResult Settings() => View();
}
