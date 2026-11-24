using Local133.Domain.Common;
using Local133.Domain.Settings;

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
    /// <param name="subscriptionChargeId">Cargo que lo pagó (5.6).</param>
    public SubscriptionPeriod AddPaidPeriod(
        SubscriptionPackage package, DateTimeOffset now, int graceDays, int? subscriptionChargeId = null)
    {
        var start = PlanStatus.NextPeriodStart(Periods, now, graceDays);
        var period = Add(SubscriptionPeriodKind.Paid, start, MexicoTime.AddMonths(start, package.Months()), now);
        period.SubscriptionChargeId = subscriptionChargeId;
        LastPackage = package;
        return period;
    }

    /// <summary>
    /// Precio de un paquete para este vendedor (§2, §6 y §7 del diseño): de fundador si ya lo es o si
    /// <paramref name="founderSlotAvailable"/>, el anual congelado solo si no ha bajado al gratuito (sigue en prueba,
    /// Pro o gracia), y el descuento de bienvenida si <paramref name="welcomeDiscount"/>.
    /// </summary>
    /// <param name="welcomeDiscount">
    /// Se registró con un código y todavía no se le abona ningún pago (5.8): 30 % de un mes en cualquier paquete.
    /// </param>
    /// <returns>La cotización y si salió con precio de fundador.</returns>
    public (SubscriptionQuote Quote, bool IsFounderPrice) QuoteFor(
        SubscriptionPackage package, SystemSettings settings, PlanStatus status, bool founderSlotAvailable,
        bool welcomeDiscount = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(status);
        var founder = IsFounder || founderSlotAvailable;
        var locked = status.IsProLike ? LockedAnnualPrice : null;
        return (SubscriptionPricing.Quote(package, settings, founder, locked, welcomeDiscount), founder);
    }

    /// <summary>Lugar de fundador (precio de fundador de por vida). Una sola vez.</summary>
    public void AssignFounder(int number)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        if (IsFounder)
        {
            throw new InvalidOperationException("El vendedor ya es fundador.");
        }

        IsFounder = true;
        FounderNumber = number;
    }

    /// <summary>Precio del anual que acaba de pagar: se respeta al renovar sin bajar al gratuito.</summary>
    public void LockAnnualPrice(decimal price)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);
        LockedAnnualPrice = price;
    }

    /// <summary>
    /// Periodo de días regalados (premio de referido o regalo del admin); empieza según
    /// <see cref="PlanStatus.NextPeriodStart"/>. No sirve para la prueba, que solo se da con <see cref="StartTrial"/>.
    /// </summary>
    /// <param name="referralId">Referido que lo ganó; solo en <see cref="SubscriptionPeriodKind.ReferralReward"/> (5.8).</param>
    public SubscriptionPeriod AddDaysPeriod(
        SubscriptionPeriodKind kind, int days, DateTimeOffset now, int graceDays, int? referralId = null)
    {
        if (kind is not (SubscriptionPeriodKind.ReferralReward or SubscriptionPeriodKind.AdminGrant))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Solo los premios y los regalos se dan por días.");
        }

        if (referralId is not null && kind != SubscriptionPeriodKind.ReferralReward)
        {
            throw new ArgumentException("Solo un premio de referido lleva el referido.", nameof(referralId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var start = PlanStatus.NextPeriodStart(Periods, now, graceDays);
        var period = Add(kind, start, MexicoTime.AddDays(start, days), now);
        period.ReferralId = referralId;
        return period;
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
