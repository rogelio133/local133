using Local133.Application.Auth;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Legal;
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
/// «Mi plan» y el checkout por pasos (5.6, 5.15, §6 del diseño de planes Básico y Pro):
/// <list type="bullet">
/// <item><c>/app/plan</c>: estado, lo que sigue, botones a la sección de su plan en el checkout, «Tus pedidos» y lo
/// que incluye cada plan;</item>
/// <item><c>/app/plan/checkout</c> → <c>/{plan}/{paquete}</c>: plan y duración (Básico y Pro, 1 mes y Anual, en una
/// página) y resumen del pedido. El estado va en la URL y <b>ningún GET llama a Pey ni crea cargos</b>;</item>
/// <item><c>POST /app/plan/checkout</c>: con la casilla de los Términos y el total que vio el vendedor, crea (o
/// reutiliza) el link y manda a Pey (paso 3);</item>
/// <item><c>/app/plan/pago/{id}</c>: la confirmación (regreso de Pey); solo consulta, abonan el webhook o la consulta
/// del servidor a Pey;</item>
/// <item><c>/app/plan/pedidos/{id}/comprobante</c>: el comprobante de un pedido pagado.</item>
/// </list>
/// El layout no pinta el banner del plan en este controlador.
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

    /// <summary>Pedidos que se muestran en «Mi plan».</summary>
    public const int OrderHistorySize = 12;

    internal const string HttpsDevelopmentWarning =
        "Pey exige HTTPS para las URLs de regreso: arranca con el perfil https (https://localhost:7180) o configura App:PublicBaseUrl con la URL del túnel.";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        var now = clock.GetUtcNow();
        var today = MexicoTime.Today(now);

        // Solo pagados, en proceso o en revisión: un link que el vendedor abrió y no pagó no es un «pago pendiente» (5.7).
        var orders = await db.SubscriptionCharges.AsNoTracking()
            .Include(c => c.Payments)
            .Where(c => c.MerchantId == merchant.Id
                && (c.Status == SubscriptionChargeStatus.Paid
                    || c.Status == SubscriptionChargeStatus.Processing
                    || c.Status == SubscriptionChargeStatus.Partial))
            .OrderByDescending(c => c.PaidAt ?? c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(OrderHistorySize)
            .ToListAsync(cancellationToken);

        var subscription = await db.Subscriptions.AsNoTracking()
            .Where(s => s.MerchantId == merchant.Id)
            .Select(s => new { s.LastPeriodKind, s.LastPackage })
            .SingleOrDefaultAsync(cancellationToken);
        var canPay = subscription is not null && merchant.Status != MerchantStatus.Disabled;

        // Aviso de que toca renovar (5.7), con el nombre del plan que vence. Sin botón ni link: abajo va «Renovar mi plan …».
        var actionPlan = MyPlanViewModel.ActionPlanFor(plan);
        var renewal = canPay
            ? RenewalNoticeViewModel.From(
                SubscriptionReminders.RenewalNoticeFor(plan.Status, subscription!.LastPeriodKind, subscription.LastPackage, now),
                actionPlan,
                today)
            : null;

        // Los botones van a la sección de su plan en el checkout; «Renovar» cuando ya toca (aviso o vencido).
        var isRenewal = renewal is not null || plan.Tier == PlanTier.Expired;
        var actions = canPay ? MyPlanViewModel.ActionsFor(plan, isRenewal) : [];

        return View(new MyPlanViewModel(
            PlanSummaryViewModel.From(plan.Status, now),
            plan.TrialDays,
            MyPlanViewModel.FeaturesFor(),
            canPay,
            merchant.Status == MerchantStatus.Disabled,
            actions,
            orders.Select(c => OrderRow.From(c, today)).ToList(),
            renewal));
    }

    /// <summary>Paso 1: Básico y Pro, cada uno con 1 mes y Anual (secciones <c>#basico</c> y <c>#pro</c>).</summary>
    [HttpGet("checkout")]
    public async Task<IActionResult> ChoosePlan(CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        var settings = await SettingsAsync(cancellationToken);
        var offers = await checkout.GetOffersAsync(merchant.Id, cancellationToken: cancellationToken);
        var currentPlan = plan.Plan ?? plan.LastPlan;
        var today = MexicoTime.Today(clock.GetUtcNow());

        var sections = Enum.GetValues<SubscriptionPlan>().Select(p => new CheckoutPlanSectionViewModel(
            p,
            CheckoutPlanSectionViewModel.TaglineFor(p),
            CheckoutPlanSectionViewModel.HighlightsFor(p),
            p == currentPlan,
            p == SubscriptionPlan.Pro,
            offers.Offers.Where(o => o.Plan == p)
                .Select(o => PlanOfferViewModel.From(o, today, plan.LastPlan, plan.LastPackage))
                .ToList(),
            offers.CanPay ? CheckoutPlanSectionViewModel.NoteFor(plan.Status, p) : null)).ToList();

        return View(new ChoosePlanViewModel(
            new CheckoutStepsViewModel(CheckoutStepsViewModel.PlanStep),
            offers.CanPay ? null : await NotAllowedMessageAsync(merchant.Id, cancellationToken),
            sections,
            FounderNoticeFor(offers, settings),
            offers.CanPay ? MyPlanViewModel.WelcomeDiscountNoticeFor(offers.WelcomeDiscount) : null,
            MyPlanViewModel.FeaturesFor()));
    }

    /// <summary>
    /// El antiguo paso de la duración (hasta 2026-11-27): lleva a la sección del plan en el paso 1, para no romper ligas
    /// guardadas.
    /// </summary>
    [HttpGet("checkout/{plan}")]
    public IActionResult ChooseDuration(string plan) =>
        SubscriptionSlugs.TryParsePlan(plan, out var chosen)
            ? Redirect(Url.Content("~" + PlanUrls.CheckoutFor(chosen)))
            : NotFound();

    /// <summary>
    /// Paso 2: el resumen del pedido, con el total, la casilla de los Términos y «Pagar». No llama a Pey. Un paquete que
    /// ya no se ofrece (3 o 6 meses: el «Regresar» de un link viejo) lleva a la sección de su plan.
    /// </summary>
    [HttpGet("checkout/{plan}/{package}")]
    public async Task<IActionResult> Summary(string plan, string package, CancellationToken cancellationToken)
    {
        if (!SubscriptionSlugs.TryParsePlan(plan, out var chosenPlan) || !SubscriptionSlugs.TryParsePackage(package, out var chosenPackage))
        {
            return NotFound();
        }

        if (!chosenPackage.IsOffered())
        {
            return Redirect(Url.Content("~" + PlanUrls.CheckoutFor(chosenPlan)));
        }

        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        return await SummaryViewAsync(merchant.Id, chosenPlan, chosenPackage, new CheckoutInput(), cancellationToken);
    }

    /// <summary>
    /// «Pagar» del resumen: valida la casilla, vuelve a cotizar y solo entonces crea (o reutiliza) el link y manda a la
    /// página de Pey. Sin la casilla o con el precio cambiado vuelve al resumen sin llamar a Pey.
    /// </summary>
    [HttpPost("checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout([Bind(Prefix = "Input")] CheckoutInput input, CancellationToken cancellationToken)
    {
        if (!SubscriptionSlugs.TryParsePlan(input.Plan, out var plan) || !SubscriptionSlugs.TryParsePackage(input.Package, out var package))
        {
            return NotFound();
        }

        // 3 y 6 meses ya no se venden: ni cargo ni link.
        if (!package.IsOffered())
        {
            return Redirect(Url.Content("~" + PlanUrls.CheckoutFor(plan)));
        }

        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        if (!input.TryGetExpectedTotal(out var expectedTotal))
        {
            ModelState.AddModelError(string.Empty, CheckoutMessages.PriceChanged);
        }

        if (!ModelState.IsValid)
        {
            return await SummaryViewAsync(merchant.Id, plan, package, input, cancellationToken);
        }

        var baseUrl = new Uri(site.Value.BaseUrlFor(Request));
        var result = await checkout.StartAsync(
            new StartCheckoutCommand(merchant.Id, plan, package, baseUrl, LegalDocuments.CurrentTermsVersion, expectedTotal),
            cancellationToken);
        switch (result)
        {
            case CheckoutResult.Redirect redirect:
                return Redirect(redirect.Url.AbsoluteUri);
            case CheckoutResult.PriceChanged:
                ModelState.AddModelError(string.Empty, CheckoutMessages.PriceChanged);
                return await SummaryViewAsync(merchant.Id, plan, package, input, cancellationToken);
            case CheckoutResult.NotAllowed notAllowed:
                TempData[NoticeKey] = notAllowed.Message;
                break;
            case CheckoutResult.HttpsRequired:
                TempData[NoticeKey] = environment.IsDevelopment() ? HttpsDevelopmentWarning : CheckoutMessages.GatewayUnavailable;
                break;
            case CheckoutResult.GatewayFailed failed:
                TempData[NoticeKey] = failed.Message;
                break;
            default:
                throw new InvalidOperationException($"Resultado inesperado: {result}");
        }

        return Redirect(Url.Content("~" + PlanUrls.CheckoutSummary(plan, package)));
    }

    /// <summary>
    /// Paso 4, regreso de Pey: muestra el pedido (consulta a Pey con freno) y la vista sigue consultando cada 3 s. El
    /// cargo de otro vendedor da 404 (filtro multi-tenant).
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

        string? period = null;
        if (charge.Status == SubscriptionChargeStatus.Paid)
        {
            period = await PeriodTextAsync(charge.Id, cancellationToken);
        }

        return View(new SubscriptionPaymentViewModel(
            charge.Id,
            SubscriptionPaths.OrderNumber(charge.Id),
            $"{charge.Plan.Label()} · {charge.Package.Label()}",
            SaleQrViewModel.FormatAmount(charge.Amount),
            charge.Status,
            period,
            charge.Status == SubscriptionChargeStatus.Pending ? charge.Url : null,
            PlanUrls.CheckoutSummary(charge.Plan, charge.Package)));
    }

    /// <summary>Comprobante de un pedido pagado. Uno sin pagar o de otro vendedor da 404 (filtro multi-tenant).</summary>
    [HttpGet("pedidos/{id:int}/comprobante")]
    public async Task<IActionResult> Receipt(int id, CancellationToken cancellationToken)
    {
        var charge = await db.SubscriptionCharges.AsNoTracking()
            .Include(c => c.Payments)
            .SingleOrDefaultAsync(c => c.Id == id && c.Status == SubscriptionChargeStatus.Paid, cancellationToken);
        if (charge is null)
        {
            return NotFound();
        }

        var owner = await db.Merchants.AsNoTracking()
            .Where(m => m.Id == charge.MerchantId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.BusinessName, u.Email })
            .SingleOrDefaultAsync(cancellationToken);
        var settings = await SettingsAsync(cancellationToken);
        var (amountBase, vat) = SubscriptionPricing.VatBreakdown(charge.Amount, settings.VatRate);
        var paidAt = charge.Payments.Max(p => (DateTimeOffset?)p.PaidAt) ?? charge.PaidAt;
        var today = MexicoTime.Today(clock.GetUtcNow());

        return View(new SubscriptionReceiptViewModel(
            charge.Id,
            SubscriptionPaths.OrderNumber(charge.Id),
            string.IsNullOrWhiteSpace(owner?.BusinessName) ? null : owner.BusinessName,
            owner?.Email,
            $"{charge.Plan.Label()} · {charge.Package.Label()}",
            await PeriodTextAsync(charge.Id, cancellationToken),
            SubscriptionReceiptViewModel.RowsFor(charge),
            SaleQrViewModel.FormatAmount(charge.Amount),
            SaleQrViewModel.FormatAmount(amountBase),
            SaleQrViewModel.FormatAmount(vat),
            OrderRow.MethodTextOf(charge.Payments),
            paidAt is { } instant ? SaleFormats.DateTime(instant, today) : null,
            site.Value.BaseUrlFor(Request).TrimEnd('/') + PlanUrls.Receipt(charge.Id)));
    }

    private async Task<IActionResult> SummaryViewAsync(
        int merchantId, SubscriptionPlan plan, SubscriptionPackage package, CheckoutInput input, CancellationToken cancellationToken)
    {
        var order = await checkout.GetOrderAsync(merchantId, plan, package, cancellationToken);
        var httpsWarning = order.CanPay && environment.IsDevelopment() && !IsHttps(site.Value.BaseUrlFor(Request))
            ? HttpsDevelopmentWarning
            : null;
        input.Plan = plan.Slug();
        input.Package = package.Slug();
        return View("Summary",
            CheckoutSummaryViewModel.For(order, plan, package, clock.GetUtcNow(), site.Value.TermsUrl, httpsWarning) with { Input = input });
    }

    /// <summary>El periodo que pagó el pedido (el <c>Paid</c> con su cargo; una conversión conserva el cargo, pero no lo es).</summary>
    private async Task<string?> PeriodTextAsync(int chargeId, CancellationToken cancellationToken)
    {
        var period = await db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.SubscriptionChargeId == chargeId && p.Kind == SubscriptionPeriodKind.Paid)
            .Select(p => new { p.StartsAt, p.EndsAt })
            .FirstOrDefaultAsync(cancellationToken);
        return period is null ? null : SubscriptionPaymentViewModel.PeriodTextFor(period.StartsAt, period.EndsAt);
    }

    private async Task<string> NotAllowedMessageAsync(int merchantId, CancellationToken cancellationToken)
    {
        var order = await checkout.GetOrderAsync(merchantId, SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, cancellationToken);
        return order.NotAllowedMessage ?? CheckoutMessages.GatewayUnavailable;
    }

    private Task<SystemSettings> SettingsAsync(CancellationToken cancellationToken) =>
        db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);

    private Task<SubscriptionCharge?> FindChargeAsync(int id, CancellationToken cancellationToken) =>
        db.SubscriptionCharges.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    private static string? FounderNoticeFor(SubscriptionOffersResult offers, SystemSettings settings)
    {
        if (offers.IsFounder)
        {
            return "Eres fundador: conservas el precio de fundador de por vida, en Básico y en Pro.";
        }

        if (!offers.CanPay || offers.FounderSlotsLeft <= 0)
        {
            return null;
        }

        var slots = offers.FounderSlotsLeft == 1 ? "queda 1 lugar" : $"quedan {offers.FounderSlotsLeft:N0} lugares";
        return $"Precio de fundador para los primeros {settings.FounderSlots:N0} que paguen, de por vida y en cualquier plan: {slots}.";
    }

    private static bool IsHttps(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
