using Local133.Application.Subscriptions;
using Local133.Application.Tenancy;
using Local133.Domain.Common;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Areas.Merchant.Services;
using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Areas.Merchant.ViewComponents;

/// <summary>
/// Banner de gracia en el layout del vendedor (5.7): «Tu plan Pro venció el … Paga antes del … para no perder Pro»,
/// con el link de renovación de Pey o, si no hay, la liga a «Mi plan». Solo en <see cref="PlanTier.Grace"/>.
/// </summary>
public sealed class GraceBannerViewComponent(
    IPlanAccess plans, ITenantContext tenant, AppDbContext db, TimeProvider clock) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (tenant.MerchantId is not { } merchantId || merchantId <= 0)
        {
            return Content(string.Empty);
        }

        var plan = await plans.GetCurrentAsync(HttpContext.RequestAborted);
        if (plan.Tier != PlanTier.Grace || plan.Status.LastProDay is not { } lastDay || plan.Status.LastGraceDay is not { } graceDay)
        {
            return Content(string.Empty);
        }

        var today = MexicoTime.Today(clock.GetUtcNow());
        var link = await RenewalLinkQuery.FindAsync(db, merchantId, HttpContext.RequestAborted);
        return View(new GraceBannerViewModel(
            SaleFormats.Date(lastDay, today),
            graceDay == today ? "hoy" : SaleFormats.Date(graceDay, today),
            link?.Url ?? Url.Content("~" + PlanUrls.Plan),
            link is not null));
    }
}
