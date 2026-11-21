using Local133.Domain.Common;
using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Un paquete como se ofrece en «Mi plan» (§2 del diseño).
/// </summary>
/// <param name="MonthlyEquivalent">Lo que sale cada mes (monto ÷ meses), redondeado a centavos.</param>
/// <param name="AnnualUpgradeDifference">Solo en 6 meses: lo que falta para el año completo («Por $796 más…»).</param>
/// <param name="GiftMonths">Solo en el anual: meses de regalo frente al precio mensual (12 − anual ÷ mensual).</param>
/// <param name="ProUntil">Hasta dónde llegaría Pro (excluido) si paga hoy, encadenado a lo que ya tiene.</param>
public sealed record SubscriptionOffer(
    SubscriptionPackage Package,
    SubscriptionQuote Quote,
    bool IsFounderPrice,
    decimal MonthlyEquivalent,
    decimal? AnnualUpgradeDifference,
    int GiftMonths,
    DateTimeOffset ProUntil)
{
    /// <summary>Último día (en México) de Pro si paga hoy.</summary>
    public DateOnly LastProDay => MexicoTime.DateOf(ProUntil.AddTicks(-1));
}

public static class SubscriptionOffers
{
    public static IReadOnlyList<SubscriptionOffer> For(
        Subscription subscription, SystemSettings settings, DateTimeOffset now, bool founderSlotAvailable)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(settings);
        var status = subscription.StatusAt(now, settings.GraceDays);
        var start = PlanStatus.NextPeriodStart(subscription.Periods, now, settings.GraceDays);
        var quotes = Enum.GetValues<SubscriptionPackage>()
            .Select(p => subscription.QuoteFor(p, settings, status, founderSlotAvailable))
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
                ? Math.Max(0, quote.Months - (int)Math.Floor(quote.Amount / monthly.ListPrice))
                : 0;
            return new SubscriptionOffer(
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
