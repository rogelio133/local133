using System.Globalization;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Rutas del plan (relativas a la raíz del sitio, para <c>Url.Content("~" + …)</c>).</summary>
public static class PlanUrls
{
    /// <summary>«Mi plan»: estado, paquetes y pagos.</summary>
    public const string Plan = "/app/plan";

    /// <summary>
    /// «Elegir plan» del vencido (Cobrar, Inicio, banner, corte y productos, 5.14). Hoy es «Mi plan»; la 5.15 lo
    /// cambia al checkout.
    /// </summary>
    public const string ChoosePlan = Plan;

    /// <summary>POST con el paquete: crea el link de Pey y redirige a su página.</summary>
    public const string Pay = "/app/plan/pagar";

    /// <summary>Confirmación del pago (regreso de Pey).</summary>
    public static string PaymentStatus(int chargeId) => $"/app/plan/pago/{chargeId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Estado del cargo para el polling de la confirmación.</summary>
    public static string ApiStatus(int chargeId) => $"/api/subscription-charges/{chargeId.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Aviso «Disponible con Pro» (parcial <c>_ProRequired</c>): qué se gana con Pro y el botón a «Mi plan».
/// </summary>
/// <param name="Detail">Lo que el vendedor ya tiene esperando (p. ej. «Tu catálogo de 63 productos te espera»).</param>
public sealed record ProRequiredViewModel(string Title, string Message, string? Detail = null, string Icon = "bi-stars");

/// <summary>
/// Plan vencido (5.14, parcial <c>_PlanExpired</c>): «Tu plan venció el 3 de diciembre · Elige un plan para seguir
/// cobrando», con el botón «Elegir plan». Lo muestran Cobrar (monto y carrito), Inicio, el corte y Productos.
/// </summary>
/// <param name="LastActiveDayText">Último día del plan («3 de diciembre»); <c>null</c> si no se conoce.</param>
/// <param name="Detail">Texto propio de la pantalla; sin él, <see cref="DefaultMessage"/>.</param>
public sealed record PlanExpiredViewModel(string? LastActiveDayText, string? Detail = null)
{
    public const string DefaultMessage = "Elige un plan para seguir cobrando.";

    public string Title => LastActiveDayText is null ? "Tu plan venció" : $"Tu plan venció el {LastActiveDayText}";

    public string Message => Detail ?? DefaultMessage;

