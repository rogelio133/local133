using System.Globalization;
using Local133.Domain.Common;
using Local133.Domain.Notifications;

namespace Local133.Domain.Subscriptions;

/// <summary>Etapa de los recordatorios de la suscripción (§4 del diseño, 5.7; §7 del de planes, 5.16).</summary>
public enum SubscriptionReminderStage
{
    /// <summary>Anual: 15 días antes de vencer.</summary>
    AnnualUpcoming,

    /// <summary>3 días antes de vencer (pago, prueba, días regalados o transición).</summary>
    Upcoming,

    /// <summary>El día que vence un periodo pagado.</summary>
    DueToday,

    /// <summary>Día 3 de la gracia.</summary>
    Grace,

    /// <summary>El plan venció: ya no genera cobros (días 0 a 2 desde el vencimiento).</summary>
    Expired,

    /// <summary>Seguimiento del vencido: el día 7 desde el vencimiento, una sola vez.</summary>
    ExpiredFollowUp,
}

/// <summary>
/// Recordatorio que le toca hoy al vendedor.
/// </summary>
/// <param name="EndingKind">Origen del último periodo: decide el texto (prueba, pago, días regalados o transición).</param>
/// <param name="EndingPlan">Plan del último periodo de la cadena: el plan que vence (o venció).</param>
/// <param name="LastActiveDay">Último día (en México) vigente.</param>
/// <param name="LastGraceDay">Último día de la gracia, solo si el último periodo es pagado.</param>
/// <param name="DaysLeft">Días que faltan para <paramref name="LastActiveDay"/> (o, en la gracia, para
/// <paramref name="LastGraceDay"/>); 0 el último día.</param>
/// <param name="DedupKey">Llave del aviso: una por etapa y por ciclo (<c>ProUntil</c>).</param>
public sealed record SubscriptionReminder(
    SubscriptionReminderStage Stage,
    SubscriptionPeriodKind EndingKind,
    SubscriptionPlan EndingPlan,
    DateOnly LastActiveDay,
    DateOnly? LastGraceDay,
    int DaysLeft,
    string DedupKey)
{
    public NotificationType NotificationType => Stage switch
    {
        SubscriptionReminderStage.AnnualUpcoming => NotificationType.SubscriptionUpcoming,
        SubscriptionReminderStage.Upcoming => EndingKind switch
        {
            SubscriptionPeriodKind.Paid => NotificationType.SubscriptionUpcoming,
            SubscriptionPeriodKind.Trial => NotificationType.TrialEnding,
            _ => NotificationType.PlanDaysEnding,
        },
        SubscriptionReminderStage.DueToday => NotificationType.SubscriptionDueToday,
        SubscriptionReminderStage.Grace => NotificationType.SubscriptionGrace,
        SubscriptionReminderStage.Expired => NotificationType.PlanExpired,
        SubscriptionReminderStage.ExpiredFollowUp => NotificationType.PlanExpiredFollowUp,
        _ => throw new InvalidOperationException($"Etapa desconocida: {Stage}."),
    };
}

/// <summary>
/// Calendario de los recordatorios de la suscripción (§4 del diseño, §7 del de planes). Lo corre el job diario de las
/// 9:00 (México):
/// <list type="table">
/// <item><term>Anual</term><description>15 días antes, 3 días antes, el día del vencimiento, día 3 de la gracia, al
/// vencer y el seguimiento del día 7.</description></item>
/// <item><term>Paquete de 1 mes</term><description>Lo mismo sin el de 15 días.</description></item>
/// <item><term>Prueba, días regalados y transición</term><description>3 días antes (hasta el último día, porque no hay
/// aviso del día del vencimiento), al vencer y el seguimiento. No hay gracia.</description></item>
/// </list>
/// Cada etapa es una <b>ventana</b> de días, no un día exacto: si el job no corre un día, el aviso sale al siguiente con
/// los días reales. Pasada la ventana no se manda nada atrasado. Las ventanas no se enciman: a lo más una etapa por día.
/// El vencimiento es un cálculo (<see cref="PlanStatus"/>): esto solo decide qué avisar.
/// </summary>
public static class SubscriptionReminders
{
    /// <summary>Días antes del vencimiento del anual para el primer aviso.</summary>
    public const int AnnualNoticeDays = 15;

    /// <summary>Días antes del vencimiento del aviso «vence pronto».</summary>
    public const int UpcomingDays = 3;

    /// <summary>Día de la gracia del último recordatorio (contado desde el último día vigente).</summary>
    public const int GraceNoticeDay = 3;

    /// <summary>Días, contando el del vencimiento, en que todavía se avisa que el plan venció.</summary>
    public const int ExpiredNoticeWindowDays = 3;

    /// <summary>Día (desde el vencimiento) del seguimiento del vencido.</summary>
    public const int ExpiredFollowUpDay = 7;

    /// <summary>Días de la ventana del seguimiento: si el job no corre el día 7, sale el 8 o el 9.</summary>
    public const int ExpiredFollowUpWindowDays = 3;

