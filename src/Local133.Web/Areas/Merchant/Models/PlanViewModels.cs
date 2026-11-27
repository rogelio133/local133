using System.Globalization;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Subscriptions;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Rutas del plan y del checkout (relativas a la raíz del sitio, para <c>Url.Content("~" + …)</c>).</summary>
public static class PlanUrls
{
    /// <summary>«Mi plan»: estado, lo que sigue, pedidos y lo que incluye cada plan.</summary>
    public const string Plan = SubscriptionPaths.Plan;

    /// <summary>Paso 1 del checkout: elegir plan y duración (Básico y Pro, 1 mes y Anual) en una sola página.</summary>
    public const string Checkout = SubscriptionPaths.Checkout;

    /// <summary>«Elegir plan» sin más datos (Productos del vencido): el paso 1 del checkout (5.15).</summary>
    public const string ChoosePlan = Checkout;

    /// <summary>Paso 1 con el scroll en la sección de un plan («Conoce Pro» lleva a la de Pro).</summary>
    public static string CheckoutFor(SubscriptionPlan plan) => SubscriptionPaths.ChoosePlan(plan);

    /// <summary>Paso 2: el resumen del pedido.</summary>
    public static string CheckoutSummary(SubscriptionPlan plan, SubscriptionPackage package) =>
        SubscriptionPaths.Summary(plan, package);

    /// <summary>
    /// «Elegir plan» o «Renovar» del vencido, de la gracia, de Inicio y del banner: el resumen de su último plan y
    /// paquete; si ese paquete ya no se ofrece (3 o 6 meses), la sección de su plan; si nunca pagó, el paso 1 (§6 del
    /// diseño).
    /// </summary>
    public static string ChoosePlanFor(SubscriptionPlan? lastPlan, SubscriptionPackage? lastPackage) =>
        SubscriptionPaths.ChoosePlanFor(lastPlan, lastPackage);

    public static string ChoosePlanFor(MerchantPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ChoosePlanFor(plan.LastPlan, plan.LastPackage);
    }

    /// <summary>Paso 4: la confirmación del pedido (regreso de Pey).</summary>
    public static string PaymentStatus(int chargeId) => SubscriptionPaths.Payment(chargeId);

    /// <summary>Comprobante de un pedido pagado.</summary>
    public static string Receipt(int chargeId) => SubscriptionPaths.Receipt(chargeId);

    /// <summary>Estado del cargo para el polling de la confirmación.</summary>
    public static string ApiStatus(int chargeId) => $"/api/subscription-charges/{chargeId.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Aviso «Disponible con Pro» (parcial <c>_ProRequired</c>): qué se gana con Pro y el botón a las duraciones de Pro.
/// </summary>
/// <param name="Detail">Lo que el vendedor ya tiene esperando (p. ej. «Tu catálogo de 63 productos te espera»).</param>
public sealed record ProRequiredViewModel(string Title, string Message, string? Detail = null, string Icon = "bi-stars");

/// <summary>
/// Plan vencido (5.14, parcial <c>_PlanExpired</c>): «Tu plan venció el 3 de diciembre · Elige un plan para seguir
/// cobrando», con el botón «Elegir plan». Lo muestran Cobrar (monto y carrito), Inicio, el corte y Productos.
/// </summary>
/// <param name="LastActiveDayText">Último día del plan («3 de diciembre»); <c>null</c> si no se conoce.</param>
/// <param name="Detail">Texto propio de la pantalla; sin él, <see cref="DefaultMessage"/>.</param>
/// <param name="ChooseUrl">Adónde lleva «Elegir plan» (<see cref="PlanUrls.ChoosePlanFor(MerchantPlan)"/>).</param>
public sealed record PlanExpiredViewModel(string? LastActiveDayText, string? Detail = null, string ChooseUrl = PlanUrls.ChoosePlan)
{
    public const string DefaultMessage = "Elige un plan para seguir cobrando.";

    public string Title => LastActiveDayText is null ? "Tu plan venció" : $"Tu plan venció el {LastActiveDayText}";

    public string Message => Detail ?? DefaultMessage;

