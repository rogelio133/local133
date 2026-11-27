using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Subscriptions;
using Local133.Web.Identity;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Textos del checkout por pasos (5.15).</summary>
public static class CheckoutMessages
{
    public const string TermsNotAccepted = "Para pagar, acepta los Términos de la suscripción.";

    public const string PriceChanged = "El precio de tu pedido cambió. Revisa el total antes de pagar.";

    public const string GatewayUnavailable = "No pudimos generar el link de pago. Intenta de nuevo en unos minutos.";
}

/// <summary>Un paso de la barra del checkout.</summary>
/// <param name="Url">Liga si ya se hizo y se puede volver; <c>null</c> en el actual, los que siguen y el de Pey.</param>
public sealed record CheckoutStep(int Number, string Name, string? Url, bool IsCurrent, bool IsDone);

/// <summary>
/// Barra de pasos del checkout (parcial <c>_CheckoutSteps</c>): Plan ─ Resumen ─ Pago ─ Listo (2026-11-27: plan y
/// duración van en el mismo paso). Los pasos ya hechos son ligas mientras el pedido no se paga; el de Pey nunca lo es.
/// </summary>
public sealed record CheckoutStepsViewModel(int Current, SubscriptionPlan? Plan = null, SubscriptionPackage? Package = null)
{
    public const int PlanStep = 1;
    public const int SummaryStep = 2;
    public const int PayStep = 3;
    public const int DoneStep = 4;

    private static readonly string[] Names = ["Plan", "Resumen", "Pago", "Listo"];

    public int Total => Names.Length;

    public string CurrentName => Names[Current - 1];

    /// <summary>En pantallas chicas: «Paso 2 de 4 · Resumen».</summary>
    public string CompactText => $"Paso {Current.ToString(CultureInfo.InvariantCulture)} de {Total.ToString(CultureInfo.InvariantCulture)} · {CurrentName}";

    public IReadOnlyList<CheckoutStep> Steps =>
    [
        Step(PlanStep, Plan is { } plan ? PlanUrls.CheckoutFor(plan) : PlanUrls.Checkout),
        Step(SummaryStep, Plan is { } p && Package is { } package ? PlanUrls.CheckoutSummary(p, package) : null),
        Step(PayStep, null),
        Step(DoneStep, null),
    ];

    private CheckoutStep Step(int number, string? url) => new(
        number,
        Names[number - 1],
        number < Current && Current <= SummaryStep ? url : null,
        number == Current,
        number < Current);
}

/// <summary>Formulario del resumen del pedido (<c>POST /app/plan/checkout</c>).</summary>
public sealed class CheckoutInput
{
    /// <summary>Plan en la URL (<see cref="SubscriptionSlugs"/>).</summary>
    public string? Plan { get; set; }

    /// <summary>Paquete en la URL (<see cref="SubscriptionSlugs"/>).</summary>
    public string? Package { get; set; }

    // [Required] explícito solo para que el data-val-required implícito de un bool no salga en inglés (como
    // LegalAcceptanceInput); la regla real es MustBeTrue, también en el cliente.
    [Required(ErrorMessage = CheckoutMessages.TermsNotAccepted)]
    [MustBeTrue(ErrorMessage = CheckoutMessages.TermsNotAccepted)]
    [Display(Name = "Acepto los Términos de la suscripción")]
    public bool AcceptTerms { get; set; }

    /// <summary>Total que vio el vendedor, en texto invariante («1490.00»).</summary>
    public string? ExpectedTotal { get; set; }

    public bool TryGetExpectedTotal(out decimal total)
    {
        total = 0;
        return ExpectedTotal is not null
            && Regex.IsMatch(ExpectedTotal, ChargeInput.AmountPattern)
            && ChargeInput.TryParseAmount(ExpectedTotal, out total);
    }

