using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>Qué pasa con lo que ya tiene el vendedor si paga el pedido hoy (resumen del pedido, §6 del diseño).</summary>
public enum SubscriptionOrderTimeline
{
    /// <summary>No tiene nada vigente: empieza hoy.</summary>
    StartsNow,

    /// <summary>Se encadena al final de lo que tiene: «Tu Básico empieza el 9/dic, cuando termina tu Pro».</summary>
    AfterCurrent,

    /// <summary>Sube de Básico a Pro: «Tus 20 días de Básico se vuelven unos 9 días de Pro desde hoy».</summary>
    ConvertsBasic,

    /// <summary>Está en la gracia: cuenta desde que venció (pagar en la gracia no regala días).</summary>
    FromGrace,
}

/// <summary>
/// Resumen de un pedido (5.15, paso 2 del checkout): la oferta del paquete con el desglose del precio y lo que pasa con lo
/// que ya tiene. Se cotiza al mostrarlo; el abono vuelve a calcular todo al instante del pago, así que los días de la
/// conversión son aproximados («unos 9 días»).
/// </summary>
/// <param name="RegularPrice">Precio de lista del paquete, sin fundador ni anual congelado.</param>
/// <param name="PriceAdjustment">
/// Lo que baja el precio de fundador o el anual congelado (<c>RegularPrice − Quote.ListPrice</c>); 0 si no aplica.
/// </param>
/// <param name="Vat">IVA incluido en el total.</param>
/// <param name="PrecedingPlan">En <see cref="SubscriptionOrderTimeline.AfterCurrent"/>: el plan del final de su cadena.</param>
/// <param name="PrecedingIsTrial">En <see cref="SubscriptionOrderTimeline.AfterCurrent"/>: lo que termina antes es la prueba.</param>
/// <param name="BasicDays">En la conversión: días completos de Básico que le quedan.</param>
/// <param name="ProDays">En la conversión: días completos de Pro que le darían, redondeados hacia abajo.</param>
/// <param name="ConvertsFromNow">En la conversión: tiene Básico en curso (los días de Pro empiezan hoy).</param>
public sealed record SubscriptionOrder(
    SubscriptionOffer Offer,
    decimal RegularPrice,
    decimal PriceAdjustment,
    decimal Vat,
    SubscriptionOrderTimeline Timeline,
    SubscriptionPlan? PrecedingPlan,
    bool PrecedingIsTrial,
    int BasicDays,
    int ProDays,
    bool ConvertsFromNow)
{
    public SubscriptionPlan Plan => Offer.Plan;

    public SubscriptionPackage Package => Offer.Package;

    public SubscriptionQuote Quote => Offer.Quote;

    public bool IsFounderPrice => Offer.IsFounderPrice;

    /// <summary>Descuento de bienvenida del referido.</summary>
    public decimal WelcomeDiscount => Offer.Quote.DiscountAmount;

    /// <summary>Lo que paga en Pey.</summary>
    public decimal Total => Offer.Quote.Amount;

    public static SubscriptionOrder For(
        Subscription subscription, SubscriptionPlan plan, SubscriptionPackage package, SystemSettings settings,
        DateTimeOffset now, bool founderSlotAvailable, bool welcomeDiscount = false)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(settings);
        if (!package.IsOffered())
        {
            throw new ArgumentOutOfRangeException(nameof(package), package, "Paquete que ya no se ofrece.");
        }

        var offer = SubscriptionOffers.For(subscription, plan, settings, now, founderSlotAvailable, welcomeDiscount)
            .Single(o => o.Package == package);
        var regular = SubscriptionPricing.Quote(plan, package, settings).ListPrice;
        var adjustment = Math.Max(0, regular - offer.Quote.ListPrice);
        var vat = SubscriptionPricing.VatBreakdown(offer.Quote.Amount, settings.VatRate).Vat;

        var status = subscription.StatusAt(now, settings.GraceDays);
        var basicAhead = subscription.BasicTimeAhead(now);
        if (plan == SubscriptionPlan.Pro && basicAhead > TimeSpan.Zero && status.Tier != PlanTier.Grace)
        {
            var ratio = SubscriptionPricing.ConversionRatio(settings, subscription.IsFounder || founderSlotAvailable);
            var pro = TimeSpan.FromTicks((long)Math.Floor(basicAhead.Ticks * ratio));
            var fromNow = subscription.Periods.Any(p => p.Plan == SubscriptionPlan.Basic && p.StartsAt <= now && p.EndsAt > now);
            return new SubscriptionOrder(offer, regular, adjustment, vat, SubscriptionOrderTimeline.ConvertsBasic, null, false,
                (int)Math.Floor(basicAhead.TotalDays), (int)Math.Floor(pro.TotalDays), fromNow);
        }

        if (status.Tier == PlanTier.Grace)
        {
            return new SubscriptionOrder(offer, regular, adjustment, vat, SubscriptionOrderTimeline.FromGrace, null, false, 0, 0, false);
        }

        if (offer.Starts > now)
        {
            var last = subscription.Periods.OrderBy(p => p.EndsAt).ThenBy(p => p.CreatedAt).Last();
            return new SubscriptionOrder(offer, regular, adjustment, vat, SubscriptionOrderTimeline.AfterCurrent,
                last.Plan, last.Kind == SubscriptionPeriodKind.Trial, 0, 0, false);
        }

        return new SubscriptionOrder(offer, regular, adjustment, vat, SubscriptionOrderTimeline.StartsNow, null, false, 0, 0, false);
    }
}
