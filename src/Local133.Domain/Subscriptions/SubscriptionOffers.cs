using Local133.Domain.Common;
using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Un paquete de un plan como se ofrece al vendedor (§2 y §6 del diseño). Solo 1 mes y Anual
/// (<see cref="SubscriptionPackages.Offered"/>).
/// </summary>
/// <param name="MonthlyEquivalent">Lo que sale cada mes (monto ÷ meses), redondeado a centavos.</param>
/// <param name="GiftMonths">
/// Solo en el anual: meses de regalo frente al precio mensual (12 − anual ÷ mensual), con los precios de lista: el
/// descuento de bienvenida no los cambia.
/// </param>
/// <param name="Starts">Dónde empezaría lo pagado si paga hoy (el mismo cálculo que el abono).</param>
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
    int GiftMonths,
    DateTimeOffset Starts,
    DateTimeOffset Until)
{
    /// <summary>Día (en México) en que empezaría.</summary>
    public DateOnly FirstDay => MexicoTime.DateOf(Starts);

    /// <summary>Último día (en México) del paquete si paga hoy.</summary>
    public DateOnly LastDay => MexicoTime.DateOf(Until.AddTicks(-1));

    /// <summary>Meses que se pagan en el anual («12 meses por el precio de 10»): meses del paquete menos los de regalo.</summary>
    public int PaidMonths => Quote.Months - GiftMonths;
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
        var start = subscription.NextPeriodStartFor(
            plan, now, settings.GraceDays, SubscriptionPricing.ConversionRatio(settings, subscription.IsFounder || founderSlotAvailable));
        var quotes = SubscriptionPackages.Offered
            .Select(p => subscription.QuoteFor(plan, p, settings, status, founderSlotAvailable, welcomeDiscount))
            .ToList();
        var monthly = quotes.Single(q => q.Quote.Package == SubscriptionPackage.OneMonth).Quote;

        return quotes.Select(q =>
        {
            var quote = q.Quote;
            var gift = quote.Package == SubscriptionPackage.Annual && monthly.ListPrice > 0
                ? Math.Max(0, quote.Months - (int)Math.Floor(quote.ListPrice / monthly.ListPrice))
                : 0;
            return new SubscriptionOffer(
                plan,
                quote.Package,
                quote,
                q.IsFounderPrice,
                Math.Round(quote.Amount / quote.Months, 2, MidpointRounding.AwayFromZero),
                gift,
                start,
                MexicoTime.AddMonths(start, quote.Months));
        }).ToList();
    }
}