    /// <summary>Total en el formato del campo oculto.</summary>
    public static string FormatTotal(decimal total) => total.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>
/// Una sección del paso 1 (<c>id="basico"</c> o <c>id="pro"</c>, a la que llegan los botones de «Mi plan»): el plan
/// con sus puntos fuertes y sus dos tarjetas, 1 mes y Anual.
/// </summary>
/// <param name="Offers">Vacío si todavía no puede pagar (onboarding o dado de baja).</param>
/// <param name="Note">Desde cuándo cuenta lo que pague (<see cref="NoteFor"/>).</param>
public sealed record CheckoutPlanSectionViewModel(
    SubscriptionPlan Plan,
    string Tagline,
    IReadOnlyList<string> Highlights,
    bool IsCurrent,
    bool IsRecommended,
    IReadOnlyList<PlanOfferViewModel> Offers,
    string? Note)
{
    public string Name => Plan.Label();

    public string Slug => Plan.Slug();

    /// <summary>Nota bajo el plan según el estado: desde cuándo cuenta el pago.</summary>
    public static string? NoteFor(PlanStatus status, SubscriptionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(status);
        var upgrades = plan == SubscriptionPlan.Pro
            && (status.Plan == SubscriptionPlan.Basic || status.NextPlan == SubscriptionPlan.Basic)
            && status.Tier != PlanTier.Grace;
        if (upgrades)
        {
            return "Tus días de Básico se vuelven días de Pro y lo que pagues empieza después: no pierdes nada.";
        }

        return status.Tier switch
        {
            PlanTier.Trial => "Tu prueba no se acorta: lo que pagues empieza cuando termine.",
            PlanTier.Pro or PlanTier.Basic => "Lo que pagues empieza cuando termine tu plan actual: no pierdes ningún día.",
            PlanTier.Grace => "Lo que pagues cuenta desde que venció tu plan.",
            PlanTier.Expired => $"{plan.Label()} empieza en cuanto se confirme tu pago.",
            _ => null,
        };
    }

    public static string TaglineFor(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Basic => "Para cobrar con CoDi sin complicarte.",
        _ => "Todo lo de Básico, más tus productos.",
    };

    public static IReadOnlyList<string> HighlightsFor(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Basic =>
        [
            "Cobros con QR o al celular, sin límite mensual",
            "Historial completo y corte de caja diario",
            "Recibo por WhatsApp",
        ],
        _ =>
        [
            "Catálogo de productos y cobro con carrito",
            "Catálogo en línea con tu liga",
            "Recibo con tu logo y ventas en Excel",
        ],
    };
}

/// <summary>
/// Paso 1 del checkout (<c>/app/plan/checkout</c>, 2026-11-27): Básico y Pro, cada uno con 1 mes y Anual, en una sola
/// página. Cada tarjeta lleva al resumen.
/// </summary>
/// <param name="NotAllowedMessage">Por qué todavía no puede pagar (onboarding o dado de baja); los planes se ven igual.</param>
public sealed record ChoosePlanViewModel(
    CheckoutStepsViewModel Steps,
    string? NotAllowedMessage,
    IReadOnlyList<CheckoutPlanSectionViewModel> Sections,
    string? FounderNotice,
    string? WelcomeDiscountNotice,
    IReadOnlyList<PlanFeatureRow> Features);

/// <summary>Paso 2 del checkout (<c>/app/plan/checkout/{plan}/{paquete}</c>): el resumen del pedido.</summary>
/// <param name="NotAllowedMessage">Por qué no puede pagar: en lugar del formulario sale el aviso.</param>
/// <param name="AnnualDealText">Solo en el anual: «12 meses por el precio de 10».</param>
/// <param name="TimelineText">Qué pasa con lo que ya tiene.</param>
/// <param name="ExpectedTotal">Total en texto invariante para el campo oculto.</param>
/// <param name="HttpsWarning">Solo en Development: el sitio corre por http y Pey rechazaría las URLs de regreso.</param>
public sealed record CheckoutSummaryViewModel(
    CheckoutStepsViewModel Steps,
    SubscriptionPlan Plan,
    SubscriptionPackage Package,
    string? NotAllowedMessage,
    string? PeriodText,
    string? TimelineText,
    IReadOnlyList<OrderPriceRow> Rows,
    string? TotalText,
    string? VatText,
    string? ExpectedTotal,
    bool IsFounderPrice,
    string TermsUrl,
    string? HttpsWarning,
    string? AnnualDealText = null)
{
    /// <summary>«Pro Anual», «Básico (1 mes)».</summary>
    public string Title => PlanOfferViewModel.TitleFor(Plan, Package);

    public bool CanPay => NotAllowedMessage is null && ExpectedTotal is not null;

    public string PlanSlug => Plan.Slug();

    public string PackageSlug => Package.Slug();

    /// <summary>La casilla de los Términos (y lo que el vendedor ya marcó si se vuelve a pintar).</summary>
    public CheckoutInput Input { get; init; } = new();

    public static CheckoutSummaryViewModel For(
        CheckoutOrderResult result, SubscriptionPlan plan, SubscriptionPackage package, DateTimeOffset now, string termsUrl,
        string? httpsWarning)
    {
        ArgumentNullException.ThrowIfNull(result);
        var steps = new CheckoutStepsViewModel(CheckoutStepsViewModel.SummaryStep, plan, package);
        if (result.Order is not { } order)
        {
            return new(steps, plan, package, result.NotAllowedMessage, null, null, [], null, null, null, false, termsUrl, null);
        }

        return new(
            steps,
            plan,
            package,
            null,
            SubscriptionPaymentViewModel.PeriodTextFor(order.Offer.Starts, order.Offer.Until),
            TimelineTextFor(order, MexicoTime.Today(now)),
            RowsFor(order),
            SaleQrViewModel.FormatAmount(order.Total),
            $"IVA incluido: {SaleQrViewModel.FormatAmount(order.Vat)}",
            CheckoutInput.FormatTotal(order.Total),
            order.IsFounderPrice,
            termsUrl,
            httpsWarning,
            PlanOfferViewModel.AnnualDealTextFor(order.Offer) is { } deal ? $"Estás obteniendo {deal}" : null);
    }

    public static IReadOnlyList<OrderPriceRow> RowsFor(SubscriptionOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var rows = new List<OrderPriceRow> { new("list", "Precio de lista", SaleQrViewModel.FormatAmount(order.RegularPrice), false) };
        if (order.PriceAdjustment > 0)
        {
            rows.Add(order.IsFounderPrice
                ? new("founder", "Precio de fundador", SaleQrViewModel.FormatAmount(order.PriceAdjustment), true)
                : new("locked", "Tu precio anual congelado", SaleQrViewModel.FormatAmount(order.PriceAdjustment), true));
        }

        if (order.WelcomeDiscount > 0)
        {
            rows.Add(new("welcome", "Descuento de bienvenida", SaleQrViewModel.FormatAmount(order.WelcomeDiscount), true));
        }

        return rows;
    }

    /// <summary>
    /// «Tus 20 días de Básico se vuelven unos 9 días de Pro desde hoy», «Tu Básico empieza el 9 de diciembre, cuando
    /// termina tu Pro», «Cuenta desde el 25 de noviembre, cuando venció tu plan» o «Empieza hoy…».
    /// </summary>
    public static string TimelineTextFor(SubscriptionOrder order, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(order);
        var plan = order.Plan.Label();
        return order.Timeline switch
        {
            SubscriptionOrderTimeline.ConvertsBasic when order.BasicDays == 0 =>
                $"Lo que te queda de Básico se vuelve Pro{(order.ConvertsFromNow ? " desde hoy" : string.Empty)}, y lo que pagues empieza después.",
            SubscriptionOrderTimeline.ConvertsBasic =>
                $"Tus {Days(order.BasicDays)} de Básico se vuelven unos {Days(order.ProDays)} de Pro{(order.ConvertsFromNow ? " desde hoy" : string.Empty)}, y lo que pagues empieza después.",
            SubscriptionOrderTimeline.AfterCurrent when order.PrecedingIsTrial =>
                $"Tu {plan} empieza el {SaleFormats.Date(order.Offer.FirstDay, today)}, cuando termina tu prueba.",
            SubscriptionOrderTimeline.AfterCurrent when order.PrecedingPlan == order.Plan =>
                $"Se suma a tu {plan}: empieza el {SaleFormats.Date(order.Offer.FirstDay, today)}, cuando termina lo que ya tienes.",
            SubscriptionOrderTimeline.AfterCurrent =>
                $"Tu {plan} empieza el {SaleFormats.Date(order.Offer.FirstDay, today)}, cuando termina tu {order.PrecedingPlan?.Label() ?? "plan actual"}.",
            SubscriptionOrderTimeline.FromGrace =>
                $"Cuenta desde el {SaleFormats.Date(order.Offer.FirstDay, today)}, cuando venció tu plan.",
            _ => "Empieza hoy, en cuanto se confirme tu pago.",
        };
    }

    private static string Days(int days) => days == 1 ? "1 día" : $"{days.ToString(CultureInfo.InvariantCulture)} días";
}
