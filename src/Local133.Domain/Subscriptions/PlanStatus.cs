using Local133.Domain.Common;
using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Estado del plan en un instante, calculado con la historia de periodos (§3 del diseño de suscripciones):
/// <list type="bullet">
/// <item>sin prueba ⇒ <see cref="PlanTier.Onboarding"/> (la prueba nace al activarse la cuenta bancaria);</item>
/// <item>un periodo cubre el instante ⇒ <see cref="PlanTier.Trial"/> si es la prueba, <see cref="PlanTier.Pro"/> si no;</item>
/// <item>venció un periodo <b>pagado</b> hace menos de <c>GraceDays</c> ⇒ <see cref="PlanTier.Grace"/>;</item>
/// <item>si no ⇒ <see cref="PlanTier.Free"/>. Al terminar la prueba o un premio no hay gracia.</item>
/// </list>
/// Todo va al instante; los días se suman en hora de México.
/// </summary>
/// <param name="ProUntil">Fin (excluido) del último periodo; <c>null</c> en el onboarding.</param>
/// <param name="GraceEndsAt">Fin (excluido) de la gracia, solo si el último periodo es pagado.</param>
public sealed record PlanStatus(PlanTier Tier, DateTimeOffset? ProUntil, DateTimeOffset? GraceEndsAt)
{
    public static PlanStatus Onboarding { get; } = new(PlanTier.Onboarding, null, null);

    /// <summary>Tiene las funciones de Pro: prueba, Pro o gracia.</summary>
    public bool IsProLike => Tier is PlanTier.Trial or PlanTier.Pro or PlanTier.Grace;

    /// <summary>Último día (en México) con Pro: el del instante anterior a <see cref="ProUntil"/>.</summary>
    public DateOnly? LastProDay => ProUntil is { } until ? LastDayBefore(until) : null;

    /// <summary>Último día (en México) de la gracia.</summary>
    public DateOnly? LastGraceDay => GraceEndsAt is { } until ? LastDayBefore(until) : null;

    public static PlanStatus For(IEnumerable<SubscriptionPeriod> periods, DateTimeOffset now, SystemSettings settings) =>
        For(periods, now, settings.GraceDays);

    public static PlanStatus For(IEnumerable<SubscriptionPeriod> periods, DateTimeOffset now, int graceDays)
    {
        ArgumentNullException.ThrowIfNull(periods);
        ArgumentOutOfRangeException.ThrowIfNegative(graceDays);
        var list = periods as IReadOnlyCollection<SubscriptionPeriod> ?? periods.ToList();
        if (!list.Any(p => p.Kind == SubscriptionPeriodKind.Trial))
        {
            return Onboarding;
        }

        var last = list.MaxBy(p => p.EndsAt)!;
        DateTimeOffset? graceEndsAt = last.Kind == SubscriptionPeriodKind.Paid ? MexicoTime.AddDays(last.EndsAt, graceDays) : null;

        var covering = list.Where(p => p.Covers(now)).ToList();
        PlanTier tier;
        if (covering.Count > 0)
        {
            tier = covering.All(p => p.Kind == SubscriptionPeriodKind.Trial) ? PlanTier.Trial : PlanTier.Pro;
        }
        else if (graceEndsAt is { } graceEnd && now >= last.EndsAt && now < graceEnd)
        {
            tier = PlanTier.Grace;
        }
        else
        {
            tier = PlanTier.Free;
        }

        return new PlanStatus(tier, last.EndsAt, graceEndsAt);
    }

    /// <summary>
    /// Dónde empieza un periodo nuevo: donde termina el anterior si sigue con Pro o está en gracia (pagar en la gracia
    /// no regala días), o en ese momento si está en el gratuito.
    /// </summary>
    public static DateTimeOffset NextPeriodStart(IEnumerable<SubscriptionPeriod> periods, DateTimeOffset now, int graceDays)
    {
        var status = For(periods, now, graceDays);
        return status.IsProLike ? status.ProUntil!.Value : now;
    }

    /// <summary>
    /// Días que le quedan de Pro, por calendario de México: 0 si el último día es hoy. <c>null</c> en el onboarding.
    /// </summary>
    public int? DaysLeft(DateTimeOffset now) =>
        LastProDay is { } last ? Math.Max(0, last.DayNumber - MexicoTime.Today(now).DayNumber) : null;

    private static DateOnly LastDayBefore(DateTimeOffset end) => MexicoTime.DateOf(end.AddTicks(-1));
}
