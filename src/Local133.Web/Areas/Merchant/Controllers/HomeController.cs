using Local133.Application.Auth;
using Local133.Domain.Pey;
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
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        // Si Pey rechazó la cuenta, el Inicio muestra el motivo junto al paso de la CLABE.
        var rejection = await db.BankAccounts
            .Where(b => b.MerchantId == merchant.Id && b.CodiStatus == PeyCodiStatus.Error)
            .Select(b => new { b.ErrorMessage })
            .SingleOrDefaultAsync(cancellationToken);

        return View(MerchantHomeViewModel.From(merchant, User.Identity?.Name,
            rejection is null ? null : rejection.ErrorMessage ?? MerchantHomeViewModel.DefaultRejection));
    }

    [HttpGet("cobrar")]
    public IActionResult Charge() => View();

    [HttpGet("ventas")]
    public IActionResult Sales() => View();

    [HttpGet("mas")]
    public IActionResult More() => View();
}
