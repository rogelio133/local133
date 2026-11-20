using Local133.Domain.Common;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Suscripción del vendedor (una por vendedor). Nace con la prueba, cuando su cuenta bancaria queda activa. Los días
/// de Pro están en <see cref="Periods"/>; el estado del plan se calcula con ellos (<see cref="PlanStatus.For"/>).
/// Para agregar periodos hay que cargar <see cref="Periods"/>.
/// </summary>
public class Subscription : Entity
{
    public int MerchantId { get; set; }

    /// <summary>Inicio de la prueba (una sola vez por vendedor).</summary>
    public DateTimeOffset TrialStartedAt { get; set; }

    /// <summary>Fin (excluido) del último periodo: el máximo de <c>EndsAt</c>, guardado para consultar rápido.</summary>
    public DateTimeOffset ProUntil { get; set; }

    /// <summary>Origen del último periodo.</summary>
    public SubscriptionPeriodKind LastPeriodKind { get; set; }

    /// <summary>Paquete del último pago: define el link de renovación y los recordatorios (5.6, 5.7).</summary>
    public SubscriptionPackage? LastPackage { get; set; }

    /// <summary>Tiene precio de fundador (5.6).</summary>
    public bool IsFounder { get; set; }

    /// <summary>Lugar de fundador (1 a <c>FounderSlots</c>), único.</summary>
    public int? FounderNumber { get; set; }

    /// <summary>Precio del último anual pagado: se respeta al renovar sin bajar al gratuito (5.6).</summary>
    public decimal? LockedAnnualPrice { get; set; }

    public List<SubscriptionPeriod> Periods { get; set; } = [];

    /// <summary>Crea la suscripción con la prueba de <paramref name="trialDays"/> días desde <paramref name="now"/>.</summary>
    public static Subscription StartTrial(int merchantId, DateTimeOffset now, int trialDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(trialDays);
        var trial = SubscriptionPeriod.Create(merchantId, SubscriptionPeriodKind.Trial, now, MexicoTime.AddDays(now, trialDays), now);
        return new Subscription
        {
            MerchantId = merchantId,
            TrialStartedAt = now,
            ProUntil = trial.EndsAt,
            LastPeriodKind = SubscriptionPeriodKind.Trial,
            Periods = [trial],
        };
    }

    public PlanStatus StatusAt(DateTimeOffset now, int graceDays) => PlanStatus.For(Periods, now, graceDays);

    /// <summary>Periodo pagado con un paquete; empieza según <see cref="PlanStatus.NextPeriodStart"/>.</summary>
    public SubscriptionPeriod AddPaidPeriod(SubscriptionPackage package, DateTimeOffset now, int graceDays)
    {
        var start = PlanStatus.NextPeriodStart(Periods, now, graceDays);
        var period = Add(SubscriptionPeriodKind.Paid, start, MexicoTime.AddMonths(start, package.Months()), now);
        LastPackage = package;
        return period;
    }

    /// <summary>
    /// Periodo de días regalados (premio de referido o regalo del admin); empieza según
    /// <see cref="PlanStatus.NextPeriodStart"/>. No sirve para la prueba, que solo se da con <see cref="StartTrial"/>.
    /// </summary>
    public SubscriptionPeriod AddDaysPeriod(SubscriptionPeriodKind kind, int days, DateTimeOffset now, int graceDays)
    {
        if (kind is not (SubscriptionPeriodKind.ReferralReward or SubscriptionPeriodKind.AdminGrant))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Solo los premios y los regalos se dan por días.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var start = PlanStatus.NextPeriodStart(Periods, now, graceDays);
        return Add(kind, start, MexicoTime.AddDays(start, days), now);
    }

    private SubscriptionPeriod Add(SubscriptionPeriodKind kind, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        var period = SubscriptionPeriod.Create(MerchantId, kind, start, end, now);
        Periods.Add(period);
        if (period.EndsAt >= ProUntil)
        {
            ProUntil = period.EndsAt;
            LastPeriodKind = kind;
        }

        return period;
    }
}
