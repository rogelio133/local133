using Local133.Application.Auth;
using Local133.Application.Subscriptions;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Areas.Merchant.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>Portal del vendedor (/app): solo rol Merchant.</summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app")]
[ApiExplorerSettings(IgnoreApi = true)]
public class HomeController(AppDbContext db, IPlanAccess plans, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var now = clock.GetUtcNow();
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        // Si Pey rechazó la cuenta, el Inicio muestra el motivo junto al paso de la CLABE.
        var rejection = await db.BankAccounts
            .Where(b => b.MerchantId == merchant.Id && b.CodiStatus == PeyCodiStatus.Error)
            .Select(b => new { b.ErrorMessage })
            .SingleOrDefaultAsync(cancellationToken);

        // Resumen del día: solo cuando ya pudo cobrar (en el onboarding aún no hay ventas).
        var summary = merchant.Status is MerchantStatus.Active or MerchantStatus.Suspended or MerchantStatus.Disabled
            ? await DaySummaryQuery.BuildAsync(db, merchant.Id, now, cancellationToken)
            : null;

        return View(MerchantHomeViewModel.From(merchant, User.Identity?.Name,
            rejection is null ? null : rejection.ErrorMessage ?? MerchantHomeViewModel.DefaultRejection, summary,
            PlanSummaryViewModel.From(plan.Status, now), plan.TrialDays));
    }

    [HttpGet("mas")]
    public IActionResult More() => View();
}
