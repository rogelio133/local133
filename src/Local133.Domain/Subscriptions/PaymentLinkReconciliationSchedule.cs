using System.Linq.Expressions;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Calendario de la conciliación de links de pago (job <c>conciliar-links</c>, respaldo del webhook): cuándo se le
/// pregunta a Pey por un cargo sin abonar, contado desde que se creó. Los links no expiran, así que un pago puede
/// llegar tarde; pasados <see cref="MaxAge"/> el caso queda para el admin (Fase 6).
/// <list type="bullet">
/// <item>las primeras <see cref="FrequentWindow"/>: cada <see cref="FrequentInterval"/> (la primera vez también tras
/// <see cref="FrequentInterval"/>, para darle tiempo al webhook);</item>
/// <item>hasta <see cref="HourlyWindow"/>: cada hora;</item>
/// <item>hasta <see cref="MaxAge"/>: una vez al día.</item>
/// </list>
/// </summary>
public static class PaymentLinkReconciliationSchedule
{
    public static readonly TimeSpan FrequentInterval = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan FrequentWindow = TimeSpan.FromHours(2);

    public static readonly TimeSpan HourlyInterval = TimeSpan.FromHours(1);

    public static readonly TimeSpan HourlyWindow = TimeSpan.FromDays(3);

    public static readonly TimeSpan DailyInterval = TimeSpan.FromDays(1);

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(60);

    /// <summary>
    /// Cargos que toca consultar en <paramref name="now"/>: pendientes, en proceso o incompletos, con link. Es una
    /// expresión para que EF Core la traduzca; <see cref="IsDue"/> la evalúa en memoria.
    /// </summary>
    public static Expression<Func<SubscriptionCharge, bool>> DueAt(DateTimeOffset now)
    {
        var firstCheckBefore = now - FrequentInterval;
        var frequentSince = now - FrequentWindow;
        var hourlySince = now - HourlyWindow;
        var oldest = now - MaxAge;
        var frequentChecked = now - FrequentInterval;
        var hourlyChecked = now - HourlyInterval;
        var dailyChecked = now - DailyInterval;

        return c =>
            (c.Status == SubscriptionChargeStatus.Pending
                || c.Status == SubscriptionChargeStatus.Processing
                || c.Status == SubscriptionChargeStatus.Partial)
            && c.PeyPaymentLinkId != null
            && c.CreatedAt <= firstCheckBefore
            && c.CreatedAt > oldest
            && ((c.CreatedAt > frequentSince && (c.PaymentCheckedAt == null || c.PaymentCheckedAt <= frequentChecked))
                || (c.CreatedAt <= frequentSince && c.CreatedAt > hourlySince
                    && (c.PaymentCheckedAt == null || c.PaymentCheckedAt <= hourlyChecked))
                || (c.CreatedAt <= hourlySince && (c.PaymentCheckedAt == null || c.PaymentCheckedAt <= dailyChecked)));
    }

    /// <summary>¿Toca consultar este cargo en <paramref name="now"/>? (misma regla que <see cref="DueAt"/>).</summary>
    public static bool IsDue(SubscriptionCharge charge, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(charge);
        return DueAt(now).Compile()(charge);
    }
}
