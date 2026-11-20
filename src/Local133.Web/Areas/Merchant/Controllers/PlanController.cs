using Local133.Application.Auth;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Settings;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>
/// «Mi plan» (/app/plan), provisional desde la 5.5: estado del plan, contador del mes del gratuito y lo que incluye
/// Pro. Los avisos «Disponible con Pro» llevan aquí. El pago con link de Pey llega en la 5.6, que reemplaza esta vista.
/// </summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/plan")]
[ApiExplorerSettings(IgnoreApi = true)]
public class PlanController(AppDbContext db, IPlanAccess plans, IChargeUsage usage, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        var now = clock.GetUtcNow();
        var usageModel = merchant.Status == MerchantStatus.Active && plan.Entitlements.HasMonthlyChargeLimit
            ? ChargeUsageViewModel.From(await usage.GetMonthAsync(merchant.Id, cancellationToken), MexicoTime.Today(now))
            : null;

        var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);
        return View(new MyPlanViewModel(
            PlanSummaryViewModel.From(plan.Status, now), usageModel, plan.TrialDays, MyPlanViewModel.FeaturesFor(settings)));
    }
}
