using System.Security.Claims;
using Local133.Application.Auth;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>Portal del vendedor (/app): solo rol Merchant.</summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app")]
[ApiExplorerSettings(IgnoreApi = true)]
public class HomeController(AppDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        // Todo usuario Merchant tiene su Merchant (AccountRegistration y el backfill de la migración Merchants).
        var merchant = await db.Merchants.AsNoTracking().SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException($"El usuario {userId} tiene rol Merchant pero no tiene Merchant.");

        return View(MerchantHomeViewModel.From(merchant, User.Identity?.Name));
    }

    [HttpGet("cobrar")]
    public IActionResult Charge() => View();

    [HttpGet("ventas")]
    public IActionResult Sales() => View();

    [HttpGet("mas")]
    public IActionResult More() => View();
}
