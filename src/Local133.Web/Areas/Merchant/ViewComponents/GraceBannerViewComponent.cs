using Local133.Application.Subscriptions;
using Local133.Application.Tenancy;
using Local133.Domain.Common;
using Local133.Domain.Subscriptions;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Areas.Merchant.ViewComponents;

/// <summary>
/// Banner de gracia en el layout del vendedor (5.7): «Tu plan Pro venció el … Paga a más tardar el … para no perder
/// Pro» (o Básico, el plan que pagó), con «Renovar ahora» a «Mi plan» (ahí elige el paquete y se genera el link). Solo en <see cref="PlanTier.Grace"/>.
/// </summary>
public sealed class GraceBannerViewComponent(IPlanAccess plans, ITenantContext tenant, TimeProvider clock) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (tenant.MerchantId is not { } merchantId || merchantId <= 0)
        {
            return Content(string.Empty);
        }

        var plan = await plans.GetCurrentAsync(HttpContext.RequestAborted);
        if (plan.Tier != PlanTier.Grace || plan.Plan is not { } paidPlan
            || plan.Status.LastActiveDay is not { } lastDay || plan.Status.LastGraceDay is not { } graceDay)
        {
            return Content(string.Empty);
        }

        var today = MexicoTime.Today(clock.GetUtcNow());
        return View(new GraceBannerViewModel(
            paidPlan.Label(),
            SaleFormats.Date(lastDay, today),
            graceDay == today ? "hoy" : SaleFormats.Date(graceDay, today)));
    }
}
