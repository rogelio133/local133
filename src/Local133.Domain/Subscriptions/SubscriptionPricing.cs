using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>Precio de un paquete de un plan, como se guardará en el cargo (5.6). Todo con IVA incluido.</summary>
/// <param name="ListPrice">Precio antes del descuento.</param>
/// <param name="Amount">Lo que se cobra: <c>ListPrice − DiscountAmount</c>.</param>
public sealed record SubscriptionQuote(
    SubscriptionPlan Plan,
    SubscriptionPackage Package,
    int Months,
    decimal ListPrice,
    decimal DiscountAmount,
    SubscriptionDiscountReason? DiscountReason,
    decimal Amount);

/// <summary>
/// Precios de la suscripción (§2 del diseño de planes Básico y Pro), con IVA incluido y tomados de
/// <see cref="SystemSettings"/>: 1, 3 y 6 meses = n × precio mensual del plan; anual = precio anual del plan; el
/// fundador usa los precios de fundador del plan.
/// </summary>
public static class SubscriptionPricing
{
    /// <param name="lockedAnnualPrice">
    /// Precio congelado del anual. Quien llama decide si aplica: solo al renovar el anual del mismo plan sin haber
    /// vencido. Si el precio vigente es menor, se cobra el vigente.
    /// </param>
    /// <param name="applyWelcomeDiscount">
    /// Primer pago de un referido: descuento de <c>ReferralWelcomeDiscountRate</c> sobre el precio de un mes del plan
    /// que le toca (normal o de fundador), en cualquier paquete.
    /// </param>
    public static SubscriptionQuote Quote(
        SubscriptionPlan plan,
        SubscriptionPackage package,
        SystemSettings settings,
        bool isFounder = false,
        decimal? lockedAnnualPrice = null,
        bool applyWelcomeDiscount = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var monthly = MonthlyPrice(plan, settings, isFounder);
        var months = package.Months();
        var listPrice = package == SubscriptionPackage.Annual
            ? LockedOrCurrent(AnnualPrice(plan, settings, isFounder), lockedAnnualPrice)
            : monthly * months;

        var discount = applyWelcomeDiscount
            ? Math.Min(listPrice, Math.Round(monthly * settings.ReferralWelcomeDiscountRate, 2, MidpointRounding.AwayFromZero))
            : 0m;

        return new SubscriptionQuote(
            plan,
            package,
            months,
            listPrice,
            discount,
            discount > 0 ? SubscriptionDiscountReason.ReferralWelcome : null,
            listPrice - discount);
    }

    /// <summary>Precio de un mes del plan (de fundador si <paramref name="isFounder"/>).</summary>
    public static decimal MonthlyPrice(SubscriptionPlan plan, SystemSettings settings, bool isFounder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return plan switch
        {
            SubscriptionPlan.Basic => isFounder ? settings.BasicFounderMonthlyPrice : settings.BasicMonthlyPrice,
            SubscriptionPlan.Pro => isFounder ? settings.ProFounderMonthlyPrice : settings.ProMonthlyPrice,
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Plan desconocido."),
        };
    }

    /// <summary>Precio del anual del plan (de fundador si <paramref name="isFounder"/>), sin congelado.</summary>
    public static decimal AnnualPrice(SubscriptionPlan plan, SystemSettings settings, bool isFounder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return plan switch
        {
            SubscriptionPlan.Basic => isFounder ? settings.BasicFounderAnnualPrice : settings.BasicAnnualPrice,
            SubscriptionPlan.Pro => isFounder ? settings.ProFounderAnnualPrice : settings.ProAnnualPrice,
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Plan desconocido."),
        };
    }

    /// <summary>
    /// Cuánto vale un día de Básico en días de Pro al subir de plan (§3): el precio mensual de Básico ÷ el de Pro que le
    /// toca al vendedor, 99/199 o, de fundador, 69/149. Nunca más de 1.
    /// </summary>
    public static decimal ConversionRatio(SystemSettings settings, bool isFounder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var pro = MonthlyPrice(SubscriptionPlan.Pro, settings, isFounder);
        var basic = MonthlyPrice(SubscriptionPlan.Basic, settings, isFounder);
        return pro <= 0 ? 0m : Math.Min(1m, basic / pro);
    }

    /// <summary>
    /// Desglose del IVA incluido en un precio: $199 con 16 % ⇒ $171.55 de servicio y $27.45 de IVA (servirá para el CFDI).
    /// </summary>
    public static (decimal Base, decimal Vat) VatBreakdown(decimal amount, decimal vatRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfNegative(vatRate);
        var @base = Math.Round(amount / (1 + vatRate), 2, MidpointRounding.AwayFromZero);
        return (@base, amount - @base);
    }

    private static decimal LockedOrCurrent(decimal current, decimal? locked) =>
        locked is { } price && price > 0 ? Math.Min(price, current) : current;
}
