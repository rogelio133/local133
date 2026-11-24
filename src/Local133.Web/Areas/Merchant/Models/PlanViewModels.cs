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
/// Contador del mes del gratuito (parcial <c>_ChargeUsage</c>): «82 de 100 cobros este mes», aviso al 80 % y bloqueo
/// al llegar al límite. Solo se muestra cuando el plan tiene <see cref="PlanEntitlements.HasMonthlyChargeLimit"/>.
/// </summary>
public sealed record ChargeUsageViewModel(
    ChargeAllowanceState State,
    string Title,
    string Message,
    int Percent,
    string BarClass,
    bool IsBlocked)
{
    public static ChargeUsageViewModel From(MonthlyChargeUsage usage, DateOnly today)
    {
        var a = usage.Allowance;
        var resetsOn = SaleFormats.Date(usage.ResetsOn, today);
        var percent = a.PaidLimit <= 0 ? 100 : Math.Min(100, a.Paid * 100 / a.PaidLimit);
        var title = $"{Count(Math.Min(a.Paid, a.PaidLimit))} de {Count(a.PaidLimit)} cobros este mes";

        return a.State switch
        {
            ChargeAllowanceState.Exhausted => new(a.State, title,
                $"Llegaste al límite del mes. No puedes generar cobros hasta el {resetsOn} o hasta que pagues Pro.",
                100, "bg-danger", IsBlocked: true),
            ChargeAllowanceState.GenerationCapReached => new(a.State, title,
                $"Generaste {Count(a.Generated)} cobros este mes, el máximo del plan gratuito. Podrás generar más el {resetsOn} o con Pro.",
                percent, "bg-danger", IsBlocked: true),
            ChargeAllowanceState.NearLimit => new(a.State, title,
                a.Remaining == 1
                    ? "Te queda 1 cobro este mes. Con Pro cobras sin límite."
                    : $"Te quedan {Count(a.Remaining)} cobros este mes. Con Pro cobras sin límite.",
                percent, "bg-warning", IsBlocked: false),
            _ => new(a.State, title,
                $"El plan gratuito incluye {Count(a.PaidLimit)} cobros al mes. Se reinician el {resetsOn}.",
                percent, "bg-success", IsBlocked: false),
        };
    }

    /// <summary>Hay que llamar la atención (aviso o bloqueo).</summary>
    public bool IsWarning => State is not ChargeAllowanceState.Normal;

    private static string Count(int n) => n.ToString("#,0", CultureInfo.GetCultureInfo("es-MX"));
}

/// <summary>Avisos del bloqueo de cobro por el plan (formulario de Cobrar).</summary>
public static class PlanBlockedNotices
{
    public static ChargeBlockedNotice? For(MonthlyChargeUsage usage, DateOnly today)
    {
        var resetsOn = SaleFormats.Date(usage.ResetsOn, today);
        return usage.Allowance.State switch
        {
            ChargeAllowanceState.Exhausted => new(
                "Llegaste a tus cobros del mes",
                $"Usaste los {usage.Allowance.PaidLimit} cobros del mes del plan gratuito. Podrás cobrar de nuevo el {resetsOn}, o desde hoy con Pro.",
                "~" + PlanUrls.Plan, "Conoce Pro"),
            ChargeAllowanceState.GenerationCapReached => new(
                "Llegaste al máximo de cobros generados",
                $"Generaste {usage.Allowance.Generated} cobros este mes, el máximo del plan gratuito. Podrás cobrar de nuevo el {resetsOn}, o desde hoy con Pro.",
                "~" + PlanUrls.Plan, "Conoce Pro"),
            _ => null,
        };
    }
}

/// <summary>«Mi plan» (5.6): tarjeta del plan, contador del mes, paquetes para pagar, pagos y lo que incluye cada plan.</summary>
/// <param name="Plan">Tarjeta del plan; <c>null</c> en el onboarding (la prueba empieza al activarse la cuenta).</param>
/// <param name="Offers">Paquetes; vacío si todavía no puede pagar (onboarding) o está dado de baja.</param>
/// <param name="FounderNotice">«Eres fundador…» o «Quedan N lugares de fundador»; <c>null</c> si ya no hay.</param>
/// <param name="HttpsWarning">Solo en Development: el sitio corre por http y Pey rechazaría las URLs de regreso.</param>
/// <param name="RenewalNotice">Aviso de que toca renovar (5.7): antes del vencimiento, ese día y en la gracia.</param>
public sealed record MyPlanViewModel(
    PlanSummaryViewModel? Plan,
    ChargeUsageViewModel? Usage,
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
        PlanTier.Grace => "Renueva tu plan",
        PlanTier.Trial => "Paga Pro y sigue después de tu prueba",
        _ => "Paga Pro",
    };

    /// <summary>Nota bajo los paquetes: desde cuándo cuenta el pago.</summary>
    public string? OffersNote => Plan?.Tier switch
    {
        PlanTier.Trial => "Tu prueba no se acorta: lo que pagues empieza cuando termine.",
        PlanTier.Pro => "Lo que pagues empieza cuando termine tu plan actual: no pierdes ningún día.",
        PlanTier.Grace => "Lo que pagues cuenta desde que venció tu plan.",
        PlanTier.Free => "Pro empieza en cuanto se confirme tu pago.",
        _ => null,
    };

    /// <summary>
    /// Lo que incluye cada plan (§2 del diseño), con los números de <see cref="SystemSettings"/>. <c>ComingSoon</c> =
    /// se anuncia, todavía no existe.
    /// </summary>
    public static IReadOnlyList<PlanFeatureRow> FeaturesFor(SystemSettings settings) =>
    [
        new("Cobro por monto con QR o al celular", "Sí", "Sí"),
        new("Cobros concretados al mes", $"{settings.FreeMonthlyPaidLimit:N0}", "Sin límite"),
        new("Catálogo de productos", "Solo lectura", "Sí"),
        new("Cobro con productos (carrito)", "—", "Sí"),
        new("Recibo", "Liga para copiar, con «Cobrado con Local133»", "WhatsApp y compartir, con tu logo"),
        new("Historial de ventas", $"Últimos {settings.FreeHistoryDays:N0} días", "Completo, con fechas y búsqueda"),
        new("Exportar ventas a Excel", "—", "Sí"),
        new("Corte de caja diario", "—", "Sí"),
        new("Catálogo en línea con liga", "—", "Sí"),
        new("Invita y gana días de Pro", "Sí", "Sí"),
        new("Vendedores adicionales", "—", "3", ComingSoon: true),
        new("QR impreso del puesto", "—", "Sí", ComingSoon: true),
        new("Reportes", "—", "Sí", ComingSoon: true),
    ];
}

public sealed record PlanFeatureRow(string Feature, string Free, string Pro, bool ComingSoon = false);

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
                    $"Termina el {lastDay}; después pasas al plan gratuito. Elige tu paquete para seguir con Pro.", IsUrgent: false),
                _ => new($"Tus días de Pro terminan {When(notice.DaysLeft)}",
                    $"Terminan el {lastDay}; después pasas al plan gratuito. Elige tu paquete para seguir con Pro.", IsUrgent: false),
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
            $"Pro hasta el {SaleFormats.Date(offer.LastProDay, today)}",
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
