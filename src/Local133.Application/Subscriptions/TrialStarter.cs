using Local133.Domain.Subscriptions;

namespace Local133.Application.Subscriptions;

/// <summary>
/// Prueba de Pro al primer cobro pagado (5.13, §4 del diseño): una sola vez por vendedor, desde el <c>PaidAt</c> del
/// pago. La llaman <c>PaymentRecorder</c> (webhook QR, push y conciliación), después de guardar el pago, y el respaldo
/// del job <c>suscripciones-diario</c>. Guarda en su propia transacción.
/// </summary>
public interface ITrialStarter
{
    /// <param name="startsAt">Cuándo empieza: el <c>PaidAt</c> del primer pago (si cae en el futuro, ahora).</param>
    /// <returns>La suscripción nueva, o <c>null</c> si ya tenía una, está dada de baja u otro pago la creó a la vez.</returns>
    Task<Subscription?> StartAsync(int merchantId, DateTimeOffset startsAt, CancellationToken cancellationToken = default);
}
