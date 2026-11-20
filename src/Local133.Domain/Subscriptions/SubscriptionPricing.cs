using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>Precio de un paquete, como se guardará en el cargo (5.6). Todo con IVA incluido.</summary>
/// <param name="ListPrice">Precio antes del descuento.</param>
/// <param name="Amount">Lo que se cobra: <c>ListPrice − DiscountAmount</c>.</param>
public sealed record SubscriptionQuote(
    SubscriptionPackage Package,
    int Months,
    decimal ListPrice,
    decimal DiscountAmount,
    SubscriptionDiscountReason? DiscountReason,
    decimal Amount);

/// <summary>
/// Precios de la suscripción (§2 y §6 del diseño), con IVA incluido y tomados de <see cref="SystemSettings"/>:
/// 1, 3 y 6 meses = n × precio mensual; anual = precio anual; el fundador usa sus propios precios.
/// </summary>
public static class SubscriptionPricing
{
    /// <param name="lockedAnnualPrice">
    /// Precio congelado del anual. Quien llama decide si aplica: solo al renovar el anual sin haber bajado al gratuito.
    /// Si el precio vigente es menor, se cobra el vigente.
    /// </param>
    /// <param name="applyWelcomeDiscount">
    /// Primer pago de un referido: descuento de <c>ReferralWelcomeDiscountRate</c> sobre el precio de un mes que le
    /// toca (normal o de fundador), en cualquier paquete.
    /// </param>
    public static SubscriptionQuote Quote(
        SubscriptionPackage package,
        SystemSettings settings,
        bool isFounder = false,
        decimal? lockedAnnualPrice = null,
        bool applyWelcomeDiscount = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var monthly = isFounder ? settings.FounderMonthlyPrice : settings.MonthlyPrice;
        var months = package.Months();
        var listPrice = package == SubscriptionPackage.Annual
            ? AnnualPrice(isFounder ? settings.FounderAnnualPrice : settings.AnnualPrice, lockedAnnualPrice)
            : monthly * months;

        var discount = applyWelcomeDiscount
            ? Math.Min(listPrice, Math.Round(monthly * settings.ReferralWelcomeDiscountRate, 2, MidpointRounding.AwayFromZero))
            : 0m;

        return new SubscriptionQuote(
            package,
            months,
            listPrice,
            discount,
            discount > 0 ? SubscriptionDiscountReason.ReferralWelcome : null,
            listPrice - discount);
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

    private static decimal AnnualPrice(decimal current, decimal? locked) =>
        locked is { } price && price > 0 ? Math.Min(price, current) : current;
}
