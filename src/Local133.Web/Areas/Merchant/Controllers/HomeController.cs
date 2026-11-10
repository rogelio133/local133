using Local133.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>Portal del vendedor (/app): solo rol Merchant.</summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
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
