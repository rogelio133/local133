using System.Linq.Expressions;

namespace Local133.Domain.Sales;

/// <summary>
/// Calendario de la conciliación de pagos (respaldo del webhook): cuándo se le pregunta a Pey si una venta sin pagar
/// recibió un pago. Mientras el cobro está vigente se consulta cada <see cref="RetryInterval"/> (la primera vez tras
/// <see cref="FirstCheckDelay"/>, para darle tiempo al webhook) y, ya vencido, una sola vez más pasados
/// <see cref="FinalCheckDelay"/> (por si el webhook llegó tarde o se perdió). Después ya no se consulta.
/// </summary>
public static class PaymentReconciliationSchedule
{
    /// <summary>Espera desde que se crea la venta: casi siempre el webhook llega antes.</summary>
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(2);

    /// <summary>Intervalo entre consultas mientras el cobro está vigente.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(3);

    /// <summary>La consulta final se hace este tiempo después del vencimiento.</summary>
    public static readonly TimeSpan FinalCheckDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Ventas vencidas hace más de esto ya no se consultan, aunque les falte la consulta final (job detenido mucho
    /// tiempo): para lo más viejo queda el barrido por cuenta bancaria.
    /// </summary>
    public static readonly TimeSpan MaxLookback = TimeSpan.FromHours(24);

    /// <summary>
    /// Ventas que toca consultar en <paramref name="now"/>. Es una expresión para que EF Core la traduzca a SQL;
    /// <see cref="IsDue"/> la evalúa en memoria con la misma regla.
    /// </summary>
    public static Expression<Func<Sale, bool>> DueAt(DateTimeOffset now)
    {
        var createdBefore = now - FirstCheckDelay;
        var checkedBefore = now - RetryInterval;
        var finalCheckExpiredBefore = now - FinalCheckDelay;
        var expiredAfter = now - MaxLookback;
        var finalDelayMinutes = FinalCheckDelay.TotalMinutes;

        return s =>
            s.Status != SaleStatus.Paid
            && ((s.Method == ChargeMethod.DynamicQr && s.PeyQrId != null)
                || (s.Method == ChargeMethod.PushNotification && s.PeyTransactionId != null))
            && s.ExpiresAt != null
            && s.CreatedAt <= createdBefore
            && s.ExpiresAt > expiredAfter
            && (
                // Vigente: nunca consultada o ya pasó el intervalo.
                (s.ExpiresAt > now && (s.PaymentCheckedAt == null || s.PaymentCheckedAt <= checkedBefore))
                // Vencida: falta la consulta final (ninguna consulta hecha después de vencimiento + espera).
                || (s.ExpiresAt <= finalCheckExpiredBefore
                    && (s.PaymentCheckedAt == null || s.PaymentCheckedAt < s.ExpiresAt.Value.AddMinutes(finalDelayMinutes))));
    }

    /// <summary>¿Toca consultar esta venta en <paramref name="now"/>? (misma regla que <see cref="DueAt"/>).</summary>
    public static bool IsDue(Sale sale, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sale);
        return DueAt(now).Compile()(sale);
    }
}
