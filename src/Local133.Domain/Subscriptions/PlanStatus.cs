using Local133.Domain.Common;
using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Estado del plan en un instante, calculado con la historia de periodos (§3 del diseño de planes Básico y Pro,
/// 2026-11-26):
/// <list type="bullet">
/// <item>sin prueba ⇒ <see cref="PlanTier.Onboarding"/>;</item>
/// <item>un periodo cubre el instante ⇒ <see cref="PlanTier.Trial"/> si es la prueba; si no, <see cref="PlanTier.Pro"/>
/// o <see cref="PlanTier.Basic"/> según su plan. La prueba con un periodo <b>pagado de Pro</b> ya abonado después
/// cuenta como <see cref="PlanTier.Pro"/>: el vendedor ya pagó. Si lo pagado es Básico, sigue en la prueba;</item>
/// <item>venció un periodo <b>pagado</b> hace menos de <c>GraceDays</c> ⇒ <see cref="PlanTier.Grace"/>, con las
/// funciones del plan que pagó;</item>
/// <item>si no ⇒ <see cref="PlanTier.Expired"/>. Al terminar la prueba, un premio, un regalo, una conversión o una
/// transición no hay gracia.</item>
/// </list>
/// Todo va al instante; los días se suman en hora de México.
/// </summary>
/// <param name="ProUntil">Vigente hasta (excluido): fin del último periodo, de cualquier plan; <c>null</c> en el onboarding.</param>
/// <param name="GraceEndsAt">Fin (excluido) de la gracia, solo si el último periodo es pagado.</param>
public sealed record PlanStatus(PlanTier Tier, DateTimeOffset? ProUntil, DateTimeOffset? GraceEndsAt)
{
    public static PlanStatus Onboarding { get; } = new(PlanTier.Onboarding, null, null);

    /// <summary>
    /// Plan cuyas funciones aplican en este instante: Pro en la prueba, el del último periodo pagado en la gracia y
    /// <c>null</c> en el onboarding y en el vencido.
    /// </summary>
    public SubscriptionPlan? Plan { get; init; }

    /// <summary>Tiene un plan vigente: prueba, Básico, Pro o gracia.</summary>
    public bool IsCurrent => Tier is PlanTier.Trial or PlanTier.Basic or PlanTier.Pro or PlanTier.Grace;

    /// <summary>Último día (en México) vigente: el del instante anterior a <see cref="ProUntil"/>.</summary>
    public DateOnly? LastActiveDay => ProUntil is { } until ? LastDayBefore(until) : null;

    /// <summary>Último día (en México) de la gracia.</summary>
    public DateOnly? LastGraceDay => GraceEndsAt is { } until ? LastDayBefore(until) : null;

    /// <summary>
    /// Fin (excluido) de la prueba, solo mientras todavía cubre el instante. Con un pago de Pro ya abonado el estado es
    /// <see cref="PlanTier.Pro"/> y esto dice cuándo empieza lo pagado.
    /// </summary>
    public DateTimeOffset? TrialEndsAt { get; init; }

    /// <summary>Último día (en México) de la prueba en curso.</summary>
    public DateOnly? LastTrialDay => TrialEndsAt is { } until ? LastDayBefore(until) : null;

    /// <summary>
    /// Fin (excluido) del tramo seguido de <see cref="Plan"/> que cubre el instante (la prueba y lo pagado de Pro
    /// cuentan juntos). Solo en la prueba, Básico y Pro.
    /// </summary>
    public DateTimeOffset? PlanEndsAt { get; init; }

    /// <summary>Plan que sigue al terminar <see cref="PlanEndsAt"/>, si es otro: «Pro hasta el 9/dic, después Básico…».</summary>
    public SubscriptionPlan? NextPlan { get; init; }

    /// <summary>Fin (excluido) del tramo de <see cref="NextPlan"/>; empieza en <see cref="PlanEndsAt"/>.</summary>
    public DateTimeOffset? NextPlanEndsAt { get; init; }

    /// <summary>Último día (en México) del plan actual.</summary>
    public DateOnly? LastPlanDay => PlanEndsAt is { } until ? LastDayBefore(until) : null;

    /// <summary>Día (en México) en que empieza <see cref="NextPlan"/>.</summary>
    public DateOnly? NextPlanStartDay => NextPlan is not null && PlanEndsAt is { } start ? MexicoTime.DateOf(start) : null;

    /// <summary>Último día (en México) de <see cref="NextPlan"/>.</summary>
    public DateOnly? NextPlanLastDay => NextPlan is not null && NextPlanEndsAt is { } until ? LastDayBefore(until) : null;

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
        var trialEndsAt = covering.Where(p => p.Kind == SubscriptionPeriodKind.Trial)
            .Select(p => (DateTimeOffset?)p.EndsAt).Max();
        PlanTier tier;
        SubscriptionPlan? plan;
        if (covering.Count > 0)
        {
            // Con un pago de Pro ya abonado (aunque empiece al terminar la prueba) el vendedor ya es Pro. Lo pagado de
            // Básico no: la prueba le da más que eso.
            var hasProPaidAhead = list.Any(p =>
                p.Kind == SubscriptionPeriodKind.Paid && p.Plan == SubscriptionPlan.Pro && p.EndsAt > now);
            if (covering.All(p => p.Kind == SubscriptionPeriodKind.Trial) && !hasProPaidAhead)
            {
                tier = PlanTier.Trial;
                plan = SubscriptionPlan.Pro;
            }
            else if (covering.Any(p => p.Plan == SubscriptionPlan.Pro))
            {
                tier = PlanTier.Pro;
                plan = SubscriptionPlan.Pro;
            }
            else
            {
                tier = PlanTier.Basic;
                plan = SubscriptionPlan.Basic;
            }
        }
        else if (graceEndsAt is { } graceEnd && now >= last.EndsAt && now < graceEnd)
        {
            tier = PlanTier.Grace;
            plan = last.Plan;
        }
        else
        {
            tier = PlanTier.Expired;
            plan = null;
        }

        var status = new PlanStatus(tier, last.EndsAt, graceEndsAt) { Plan = plan, TrialEndsAt = trialEndsAt };
        return tier is PlanTier.Trial or PlanTier.Basic or PlanTier.Pro ? WithRuns(status, list, now, plan!.Value) : status;
    }

    /// <summary>
    /// Dónde empieza un periodo nuevo: donde termina el anterior si sigue vigente o está en gracia (pagar en la gracia
    /// no regala días), o en ese momento si ya venció.
    /// </summary>
    public static DateTimeOffset NextPeriodStart(IEnumerable<SubscriptionPeriod> periods, DateTimeOffset now, int graceDays)
    {
        var status = For(periods, now, graceDays);
        return status.IsCurrent ? status.ProUntil!.Value : now;
    }

    /// <summary>
    /// Días que le quedan vigentes, por calendario de México: 0 si el último día es hoy. <c>null</c> en el onboarding.
    /// </summary>
    public int? DaysLeft(DateTimeOffset now) =>
        LastActiveDay is { } last ? Math.Max(0, last.DayNumber - MexicoTime.Today(now).DayNumber) : null;

    /// <summary>
    /// El tramo seguido del plan actual y el del plan que sigue: junta los periodos encadenados del mismo plan, desde
    /// el que cubre el instante.
    /// </summary>
    private static PlanStatus WithRuns(
        PlanStatus status, IReadOnlyCollection<SubscriptionPeriod> periods, DateTimeOffset now, SubscriptionPlan plan)
    {
        DateTimeOffset? planEnds = null;
        SubscriptionPlan? next = null;
        DateTimeOffset? nextEnds = null;
        foreach (var period in periods.Where(p => p.EndsAt > now).OrderBy(p => p.StartsAt).ThenBy(p => p.EndsAt))
        {
            if (next is null)
            {
                if (planEnds is { } end && period.StartsAt > end)
                {
                    break;
                }

                if (period.Plan == plan)
                {
                    planEnds = planEnds is { } current && current > period.EndsAt ? current : period.EndsAt;
                    continue;
                }

                if (planEnds is null)
                {
                    continue;
                }

                next = period.Plan;
                nextEnds = period.EndsAt;
            }
            else if (period.Plan == next && period.StartsAt <= nextEnds)
            {
                nextEnds = nextEnds > period.EndsAt ? nextEnds : period.EndsAt;
            }
            else
            {
                break;
            }
        }

        return status with { PlanEndsAt = planEnds, NextPlan = next, NextPlanEndsAt = nextEnds };
    }

    private static DateOnly LastDayBefore(DateTimeOffset end) => MexicoTime.DateOf(end.AddTicks(-1));
}
