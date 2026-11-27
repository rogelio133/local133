using System.Globalization;
using Local133.Domain.Common;
using Local133.Domain.Notifications;

namespace Local133.Domain.Subscriptions;

/// <summary>Etapa de los recordatorios de la suscripción (§4 del diseño, 5.7).</summary>
public enum SubscriptionReminderStage
{
    /// <summary>Anual: 15 días antes de vencer.</summary>
    AnnualUpcoming,

    /// <summary>3 días antes de vencer (pago, prueba o días regalados).</summary>
    Upcoming,

    /// <summary>El día que vence un periodo pagado.</summary>
    DueToday,

    /// <summary>Día 3 de la gracia.</summary>
    Grace,

    /// <summary>Bajó al plan gratuito.</summary>
    Downgraded,
}

/// <summary>
/// Recordatorio que le toca hoy al vendedor.
/// </summary>
/// <param name="EndingKind">Origen del último periodo: decide el texto (prueba, pago o días regalados).</param>
/// <param name="LastProDay">Último día (en México) con Pro.</param>
/// <param name="LastGraceDay">Último día de la gracia, solo si el último periodo es pagado.</param>
/// <param name="DaysLeft">Días que faltan para <paramref name="LastProDay"/> (o, en la gracia, para
/// <paramref name="LastGraceDay"/>); 0 el último día.</param>
/// <param name="DedupKey">Llave del aviso: una por etapa y por ciclo (<c>ProUntil</c>).</param>
public sealed record SubscriptionReminder(
    SubscriptionReminderStage Stage,
    SubscriptionPeriodKind EndingKind,
    DateOnly LastProDay,
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
            _ => NotificationType.ProDaysEnding,
        },
        SubscriptionReminderStage.DueToday => NotificationType.SubscriptionDueToday,
        SubscriptionReminderStage.Grace => NotificationType.SubscriptionGrace,
        SubscriptionReminderStage.Downgraded => NotificationType.DowngradedToFree,
        _ => throw new InvalidOperationException($"Etapa desconocida: {Stage}."),
    };
}

/// <summary>
/// Calendario de los recordatorios de la suscripción (§4 del diseño). Lo corre el job diario de las 9:00 (México):
/// <list type="table">
/// <item><term>Anual</term><description>15 días antes, 3 días antes, el día del vencimiento, día 3 de la gracia y al
/// bajar al gratuito.</description></item>
/// <item><term>Paquetes de 1, 3 y 6 meses</term><description>Lo mismo sin el de 15 días.</description></item>
/// <item><term>Prueba y días regalados</term><description>3 días antes (hasta el último día, porque no hay aviso del
/// día del vencimiento) y al bajar al gratuito. No hay gracia.</description></item>
/// </list>
/// Cada etapa es una <b>ventana</b> de días, no un día exacto: si el job no corre un día, el aviso sale al siguiente con
/// los días reales. Pasada la ventana no se manda nada atrasado. Las ventanas no se enciman: a lo más una etapa por día.
/// La baja al gratuito es un cálculo (<see cref="PlanStatus"/>): esto solo decide qué avisar.
/// </summary>
public static class SubscriptionReminders
{
    /// <summary>Días antes del vencimiento del anual para el primer aviso.</summary>
    public const int AnnualNoticeDays = 15;

    /// <summary>Días antes del vencimiento del aviso «vence pronto».</summary>
    public const int UpcomingDays = 3;

    /// <summary>Día de la gracia del último recordatorio (contado desde el último día con Pro).</summary>
    public const int GraceNoticeDay = 3;

    /// <summary>Días, contando el de la baja, en que todavía se avisa de la baja al gratuito.</summary>
    public const int DowngradeNoticeWindowDays = 3;

    /// <summary>Prefijo de las llaves de estos avisos.</summary>
    public const string DedupKeyPrefix = "sub:";

    /// <param name="lastKind">Origen del último periodo (<c>Subscription.LastPeriodKind</c>).</param>
    /// <param name="lastPackage">Paquete del último pago (<c>Subscription.LastPackage</c>): solo el anual tiene aviso de 15 días.</param>
    /// <returns>El recordatorio de hoy, o <c>null</c> si hoy no toca ninguno.</returns>
    public static SubscriptionReminder? For(
        PlanStatus status, SubscriptionPeriodKind lastKind, SubscriptionPackage? lastPackage, DateTimeOffset now) =>
        Compute(status, lastKind, lastPackage, now, wholeGrace: false);

    /// <summary>
    /// Aviso de «Mi plan» de que toca renovar: la misma etapa que el recordatorio de hoy, salvo la baja (el plan ya
    /// lo dice), y en toda la gracia (el correo sale desde el día <see cref="GraceNoticeDay"/>, el aviso desde el 1).
    /// </summary>
    public static SubscriptionReminder? RenewalNoticeFor(
        PlanStatus status, SubscriptionPeriodKind lastKind, SubscriptionPackage? lastPackage, DateTimeOffset now) =>
        Compute(status, lastKind, lastPackage, now, wholeGrace: true) is { Stage: not SubscriptionReminderStage.Downgraded } notice
            ? notice
            : null;

    private static SubscriptionReminder? Compute(
        PlanStatus status, SubscriptionPeriodKind lastKind, SubscriptionPackage? lastPackage, DateTimeOffset now, bool wholeGrace)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.Tier == PlanTier.Onboarding || status.ProUntil is not { } proUntil || status.LastActiveDay is not { } lastDay)
        {
            return null;
        }

        var today = MexicoTime.Today(now);
        var daysToEnd = lastDay.DayNumber - today.DayNumber;

        SubscriptionReminder Make(SubscriptionReminderStage stage, int daysLeft) => new(
            stage, lastKind, lastDay, status.LastGraceDay, Math.Max(0, daysLeft), DedupKeyFor(stage, proUntil));

        switch (status.Tier)
        {
            case PlanTier.Expired:
            {
                var dropDay = MexicoTime.DateOf(status.GraceEndsAt ?? proUntil);
                var sinceDrop = today.DayNumber - dropDay.DayNumber;
                return sinceDrop is >= 0 and < DowngradeNoticeWindowDays
                    ? Make(SubscriptionReminderStage.Downgraded, 0)
                    : null;
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