    public static PlanExpiredViewModel For(PlanStatus status, DateTimeOffset now, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new(status.LastActiveDay is { } lastDay ? SaleFormats.Date(lastDay, MexicoTime.Today(now)) : null, detail);
    }
}

/// <summary>«Mi plan» (5.6): tarjeta del plan, paquetes para pagar, pagos y lo que incluye cada plan.</summary>
/// <param name="Plan">Tarjeta del plan; <c>null</c> en el onboarding (la prueba empieza al activarse la cuenta).</param>
/// <param name="Offers">Paquetes; vacío si todavía no puede pagar (onboarding) o está dado de baja.</param>
/// <param name="FounderNotice">«Eres fundador…» o «Quedan N lugares de fundador»; <c>null</c> si ya no hay.</param>
/// <param name="HttpsWarning">Solo en Development: el sitio corre por http y Pey rechazaría las URLs de regreso.</param>
/// <param name="RenewalNotice">Aviso de que toca renovar (5.7): antes del vencimiento, ese día y en la gracia.</param>
public sealed record MyPlanViewModel(
    PlanSummaryViewModel? Plan,
    int TrialDays,
    IReadOnlyList<PlanFeatureRow> Features,
    bool CanPay,
    bool IsDisabled,
    IReadOnlyList<PlanOfferViewModel> Offers,
    string? FounderNotice,
    IReadOnlyList<SubscriptionChargeRow> InProgress,
    IReadOnlyList<SubscriptionChargeRow> Payments,
    string? HttpsWarning,
    RenewalNoticeViewModel? RenewalNotice = null,
    string? WelcomeDiscountNotice = null)
{
    /// <summary>Aviso del descuento de bienvenida del referido (5.8): «Te invitaron: tienes $59.70 de descuento…».</summary>
    public static string? WelcomeDiscountNoticeFor(decimal? discount) =>
        discount is { } amount && amount > 0
            ? $"Te invitaron a Local133: tienes {PlanOfferViewModel.Money(amount)} de descuento en tu primer pago de Pro, en el paquete que elijas."
            : null;

    /// <summary>Título de la sección de paquetes según el estado: pagar, renovar o agregar tiempo.</summary>
    public string OffersTitle => Plan?.Tier switch
    {
        PlanTier.Pro => "Agrega más tiempo de Pro",
        PlanTier.Basic => "Cambia a Pro",
        PlanTier.Grace => "Renueva tu plan",
        PlanTier.Trial => "Paga Pro y sigue después de tu prueba",
        _ => "Paga Pro",
    };

    /// <summary>Nota bajo los paquetes: desde cuándo cuenta el pago.</summary>
    public string? OffersNote => Plan?.Tier switch
    {
        PlanTier.Trial => "Tu prueba no se acorta: lo que pagues empieza cuando termine.",
        PlanTier.Pro or PlanTier.Basic => "Lo que pagues empieza cuando termine tu plan actual: no pierdes ningún día.",
        PlanTier.Grace => "Lo que pagues cuenta desde que venció tu plan.",
        PlanTier.Expired => "Pro empieza en cuanto se confirme tu pago.",
        _ => null,
    };

    /// <summary>
    /// Lo que incluye cada plan (§2 del diseño de planes Básico y Pro). <c>ComingSoon</c> = se anuncia, todavía no existe.
    /// </summary>
    public static IReadOnlyList<PlanFeatureRow> FeaturesFor() =>
    [
        new("Cobro por monto con QR o al celular", "Sí", "Sí"),
        new("Cobros concretados al mes", "Ilimitados, con uso justo", "Ilimitados, con uso justo"),
        new("Historial de ventas", "Completo, con fechas y búsqueda", "Completo, con fechas y búsqueda"),
        new("Corte de caja diario", "Sí", "Sí"),
        new("Recibo por WhatsApp y «Compartir»", "Sí, con «Cobrado con Local133»", "Sí, con tu logo"),
        new("Exportar ventas a Excel", "—", "Sí"),
        new("Catálogo de productos", "Solo lectura", "Sí"),
        new("Cobro con productos (carrito)", "—", "Sí"),
        new("Catálogo en línea con liga", "—", "Sí"),
        new("Invita y gana días", "Sí", "Sí"),
        new("Vendedores adicionales", "—", "3", ComingSoon: true),
        new("QR impreso del puesto", "—", "Sí", ComingSoon: true),
        new("Reportes", "—", "Sí", ComingSoon: true),
    ];
}

public sealed record PlanFeatureRow(string Feature, string Basic, string Pro, bool ComingSoon = false);

/// <summary>
/// Aviso de «Mi plan» de que toca renovar (5.7), con la misma etapa que el recordatorio
/// (<see cref="SubscriptionReminders.RenewalNoticeFor"/>). No trae link: el vendedor elige su paquete abajo y ahí
/// nace el link de pago.
/// </summary>
/// <param name="IsUrgent">El día del vencimiento y en la gracia (alerta de advertencia).</param>
public sealed record RenewalNoticeViewModel(string Title, string Message, bool IsUrgent)
{
    public static RenewalNoticeViewModel? From(SubscriptionReminder? notice, DateOnly today)
    {
        if (notice is null)
        {
            return null;
        }

        var lastDay = SaleFormats.Date(notice.LastProDay, today);
        var graceDay = notice.LastGraceDay is { } g ? (g == today ? "hoy" : SaleFormats.Date(g, today)) : null;
        const string choose = "Elige tu paquete para renovar.";
        return notice.Stage switch
        {
            // La tarjeta del plan ya dice cuándo venció y hasta cuándo hay gracia: aquí va la llamada a renovar.
            SubscriptionReminderStage.Grace => new(
                graceDay == "hoy" ? "Renueva hoy para no perder Pro" : $"Renueva antes del {graceDay} para no perder Pro",
                choose,
                IsUrgent: true),
            SubscriptionReminderStage.DueToday => new(
                "Tu plan Pro vence hoy: renuévalo",
                (graceDay is null ? "" : $"Tienes hasta el {graceDay} sin perder Pro. ") + choose,
                IsUrgent: true),
            SubscriptionReminderStage.AnnualUpcoming => new(
                $"Tu plan anual vence el {lastDay}",
                $"{choose} Si renuevas el anual antes de que terminen tus días de gracia, conservas tu precio.",
                IsUrgent: false),
            SubscriptionReminderStage.Upcoming => notice.EndingKind switch
            {
                SubscriptionPeriodKind.Paid => new($"Tu plan Pro vence {When(notice.DaysLeft)}", $"Vence el {lastDay}. {choose}", IsUrgent: false),
                SubscriptionPeriodKind.Trial => new($"Tu prueba termina {When(notice.DaysLeft)}",
                    $"Termina el {lastDay}; después tu plan vence y ya no puedes cobrar. Elige tu paquete para seguir.", IsUrgent: false),
                _ => new($"Tus días de Pro terminan {When(notice.DaysLeft)}",
                    $"Terminan el {lastDay}; después tu plan vence y ya no puedes cobrar. Elige tu paquete para seguir.", IsUrgent: false),
            },
            _ => null,
        };
    }

