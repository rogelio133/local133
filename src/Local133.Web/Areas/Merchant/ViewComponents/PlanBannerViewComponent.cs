using Local133.Application.Subscriptions;
using Local133.Application.Tenancy;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.ViewComponents;

/// <summary>
/// Banner del plan en el layout del vendedor (5.7, 5.14), salvo en «Mi plan» (lo decide el layout):
/// <list type="bullet">
/// <item>gracia: «Tu plan Pro venció el … Paga a más tardar el … para no perder Pro» (o Básico, el plan que pagó), con
/// «Renovar ahora» a «Mi plan»;</item>
/// <item>vencido: «Tu plan venció. Elige un plan para seguir cobrando», con «Elegir plan». No sale en Inicio (sí en
/// «Más») ni en Cobrar, que ya muestran el aviso completo, ni a un vendedor dado de baja.</item>
/// </list>
/// Ningún botón crea un link de pago: el link nace cuando el vendedor elige su paquete.
/// </summary>
public sealed class PlanBannerViewComponent(AppDbContext db, IPlanAccess plans, ITenantContext tenant, TimeProvider clock)
    : ViewComponent
{

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (tenant.MerchantId is not { } merchantId || merchantId <= 0)
        {
            return Content(string.Empty);
        }

        var plan = await plans.GetCurrentAsync(HttpContext.RequestAborted);
        var today = MexicoTime.Today(clock.GetUtcNow());
        if (plan.Tier == PlanTier.Grace && plan.Plan is { } paidPlan
            && plan.Status.LastActiveDay is { } lastDay && plan.Status.LastGraceDay is { } graceDay)
        {
            return View(PlanBannerViewModel.Grace(
                paidPlan.Label(),
                SaleFormats.Date(lastDay, today),
                graceDay == today ? "hoy" : SaleFormats.Date(graceDay, today)));
        }

        if (plan.Tier != PlanTier.Expired || ShowsExpiredNotice())
        {
            return Content(string.Empty);
        }

        // El filtro multi-tenant ya limita al vendedor de la sesión.
        var status = await db.Merchants.AsNoTracking()
            .Where(m => m.Id == merchantId)
            .Select(m => (MerchantStatus?)m.Status)
            .SingleOrDefaultAsync(HttpContext.RequestAborted);
        return status is null or MerchantStatus.Disabled ? Content(string.Empty) : View(PlanBannerViewModel.Expired());
    }

    /// <summary>Inicio y Cobrar (monto y carrito) ya muestran el aviso completo del vencido.</summary>
    private bool ShowsExpiredNotice()
    {
        var values = ViewContext.RouteData.Values;
        return (values["controller"] as string) switch
        {
            "Charge" => true,
            "Home" => values["action"] as string == "Index",
            _ => false,
        };
    }
}