    public static PlanExpiredViewModel For(MerchantPlan plan, DateTimeOffset now, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(plan.Status.LastActiveDay is { } lastDay ? SaleFormats.Date(lastDay, MexicoTime.Today(now)) : null, detail,
            PlanUrls.ChoosePlanFor(plan));
    }
}

/// <summary>Un botón de «Mi plan» (2026-11-27): «Renovar mi plan Pro», «Adquirir más días de Básico» o «Cambiar a Pro».</summary>
/// <param name="Key">Para <c>data-plan-action</c>: <c>renew</c>, <c>extend</c> o <c>upgrade</c>.</param>
/// <param name="Detail">Texto chico bajo el botón («Pro · Anual»).</param>
public sealed record PlanActionViewModel(string Key, string Text, string Url, bool IsPrimary, string? Detail = null);

/// <summary>
/// «Mi plan» (5.6, rediseñado en la 5.15): la página de la cuenta, no del pago. Estado y lo que sigue, botones al
/// checkout según el caso, «Tus pedidos» y lo que incluye cada plan.
/// </summary>
/// <param name="Plan">Tarjeta del plan; <c>null</c> en el onboarding (la prueba empieza con el primer cobro).</param>
/// <param name="Actions">Botones al checkout; vacío si todavía no puede pagar (onboarding) o está dado de baja.</param>
/// <param name="Orders">Pedidos pagados, en proceso o en revisión; nunca los que no pagó.</param>
/// <param name="RenewalNotice">Aviso de que toca renovar (5.7): antes del vencimiento, ese día y en la gracia.</param>
public sealed record MyPlanViewModel(
    PlanSummaryViewModel? Plan,
    int TrialDays,
    IReadOnlyList<PlanFeatureRow> Features,
    bool CanPay,
    bool IsDisabled,
    IReadOnlyList<PlanActionViewModel> Actions,
    IReadOnlyList<OrderRow> Orders,
    RenewalNoticeViewModel? RenewalNotice = null)
{
    /// <summary>Aviso del descuento de bienvenida del referido (5.8): «Te invitaron: tienes $59.70 de descuento…».</summary>
    public static string? WelcomeDiscountNoticeFor(decimal? discount) =>
        discount is { } amount && amount > 0
            ? $"Te invitaron a Local133: tienes hasta {PlanOfferViewModel.Money(amount)} de descuento en tu primer pago, en el plan y el paquete que elijas."
            : null;

    /// <summary>
    /// Plan del botón principal: el que tiene ahora (la prueba es de Pro y la gracia conserva el que pagó); si no tiene
    /// (vencido), el de su último pago y, si nunca pagó, Pro.
    /// </summary>
    public static SubscriptionPlan ActionPlanFor(MerchantPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Plan ?? plan.LastPlan ?? SubscriptionPlan.Pro;
    }

    /// <summary>
    /// Botones (2026-11-27): uno principal a la sección de su plan en el checkout, «Renovar mi plan Básico» cuando ya
    /// toca renovar (<paramref name="isRenewal"/>: hay aviso de renovar o el plan venció) o «Adquirir más días de
    /// Básico» si no; y, si tiene Básico (ahora o después), «Cambiar a Pro» a la sección de Pro.
    /// </summary>
    public static IReadOnlyList<PlanActionViewModel> ActionsFor(MerchantPlan plan, bool isRenewal)
    {
        var target = ActionPlanFor(plan);
        var url = PlanUrls.CheckoutFor(target);
        var actions = new List<PlanActionViewModel>
        {
            isRenewal
                ? new("renew", $"Renovar mi plan {target.Label()}", url, true)
                : new("extend", $"Adquirir más días de {target.Label()}", url, true),
        };

        var hasBasic = plan.Plan == SubscriptionPlan.Basic || plan.Status.NextPlan == SubscriptionPlan.Basic;
        if (hasBasic && target != SubscriptionPlan.Pro)
        {
            actions.Add(new("upgrade", "Cambiar a Pro", PlanUrls.CheckoutFor(SubscriptionPlan.Pro), false,
                "Tus días de Básico se vuelven días de Pro"));
        }

        return actions;
    }

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
/// (<see cref="SubscriptionReminders.RenewalNoticeFor"/>). No trae botón: justo abajo está «Renovar mi plan …», que
/// lleva a la sección de su plan en el checkout, y ahí nace el link de pago.
/// </summary>
/// <param name="IsUrgent">El día del vencimiento y en la gracia (alerta de advertencia).</param>
public sealed record RenewalNoticeViewModel(string Title, string Message, bool IsUrgent)
{
    /// <param name="plan">Plan que vence (<see cref="MyPlanViewModel.ActionPlanFor"/>): «Tu plan Básico vence…».</param>
    public static RenewalNoticeViewModel? From(SubscriptionReminder? notice, SubscriptionPlan plan, DateOnly today)
    {
        if (notice is null)
        {
            return null;
        }

        var name = plan.Label();
        var lastDay = SaleFormats.Date(notice.LastActiveDay, today);
        var graceDay = notice.LastGraceDay is { } g ? (g == today ? "hoy" : SaleFormats.Date(g, today)) : null;
        const string choose = "Elige tu paquete para renovar.";
        return notice.Stage switch
        {
            // La tarjeta del plan ya dice cuándo venció y hasta cuándo hay gracia: aquí va la llamada a renovar.
            SubscriptionReminderStage.Grace => new(
                graceDay == "hoy" ? $"Renueva hoy para no perder {name}" : $"Renueva antes del {graceDay} para no perder {name}",
                choose,
                IsUrgent: true),
            SubscriptionReminderStage.DueToday => new(
                $"Tu plan {name} vence hoy: renuévalo",
                (graceDay is null ? "" : $"Tienes hasta el {graceDay} sin perder {name}. ") + choose,
                IsUrgent: true),
            SubscriptionReminderStage.AnnualUpcoming => new(
                $"Tu plan {name} Anual vence el {lastDay}",
                $"{choose} Si renuevas el Anual antes de que terminen tus días de gracia, conservas tu precio.",
                IsUrgent: false),
            SubscriptionReminderStage.Upcoming => notice.EndingKind switch
            {
                SubscriptionPeriodKind.Paid => new($"Tu plan {name} vence {When(notice.DaysLeft)}", $"Vence el {lastDay}. {choose}", IsUrgent: false),
                SubscriptionPeriodKind.Trial => new($"Tu prueba termina {When(notice.DaysLeft)}",
                    $"Termina el {lastDay}; después tu plan vence y ya no puedes cobrar. Elige tu paquete para seguir.", IsUrgent: false),
                _ => new($"Tus días de {name} terminan {When(notice.DaysLeft)}",
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


/// <summary>Una tarjeta del paso 1 del checkout («Pro Anual»), con sus textos ya armados.</summary>
/// <param name="Value">Nombre del paquete (<c>data-plan-offer</c>).</param>
/// <param name="Url">Resumen del pedido de este paquete (paso 2).</param>
/// <param name="PerMonthText">«al mes» en el de 1 mes; «Equivale a $82.50 al mes» en el anual.</param>
/// <param name="Highlight">Solo en el anual: «12 meses por el precio de 10».</param>
/// <param name="IsCurrentPackage">Es el plan y el paquete del último pago (insignia «Tu paquete», 5.7).</param>
/// <param name="ListPriceText">Precio antes del descuento de bienvenida (tachado, 5.8); <c>null</c> sin descuento.</param>
public sealed record PlanOfferViewModel(
    string Value,
    string Url,
    string Title,
    string PriceText,
    string? PerMonthText,
    string? Highlight,
    string UntilText,
    bool IsFounderPrice,
    bool IsRecommended,
    bool IsCurrentPackage = false,
    string? ListPriceText = null)
{
    /// <summary>«al mes» va junto al precio (1 mes); «Equivale a … al mes» del anual, en su propio renglón.</summary>
    public bool IsPerMonthInline => Value == nameof(SubscriptionPackage.OneMonth);

    public static PlanOfferViewModel From(
        SubscriptionOffer offer, DateOnly today, SubscriptionPlan? currentPlan = null, SubscriptionPackage? currentPackage = null)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var amount = offer.Quote.Amount;
        var perMonth = offer.Package == SubscriptionPackage.OneMonth
            ? "al mes"
            : $"Equivale a {Money(offer.MonthlyEquivalent)} al mes";
        return new PlanOfferViewModel(
            offer.Package.ToString(),
            PlanUrls.CheckoutSummary(offer.Plan, offer.Package),
            TitleFor(offer.Plan, offer.Package),
            Money(amount),
            perMonth,
            AnnualDealTextFor(offer),
            $"Hasta el {LongDate(offer.LastDay)}",
            offer.IsFounderPrice,
            offer.Package == SubscriptionPackage.Annual,
            offer.Plan == currentPlan && offer.Package == currentPackage,
            offer.Quote.DiscountAmount > 0 ? Money(offer.Quote.ListPrice) : null);
    }

    /// <summary>Nombre de lo que se compra: «Básico (1 mes)», «Pro Anual».</summary>
    public static string TitleFor(SubscriptionPlan plan, SubscriptionPackage package) =>
        package == SubscriptionPackage.Annual ? $"{plan.Label()} Anual" : $"{plan.Label()} ({package.Label()})";

    /// <summary>«12 meses por el precio de 10» en el anual con meses de regalo (precios de lista); <c>null</c> si no.</summary>
    public static string? AnnualDealTextFor(SubscriptionOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return offer.Package == SubscriptionPackage.Annual && offer.GiftMonths > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{offer.Quote.Months} meses por el precio de {offer.PaidMonths}")
            : null;
    }

    /// <summary>«$199», o con centavos si los tiene: «$165.83».</summary>
    public static string Money(decimal amount) =>
        amount.ToString(amount == decimal.Truncate(amount) ? "$#,0" : "$#,0.00", CultureInfo.GetCultureInfo("es-MX"));

    /// <summary>«9 de diciembre de 2027», siempre con el año (fechas del pedido).</summary>
    public static string LongDate(DateOnly day) => day.ToString("d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es-MX"));
}

/// <summary>Un renglón del precio del pedido (resumen y comprobante).</summary>
/// <param name="Key">Para <c>data-order-row</c>: <c>list</c>, <c>founder</c>, <c>locked</c> o <c>welcome</c>.</param>
/// <param name="IsDiscount">Resta del precio de lista (se muestra con «−»).</param>
public sealed record OrderPriceRow(string Key, string Label, string AmountText, bool IsDiscount);

/// <summary>Un pedido en «Tus pedidos» (5.15): pagado, en proceso o en revisión.</summary>
/// <param name="Url">Comprobante si está pagado; si no, su estado.</param>
public sealed record OrderRow(
    int Id, string Number, string DateText, string Title, string AmountText, SubscriptionChargeStatus Status, string StatusText,
    string? MethodText, string Url, string LinkText)
{
    public bool IsPaid => Status == SubscriptionChargeStatus.Paid;

    public static OrderRow From(SubscriptionCharge charge, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(charge);
        var paid = charge.Status == SubscriptionChargeStatus.Paid;
        return new(
            charge.Id,
            SubscriptionPaths.OrderNumber(charge.Id),
            SaleFormats.Date(MexicoTime.DateOf(charge.PaidAt ?? charge.CreatedAt), today),
            $"{charge.Plan.Label()} · {charge.Package.Label()}",
            SaleQrViewModel.FormatAmount(charge.Amount),
            charge.Status,
            StatusTextOf(charge.Status),
            MethodTextOf(charge.Payments),
            paid ? PlanUrls.Receipt(charge.Id) : PlanUrls.PaymentStatus(charge.Id),
            paid ? "Comprobante" : "Ver estado");
    }

    public static string StatusTextOf(SubscriptionChargeStatus status) => status switch
    {
        SubscriptionChargeStatus.Pending => "Esperando pago",
        SubscriptionChargeStatus.Processing => "Procesando",
        SubscriptionChargeStatus.Paid => "Pagado",
        SubscriptionChargeStatus.Partial => "En revisión",
        SubscriptionChargeStatus.Failed => "Sin link",
        _ => status.ToString(),
    };

    /// <summary>«CoDi» o «Tarjeta», del último pago; <c>null</c> si Pey no lo dijo.</summary>
    public static string? MethodTextOf(IEnumerable<SubscriptionPayment> payments) =>
        payments.OrderByDescending(p => p.PaidAt).Select(p => p.PaymentMethod).FirstOrDefault() switch
        {
            "CODI" => "CoDi",
            "CARD" => "Tarjeta",
            null or "" => null,
            var other => other,
        };
}

/// <summary>Confirmación del pedido (/app/plan/pago/{id}, paso 4), al volver de la página de Pey.</summary>
/// <param name="PeriodText">«Del 10 de diciembre de 2026 al 9 de diciembre de 2027»: solo si ya se abonó.</param>
/// <param name="PayUrl">Página de pago de Pey, para volver si todavía no paga.</param>
/// <param name="SummaryUrl">Resumen del mismo pedido (para intentar de nuevo si falló el link).</param>
public sealed record SubscriptionPaymentViewModel(
    int Id,
    string OrderNumber,
    string Title,
    string AmountText,
    SubscriptionChargeStatus Status,
    string? PeriodText,
    string? PayUrl,
    string SummaryUrl)
{
    public bool IsPaid => Status == SubscriptionChargeStatus.Paid;

    /// <summary>Se sigue esperando la confirmación (la vista consulta cada 3 s).</summary>
    public bool IsWaiting => Status is SubscriptionChargeStatus.Pending or SubscriptionChargeStatus.Processing;

    public bool IsProcessing => Status == SubscriptionChargeStatus.Processing;

    public bool IsPartial => Status == SubscriptionChargeStatus.Partial;

    public bool IsFailed => Status == SubscriptionChargeStatus.Failed;

    /// <summary>«Tu plan está vigente hasta el …» del vendedor, con el último día en México; <c>null</c> si ya terminó.</summary>
    public static string? ProUntilTextFor(DateTimeOffset? proUntil, DateTimeOffset now) =>
        proUntil is { } until && until > now
            ? $"Tu plan está vigente hasta el {SaleFormats.Date(MexicoTime.DateOf(until.AddTicks(-1)), MexicoTime.Today(now))}."
            : null;

    /// <summary>«Del 10 de diciembre de 2026 al 9 de diciembre de 2027» (el último día en México).</summary>
    public static string PeriodTextFor(DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        $"Del {PlanOfferViewModel.LongDate(MexicoTime.DateOf(startsAt))} al {PlanOfferViewModel.LongDate(MexicoTime.DateOf(endsAt.AddTicks(-1)))}";
}

/// <summary>
/// Comprobante de un pedido pagado (/app/plan/pedidos/{id}/comprobante, 5.15). Solo lo guardado en el cargo: un precio
/// que cambió después no lo altera. No es una factura.
/// </summary>
/// <param name="PeriodText">Periodo pagado; <c>null</c> si no se encuentra (no debería pasar).</param>
public sealed record SubscriptionReceiptViewModel(
    int Id,
    string OrderNumber,
    string? BusinessName,
    string? Email,
    string Title,
    string? PeriodText,
    IReadOnlyList<OrderPriceRow> Rows,
    string TotalText,
    string BaseText,
    string VatText,
    string? MethodText,
    string? PaidAtText,
    string ShareUrl)
{
    public string ShareMessage => $"Comprobante del pedido {OrderNumber} de Local133: {Title}, {TotalText}.";

    /// <summary>Renglones del precio guardado en el cargo: de lista (de fundador si lo fue) y el descuento de bienvenida.</summary>
    public static IReadOnlyList<OrderPriceRow> RowsFor(SubscriptionCharge charge)
    {
        ArgumentNullException.ThrowIfNull(charge);
        var rows = new List<OrderPriceRow>
        {
            new("list", charge.IsFounderPrice ? "Precio de fundador" : "Precio", SaleQrViewModel.FormatAmount(charge.ListPrice), false),
        };
        if (charge.DiscountAmount > 0)
        {
            rows.Add(new("welcome", "Descuento de bienvenida", SaleQrViewModel.FormatAmount(charge.DiscountAmount), true));
        }

        return rows;
    }
}
