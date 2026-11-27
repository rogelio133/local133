using Local133.Domain.Common;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Un tramo del plan del vendedor (<see cref="Plan"/>): <c>[StartsAt, EndsAt)</c>, al instante. Los periodos se encadenan (ver
/// <see cref="Subscription.AddPaidPeriod"/> y <see cref="Subscription.AddDaysPeriod"/>) y son la historia de la que sale el estado del plan.
/// </summary>
public class SubscriptionPeriod : Entity
{
    public int MerchantId { get; set; }

    public SubscriptionPeriodKind Kind { get; set; }

    /// <summary>Plan cuyas funciones da el periodo. La prueba es de Pro; los periodos de antes del 2026-12 quedaron en Pro.</summary>
    public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Pro;

    /// <summary>Inicio, incluido.</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Fin, excluido.</summary>
    public DateTimeOffset EndsAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Cargo que lo pagó: en periodos <see cref="SubscriptionPeriodKind.Paid"/>, o en un <c>Conversion</c> que salió de un
    /// Básico pagado que todavía no empezaba.
    /// </summary>
    public int? SubscriptionChargeId { get; set; }

    /// <summary>
    /// Referido que lo ganó: en periodos <see cref="SubscriptionPeriodKind.ReferralReward"/> (5.8), o en un
    /// <c>Conversion</c> que salió de un premio de Básico que todavía no empezaba.
    /// </summary>
    public int? ReferralId { get; set; }

    /// <summary>El periodo cubre ese instante.</summary>
    public bool Covers(DateTimeOffset instant) => StartsAt <= instant && instant < EndsAt;

    /// <summary>Copia suelta (sin id ni seguimiento) para simular cambios sin tocar la suscripción.</summary>
    internal SubscriptionPeriod Copy() => (SubscriptionPeriod)MemberwiseClone();

    internal static SubscriptionPeriod Create(
        int merchantId, SubscriptionPeriodKind kind, SubscriptionPlan plan, DateTimeOffset startsAt, DateTimeOffset endsAt,
        DateTimeOffset now)
    {
        if (endsAt <= startsAt)
        {
            throw new ArgumentException("El periodo debe terminar después de empezar.", nameof(endsAt));
        }

        return new SubscriptionPeriod
        {
            MerchantId = merchantId,
            Kind = kind,
            Plan = plan,
            StartsAt = startsAt,
            EndsAt = endsAt,
            CreatedAt = now,
        };
    }
}
