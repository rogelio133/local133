using Local133.Domain.Subscriptions;

namespace Local133.Application.Subscriptions;

/// <summary>
/// Cobros del vendedor en el mes calendario actual (hora de México) y lo que le permite el límite del gratuito.
/// </summary>
/// <param name="Month">Día 1 del mes que se cuenta.</param>
/// <param name="ResetsOn">Día 1 del mes siguiente, cuando se reinicia el conteo.</param>
/// <param name="Allowance">
/// Límite del gratuito con estos conteos. Se calcula siempre; solo aplica si el plan tiene
/// <see cref="PlanEntitlements.HasMonthlyChargeLimit"/>.
/// </param>
public sealed record MonthlyChargeUsage(DateOnly Month, DateOnly ResetsOn, MonthlyChargeAllowance Allowance)
{
    /// <summary>Cobros concretados: ventas con <c>PaidAt</c> dentro del mes.</summary>
    public int Paid => Allowance.Paid;

    /// <summary>Cobros generados: ventas creadas dentro del mes, se paguen o no.</summary>
    public int Generated => Allowance.Generated;
}

/// <summary>Conteo de cobros del mes de un vendedor, sin depender de la sesión (cobro, webhook, vistas).</summary>
public interface IChargeUsage
{
    Task<MonthlyChargeUsage> GetMonthAsync(int merchantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Se llama después de guardar un pago que dejó una venta pagada. Detecta los umbrales del gratuito (80 % y límite)
/// y el uso justo de Pro. En la 5.5 solo los registra en el log; la 5.4 los convierte en avisos en
/// el sitio y correos. <b>Nunca lanza</b>: el pago ya quedó guardado.
/// </summary>
public interface IChargeLimitAlerts
{
    Task PaidChargeRecordedAsync(int merchantId, int saleId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A quién se avisa cuando <see cref="IChargeLimitAlerts"/> detecta un umbral. En la 5.5 solo escribe en el log; la
/// 5.4 lo reemplaza por avisos en el sitio y correos (idempotentes por <c>DedupKey</c>, p. ej.
/// <c>charge-limit-{umbral}-{AAAA-MM}</c>).
/// </summary>
public interface IChargeLimitNotifier
{
    /// <summary>Gratuito: llegó a <paramref name="threshold"/> cobros concretados (80 % o límite).</summary>
    Task FreeLimitThresholdReachedAsync(int merchantId, int threshold, MonthlyChargeUsage month, CancellationToken cancellationToken = default);

    /// <summary>Pro: pasó el uso justo del mes. Se alerta al admin, sin bloquear.</summary>
    Task ProFairUseReachedAsync(int merchantId, MonthlyChargeUsage month, CancellationToken cancellationToken = default);
}
