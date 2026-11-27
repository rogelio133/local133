using Local133.Domain.Common;
using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Un paquete de un plan como se ofrece al vendedor (§2 y §6 del diseño).
/// </summary>
/// <param name="MonthlyEquivalent">Lo que sale cada mes (monto ÷ meses), redondeado a centavos.</param>
/// <param name="AnnualUpgradeDifference">Solo en 6 meses: lo que falta para el año completo («Por $396 más…»).</param>
/// <param name="GiftMonths">
/// Solo en el anual: meses de regalo frente al precio mensual (12 − anual ÷ mensual), con los precios de lista: el
/// descuento de bienvenida no los cambia.
/// </param>
/// <param name="Until">
/// Hasta dónde llegaría (excluido) si paga hoy, encadenado a lo que ya tiene; en Pro, después de convertir el Básico
/// que tenga por delante.
/// </param>
public sealed record SubscriptionOffer(
    SubscriptionPlan Plan,
    SubscriptionPackage Package,
    SubscriptionQuote Quote,
    bool IsFounderPrice,
    decimal MonthlyEquivalent,
    decimal? AnnualUpgradeDifference,
    int GiftMonths,
    DateTimeOffset Until)
{
    /// <summary>Último día (en México) del paquete si paga hoy.</summary>
    public DateOnly LastDay => MexicoTime.DateOf(Until.AddTicks(-1));
}

public static class SubscriptionOffers
{
    /// <param name="welcomeDiscount">Descuento de bienvenida del referido (5.8, ver <see cref="Subscription.QuoteFor"/>).</param>
    public static IReadOnlyList<SubscriptionOffer> For(
        Subscription subscription, SubscriptionPlan plan, SystemSettings settings, DateTimeOffset now,
        bool founderSlotAvailable, bool welcomeDiscount = false)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(settings);
        var status = subscription.StatusAt(now, settings.GraceDays);
        var founder = subscription.IsFounder || founderSlotAvailable;
        var start = subscription.NextPeriodStartFor(
            plan, now, settings.GraceDays, SubscriptionPricing.ConversionRatio(settings, founder));
        var quotes = Enum.GetValues<SubscriptionPackage>()
            .Select(p => subscription.QuoteFor(plan, p, settings, status, founderSlotAvailable, welcomeDiscount))
            .ToDictionary(q => q.Quote.Package);

        var annual = quotes[SubscriptionPackage.Annual].Quote;
        var monthly = quotes[SubscriptionPackage.OneMonth].Quote;

        return quotes.Values.Select(q =>
        {
            var quote = q.Quote;
            decimal? upgrade = quote.Package == SubscriptionPackage.SixMonths && annual.Amount > quote.Amount
                ? annual.Amount - quote.Amount
                : null;
            var gift = quote.Package == SubscriptionPackage.Annual && monthly.ListPrice > 0
                ? Math.Max(0, quote.Months - (int)Math.Floor(quote.ListPrice / monthly.ListPrice))
                : 0;
            return new SubscriptionOffer(
                plan,
                quote.Package,
                quote,
                q.IsFounderPrice,
                Math.Round(quote.Amount / quote.Months, 2, MidpointRounding.AwayFromZero),
                upgrade,
                gift,
                MexicoTime.AddMonths(start, quote.Months));
        }).OrderBy(o => o.Package).ToList();
    }
}