    /// <summary>Último día (desde el vencimiento) en que el job todavía puede tener algo que avisar.</summary>
    public const int LastExpiredNoticeDay = ExpiredFollowUpDay + ExpiredFollowUpWindowDays - 1;

    /// <summary>Prefijo de las llaves de estos avisos.</summary>
    public const string DedupKeyPrefix = "sub:";

    /// <param name="lastKind">Origen del último periodo (<c>Subscription.LastPeriodKind</c>).</param>
    /// <param name="endingPlan">Plan del último periodo de la cadena (<c>Subscription.ChainEndPlan</c>).</param>
    /// <param name="lastPackage">Paquete del último pago (<c>Subscription.LastPackage</c>): solo el anual tiene aviso de 15 días.</param>
    /// <returns>El recordatorio de hoy, o <c>null</c> si hoy no toca ninguno.</returns>
    public static SubscriptionReminder? For(
        PlanStatus status, SubscriptionPeriodKind lastKind, SubscriptionPlan endingPlan, SubscriptionPackage? lastPackage,
        DateTimeOffset now) =>
        Compute(status, lastKind, endingPlan, lastPackage, now, wholeGrace: false);

    /// <summary>
    /// Aviso de «Mi plan» de que toca renovar: la misma etapa que el recordatorio de hoy, salvo las del vencido (el plan
    /// ya lo dice), y en toda la gracia (el correo sale desde el día <see cref="GraceNoticeDay"/>, el aviso desde el 1).
    /// </summary>
    public static SubscriptionReminder? RenewalNoticeFor(
        PlanStatus status, SubscriptionPeriodKind lastKind, SubscriptionPlan endingPlan, SubscriptionPackage? lastPackage,
        DateTimeOffset now) =>
        Compute(status, lastKind, endingPlan, lastPackage, now, wholeGrace: true) is
            { Stage: not (SubscriptionReminderStage.Expired or SubscriptionReminderStage.ExpiredFollowUp) } notice
            ? notice
            : null;

    private static SubscriptionReminder? Compute(
        PlanStatus status, SubscriptionPeriodKind lastKind, SubscriptionPlan endingPlan, SubscriptionPackage? lastPackage,
        DateTimeOffset now, bool wholeGrace)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.Tier == PlanTier.Onboarding || status.ProUntil is not { } proUntil || status.LastActiveDay is not { } lastDay)
        {
            return null;
        }

        var today = MexicoTime.Today(now);
        var daysToEnd = lastDay.DayNumber - today.DayNumber;

        SubscriptionReminder Make(SubscriptionReminderStage stage, int daysLeft) => new(
            stage, lastKind, endingPlan, lastDay, status.LastGraceDay, Math.Max(0, daysLeft), DedupKeyFor(stage, proUntil));

        switch (status.Tier)
        {
            case PlanTier.Expired:
            {
                var expiredDay = MexicoTime.DateOf(status.GraceEndsAt ?? proUntil);
                var sinceExpired = today.DayNumber - expiredDay.DayNumber;
                return sinceExpired switch
                {
                    >= 0 and < ExpiredNoticeWindowDays => Make(SubscriptionReminderStage.Expired, 0),
                    >= ExpiredFollowUpDay and <= LastExpiredNoticeDay => Make(SubscriptionReminderStage.ExpiredFollowUp, 0),
                    _ => null,
                };
            }

            case PlanTier.Grace:
            {
                // Venció hoy antes de las 9:00: ya está en la gracia, pero el aviso es el del día del vencimiento.
                if (daysToEnd == 0)
                {
                    return Make(SubscriptionReminderStage.DueToday, 0);
                }

                var graceLeft = status.LastGraceDay is { } graceDay ? graceDay.DayNumber - today.DayNumber : 0;
                return wholeGrace || -daysToEnd >= GraceNoticeDay
                    ? Make(SubscriptionReminderStage.Grace, graceLeft)
                    : null;
            }

            case PlanTier.Trial or PlanTier.Basic or PlanTier.Pro:
            {
                var paid = lastKind == SubscriptionPeriodKind.Paid;
                if (paid && daysToEnd == 0)
                {
                    return Make(SubscriptionReminderStage.DueToday, 0);
                }

                if (daysToEnd is >= 0 and <= UpcomingDays)
                {
                    return Make(SubscriptionReminderStage.Upcoming, daysToEnd);
                }

                if (paid && lastPackage == SubscriptionPackage.Annual && daysToEnd is > UpcomingDays and <= AnnualNoticeDays)
                {
                    return Make(SubscriptionReminderStage.AnnualUpcoming, daysToEnd);
                }

                return null;
            }

            default:
                return null;
        }
    }

    /// <summary><c>sub:{etapa}:{ProUntil en UTC}</c>: un pago mueve <c>ProUntil</c> y empieza otro ciclo de avisos.</summary>
    public static string DedupKeyFor(SubscriptionReminderStage stage, DateTimeOffset proUntil) =>
        string.Create(CultureInfo.InvariantCulture, $"{DedupKeyPrefix}{stage}:{proUntil.UtcDateTime:yyyyMMddHHmmss}");
}
