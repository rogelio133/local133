using Local133.Domain.Common;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Un tramo de Pro del vendedor: <c>[StartsAt, EndsAt)</c>, al instante. Los periodos se encadenan (ver
/// <see cref="Subscription.AddPaidPeriod"/> y <see cref="Subscription.AddDaysPeriod"/>) y son la historia de la que sale el estado del plan.
/// </summary>
public class SubscriptionPeriod : Entity
{
    public int MerchantId { get; set; }

    public SubscriptionPeriodKind Kind { get; set; }

    /// <summary>Inicio, incluido.</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Fin, excluido.</summary>
    public DateTimeOffset EndsAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Cargo que lo pagó (solo en periodos <see cref="SubscriptionPeriodKind.Paid"/>).</summary>
    public int? SubscriptionChargeId { get; set; }

    /// <summary>El periodo cubre ese instante.</summary>
    public bool Covers(DateTimeOffset instant) => StartsAt <= instant && instant < EndsAt;

    internal static SubscriptionPeriod Create(
        int merchantId, SubscriptionPeriodKind kind, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now)
    {
        if (endsAt <= startsAt)
        {
            throw new ArgumentException("El periodo debe terminar después de empezar.", nameof(endsAt));
        }

        return new SubscriptionPeriod
        {
            MerchantId = merchantId,
            Kind = kind,
            StartsAt = startsAt,
            EndsAt = endsAt,
            CreatedAt = now,
        };
    }
}
