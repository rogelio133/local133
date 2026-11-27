using Local133.Application.Auth;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>
/// «Mi plan» (/app/plan, 5.6): estado del plan, paquetes para pagar o adelantar Pro
/// con el link de pago de Pey, pagos anteriores y lo que incluye cada plan. El regreso de Pey
/// (<c>/app/plan/pago/{id}</c>) solo consulta: abonan el webhook o la consulta del servidor a Pey.
/// </summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/plan")]
[ApiExplorerSettings(IgnoreApi = true)]
public class PlanController(
    AppDbContext db,
    IPlanAccess plans,
    ISubscriptionCheckout checkout,
    ISubscriptionPaymentReconciliation reconciliation,
    IOptions<PublicSiteOptions> site,
    IWebHostEnvironment environment,
    TimeProvider clock) : Controller
{
    public const string NoticeKey = "PlanNotice";

    /// <summary>Freno de la consulta a Pey desde la confirmación y su polling.</summary>
    public static readonly TimeSpan StatusCheckMinInterval = TimeSpan.FromSeconds(15);

    /// <summary>Pagos que se muestran en «Mi plan».</summary>
    public const int PaymentHistorySize = 12;

    internal const string HttpsDevelopmentWarning =
        "Pey exige HTTPS para las URLs de regreso: arranca con el perfil https (https://localhost:7180) o configura App:PublicBaseUrl con la URL del túnel.";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        var now = clock.GetUtcNow();
        var today = MexicoTime.Today(now);

        var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);
        var offers = await checkout.GetOffersAsync(merchant.Id, cancellationToken);
        var charges = await db.SubscriptionCharges.AsNoTracking()
            .Include(c => c.Payments)
            .Where(c => c.MerchantId == merchant.Id
                && (c.Status == SubscriptionChargeStatus.Paid
                    || c.Status == SubscriptionChargeStatus.Processing
                    || c.Status == SubscriptionChargeStatus.Partial))
            .OrderByDescending(c => c.PaidAt ?? c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync(cancellationToken);

        // Aviso de que toca renovar (5.7). Sin link: el vendedor elige su paquete y ahí se genera.
        var last = await db.Subscriptions.AsNoTracking()
            .Where(s => s.MerchantId == merchant.Id)
            .Select(s => new { s.LastPeriodKind, s.LastPackage })
            .SingleOrDefaultAsync(cancellationToken);
        var renewal = last is not null && offers.CanPay
            ? RenewalNoticeViewModel.From(
                SubscriptionReminders.RenewalNoticeFor(plan.Status, last.LastPeriodKind, last.LastPackage, now), today)
            : null;

        return View(new MyPlanViewModel(
            PlanSummaryViewModel.From(plan.Status, now),
            plan.TrialDays,
            MyPlanViewModel.FeaturesFor(),
            offers.CanPay,
            merchant.Status == MerchantStatus.Disabled,
            offers.Offers.Select(o => PlanOfferViewModel.From(o, today, last?.LastPackage)).ToList(),
            FounderNoticeFor(offers, settings),
            charges.Where(c => c.Status != SubscriptionChargeStatus.Paid).Select(c => SubscriptionChargeRow.From(c, today)).ToList(),
            charges.Where(c => c.Status == SubscriptionChargeStatus.Paid).Take(PaymentHistorySize)
                .Select(c => SubscriptionChargeRow.From(c, today)).ToList(),
            offers.CanPay && environment.IsDevelopment() && !IsHttps(site.Value.BaseUrlFor(Request)) ? HttpsDevelopmentWarning : null,
            renewal,
            offers.CanPay ? MyPlanViewModel.WelcomeDiscountNoticeFor(offers.WelcomeDiscount) : null));
    }

    /// <summary>Crea (o reutiliza) el link del paquete y manda a la página de pago de Pey.</summary>
    [HttpPost("pagar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay([FromForm(Name = "package")] string? package, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SubscriptionPackage>(package, ignoreCase: false, out var chosen) || !Enum.IsDefined(chosen))
        {
            TempData[NoticeKey] = "Elige un paquete para pagar.";
            return Redirect(Url.Content("~" + PlanUrls.Plan));
        }

        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var baseUrl = new Uri(site.Value.BaseUrlFor(Request));
        var result = await checkout.StartAsync(new StartCheckoutCommand(merchant.Id, chosen, baseUrl), cancellationToken);
        switch (result)
        {
            case CheckoutResult.Redirect redirect:
                return Redirect(redirect.Url.AbsoluteUri);
            case CheckoutResult.NotAllowed notAllowed:
                TempData[NoticeKey] = notAllowed.Message;
                break;
            case CheckoutResult.HttpsRequired:
                TempData[NoticeKey] = environment.IsDevelopment()
                    ? HttpsDevelopmentWarning
                    : "No pudimos generar el link de pago. Intenta de nuevo en unos minutos.";
                break;
            case CheckoutResult.GatewayFailed failed:
                TempData[NoticeKey] = failed.Message;
                break;
            default:
                throw new InvalidOperationException($"Resultado inesperado: {result}");
        }

        return Redirect(Url.Content("~" + PlanUrls.Plan));
    }

    /// <summary>
    /// Regreso de Pey: muestra el estado del cargo (consulta a Pey con freno) y la vista sigue consultando cada 3 s.
    /// El cargo de otro vendedor da 404 (filtro multi-tenant).
    /// </summary>
    [HttpGet("pago/{id:int}")]
    public async Task<IActionResult> Payment(int id, CancellationToken cancellationToken)
    {
        var charge = await FindChargeAsync(id, cancellationToken);
        if (charge is null)
        {
            return NotFound();
        }

        if (charge.Status is not (SubscriptionChargeStatus.Paid or SubscriptionChargeStatus.Failed))
        {
            await reconciliation.ReconcileChargeAsync(charge.Id, StatusCheckMinInterval, cancellationToken);
            charge = await FindChargeAsync(id, cancellationToken) ?? charge;
        }

        var now = clock.GetUtcNow();
        string? proUntil = null;
        if (charge.Status == SubscriptionChargeStatus.Paid)
        {
            var until = await db.Subscriptions.AsNoTracking()
                .Where(s => s.MerchantId == charge.MerchantId)
                .Select(s => (DateTimeOffset?)s.ProUntil)
                .SingleOrDefaultAsync(cancellationToken);
            proUntil = SubscriptionPaymentViewModel.ProUntilTextFor(until, now);
        }

        return View(new SubscriptionPaymentViewModel(
            charge.Id,
            charge.Package.Label(),
            SaleQrViewModel.FormatAmount(charge.Amount),
            charge.Status,
            proUntil,
            charge.Status == SubscriptionChargeStatus.Pending ? charge.Url : null));
    }

    private Task<SubscriptionCharge?> FindChargeAsync(int id, CancellationToken cancellationToken) =>
        db.SubscriptionCharges.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    private static string? FounderNoticeFor(SubscriptionOffersResult offers, SystemSettings settings)
    {
        if (offers.IsFounder)
        {
            return "Eres fundador: conservas el precio de fundador de por vida.";
        }

        if (!offers.CanPay || offers.FounderSlotsLeft <= 0)
        {
            return null;
        }

        var slots = offers.FounderSlotsLeft == 1 ? "queda 1 lugar" : $"quedan {offers.FounderSlotsLeft:N0} lugares";
        return $"Precio de fundador para los primeros {settings.FounderSlots:N0} que paguen, de por vida: {slots}.";
    }

    private static bool IsHttps(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
