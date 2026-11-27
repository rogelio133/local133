using Local133.Domain.Common;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Anuncio del cambio de planes 2026-12 (§10 del diseño de planes, 5.16): un aviso y un correo, una sola vez, a los
/// vendedores registrados antes del corte. El corte es la hora en que se aplicó la migración
/// <c>PlanChangeAnnouncement</c> (renglón <c>AuditAction.PlanChange</c> de <c>AuditLogs</c>); el job diario lo manda
/// durante <see cref="SendDays"/> días a partir de ahí.
/// </summary>
public static class PlanChangeAnnouncement
{
    /// <summary>Llave del aviso: única por vendedor.</summary>
    public const string DedupKey = "planes-2026-12";

    /// <summary>Días después del corte en que el job todavía manda el anuncio.</summary>
    public const int SendDays = 30;

    /// <summary>Todavía toca mandarlo: desde el corte y durante <see cref="SendDays"/> días.</summary>
    public static bool IsDue(DateTimeOffset cutoff, DateTimeOffset now) => now >= cutoff && now < cutoff.AddDays(SendDays);

    /// <summary>Le toca al vendedor: se registró antes del corte (los nuevos ya conocen los planes nuevos).</summary>
    public static bool Applies(DateTimeOffset merchantCreatedAt, DateTimeOffset cutoff) => merchantCreatedAt < cutoff;
}

/// <summary>Situación del vendedor que explica el anuncio.</summary>
public enum PlanChangeSituationKind
{
    /// <summary>Sin prueba todavía: empieza con su primer cobro.</summary>
    Onboarding,

    /// <summary>Recibió días de Básico de transición (venía del gratuito o de la gracia).</summary>
    Transition,

    /// <summary>Está en su prueba y al terminarla recibe los días de Básico de transición.</summary>
    TrialThenTransition,

    /// <summary>Tiene un plan vigente (pagado, premio o regalo) que sigue igual.</summary>
    Current,

    /// <summary>Su plan ya venció.</summary>
    Expired,
}

/// <param name="Kind">Qué le pasó al vendedor con el cambio.</param>
/// <param name="Plan">Plan vigente (<see cref="PlanChangeSituationKind.Current"/>).</param>
/// <param name="LastDay">Último día (en México) de la transición o del plan vigente.</param>
public sealed record PlanChangeSituation(PlanChangeSituationKind Kind, SubscriptionPlan? Plan = null, DateOnly? LastDay = null)
{
    /// <summary>
    /// La situación con la suscripción (con sus periodos), o el onboarding si no tiene. La transición la da un periodo
    /// <see cref="SubscriptionPeriodKind.Transition"/> que todavía no termina.
    /// </summary>
    public static PlanChangeSituation For(Subscription? subscription, DateTimeOffset now, int graceDays)
    {
        if (subscription is null)
        {
            return new(PlanChangeSituationKind.Onboarding);
        }

        var status = subscription.StatusAt(now, graceDays);
        var transition = subscription.Periods
            .Where(p => p.Kind == SubscriptionPeriodKind.Transition && p.EndsAt > now)
            .MaxBy(p => p.EndsAt);
        if (transition is not null)
        {
            var lastDay = MexicoTime.DateOf(transition.EndsAt.AddTicks(-1));
            return status.Tier == PlanTier.Trial
                ? new(PlanChangeSituationKind.TrialThenTransition, SubscriptionPlan.Basic, lastDay)
                : new(PlanChangeSituationKind.Transition, SubscriptionPlan.Basic, lastDay);
        }

        return status.Tier switch
        {
            PlanTier.Onboarding => new(PlanChangeSituationKind.Onboarding),
            PlanTier.Expired => new(PlanChangeSituationKind.Expired),
            _ => new(PlanChangeSituationKind.Current, status.Plan, status.LastActiveDay),
        };
    }
}
