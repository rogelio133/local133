namespace Local133.Application.Subscriptions;

/// <summary>
/// Se llama después de guardar un pago que dejó una venta pagada. Detecta el cruce del uso justo del mes
/// (<c>FairUseMonthlyPaid</c>, en Básico y en Pro). <b>Nunca lanza</b>: el pago ya quedó guardado.
/// </summary>
public interface IChargeLimitAlerts
{
    Task PaidChargeRecordedAsync(int merchantId, int saleId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A quién se avisa cuando <see cref="IChargeLimitAlerts"/> detecta el uso justo. Hoy solo escribe en el log; el
/// tablero del admin lo calcula con su propia consulta (6.4).
/// </summary>
public interface IChargeLimitNotifier
{
    /// <summary>Pasó el uso justo del mes: se alerta al admin, sin bloquear.</summary>
    /// <param name="month">Día 1 del mes (en México).</param>
    /// <param name="paidCount">Cobros concretados del mes, contando el que acaba de pagarse.</param>
    Task FairUseReachedAsync(int merchantId, DateOnly month, int paidCount, CancellationToken cancellationToken = default);
}