    /// <summary>«en 3 días», «mañana» u «hoy».</summary>
    private static string When(int daysLeft) => daysLeft switch
    {
        <= 0 => "hoy",
        1 => "mañana",
        _ => $"en {daysLeft.ToString(CultureInfo.InvariantCulture)} días",
    };
}

/// <summary>Un paquete en «Mi plan», con sus textos ya armados.</summary>
/// <param name="Value">Valor del formulario (nombre del enum).</param>
/// <param name="Highlight">«Por $796 más llévate el año completo» o «Equivale a $165.83 al mes: 2 meses de regalo».</param>
/// <param name="IsCurrentPackage">Es el paquete del último pago (insignia «Tu paquete», 5.7).</param>
/// <param name="ListPriceText">Precio antes del descuento de bienvenida (tachado, 5.8); <c>null</c> sin descuento.</param>
public sealed record PlanOfferViewModel(
    string Value,
    string Title,
    string PriceText,
    string? PerMonthText,
    string? Highlight,
    string ProUntilText,
    bool IsFounderPrice,
    bool IsRecommended,
    bool IsCurrentPackage = false,
    string? ListPriceText = null)
{
    public static PlanOfferViewModel From(SubscriptionOffer offer, DateOnly today, SubscriptionPackage? currentPackage = null)
    {
        var amount = offer.Quote.Amount;
        // El anual con meses de regalo ya dice cuánto sale al mes en su texto destacado.
        string? perMonth = offer.Package switch
        {
            SubscriptionPackage.OneMonth => "al mes",
            SubscriptionPackage.Annual when offer.GiftMonths > 0 => null,
            _ => $"{Money(offer.MonthlyEquivalent)} al mes",
        };
        string? highlight = offer.Package switch
        {
            SubscriptionPackage.SixMonths when offer.AnnualUpgradeDifference is { } diff =>
                $"Por {Money(diff)} más llévate el año completo",
            SubscriptionPackage.Annual when offer.GiftMonths > 0 =>
                $"Equivale a {Money(offer.MonthlyEquivalent)} al mes: {(offer.GiftMonths == 1 ? "1 mes" : $"{offer.GiftMonths} meses")} de regalo",
            _ => null,
        };
        return new PlanOfferViewModel(
            offer.Package.ToString(),
            offer.Package.Label(),
            Money(amount),
            perMonth,
            highlight,
            $"{offer.Plan.Label()} hasta el {SaleFormats.Date(offer.LastDay, today)}",
            offer.IsFounderPrice,
            offer.Package == SubscriptionPackage.Annual,
            offer.Package == currentPackage,
            offer.Quote.DiscountAmount > 0 ? Money(offer.Quote.ListPrice) : null);
    }

    /// <summary>«$199», o con centavos si los tiene: «$165.83».</summary>
    public static string Money(decimal amount) =>
        amount.ToString(amount == decimal.Truncate(amount) ? "$#,0" : "$#,0.00", CultureInfo.GetCultureInfo("es-MX"));
}

/// <summary>Un cargo de la suscripción en «Mi plan» (en proceso o pagado).</summary>
public sealed record SubscriptionChargeRow(
    int Id, string DateText, string PackageText, string AmountText, SubscriptionChargeStatus Status, string StatusText,
    string? MethodText)
{
    public static SubscriptionChargeRow From(SubscriptionCharge charge, DateOnly today) => new(
        charge.Id,
        SaleFormats.Date(MexicoTime.DateOf(charge.PaidAt ?? charge.CreatedAt), today),
        charge.Package.Label(),
        SaleQrViewModel.FormatAmount(charge.Amount),
        charge.Status,
        StatusTextOf(charge.Status),
        MethodTextOf(charge.Payments.OrderByDescending(p => p.PaidAt).Select(p => p.PaymentMethod).FirstOrDefault()));

    public static string StatusTextOf(SubscriptionChargeStatus status) => status switch
    {
        SubscriptionChargeStatus.Pending => "Esperando pago",
        SubscriptionChargeStatus.Processing => "Procesando",
        SubscriptionChargeStatus.Paid => "Pagado",
        SubscriptionChargeStatus.Partial => "En revisión",
        SubscriptionChargeStatus.Failed => "Sin link",
        _ => status.ToString(),
    };

    private static string? MethodTextOf(string? method) => method switch
    {
        "CODI" => "CoDi",
        "CARD" => "Tarjeta",
        null or "" => null,
        _ => method,
    };
}

/// <summary>Confirmación del pago (/app/plan/pago/{id}), al volver de la página de Pey.</summary>
/// <param name="ProUntilText">«Pro hasta el 20 de diciembre»: solo si ya se abonó.</param>
/// <param name="PayUrl">Página de pago de Pey, para volver si todavía no paga.</param>
public sealed record SubscriptionPaymentViewModel(
    int Id,
    string PackageText,
    string AmountText,
    SubscriptionChargeStatus Status,
    string? ProUntilText,
    string? PayUrl)
{
    public bool IsPaid => Status == SubscriptionChargeStatus.Paid;

    /// <summary>Se sigue esperando la confirmación (la vista consulta cada 3 s).</summary>
    public bool IsWaiting => Status is SubscriptionChargeStatus.Pending or SubscriptionChargeStatus.Processing;

    public bool IsProcessing => Status == SubscriptionChargeStatus.Processing;

    public bool IsPartial => Status == SubscriptionChargeStatus.Partial;

    public bool IsFailed => Status == SubscriptionChargeStatus.Failed;

    /// <summary>«Tienes Pro hasta el …» del vendedor, con el último día en México; <c>null</c> si ya terminó.</summary>
    public static string? ProUntilTextFor(DateTimeOffset? proUntil, DateTimeOffset now) =>
        proUntil is { } until && until > now
            ? $"Tienes Pro hasta el {SaleFormats.Date(MexicoTime.DateOf(until.AddTicks(-1)), MexicoTime.Today(now))}."
            : null;
}
