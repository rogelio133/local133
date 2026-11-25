using System.Collections.Concurrent;
using Local133.Application.Sales;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>
/// Registra cada aviso de pago, para revisar a quién se avisó sin abrir un cliente de SignalR en cada prueba. Cada
/// sitio (el de la fábrica y los que arma <c>WithWebHostBuilder</c>) avisa con su propio <see cref="ForHost"/>, que
/// registra aquí y reenvía al SignalR de <b>ese</b> sitio. Antes había un solo notificador real compartido: el último
/// sitio en resolverlo se quedaba con él, y un sitio derivado ya desechado dejaba sin avisos al de la fábrica.
/// </summary>
public sealed class RecordingSaleNotifier : ISaleNotifier
{
    public ConcurrentQueue<(int MerchantId, int SaleId)> Notifications { get; } = new();

    public void Reset() => Notifications.Clear();

    /// <summary>Solo registra (sin SignalR).</summary>
    public Task PaymentReceivedAsync(int merchantId, int saleId, CancellationToken cancellationToken = default)
    {
        Notifications.Enqueue((merchantId, saleId));
        return Task.CompletedTask;
    }

    /// <summary>Notificador de un sitio: registra y reenvía a su notificador real.</summary>
    public ISaleNotifier ForHost(ISaleNotifier inner) => new HostNotifier(this, inner);

    private sealed class HostNotifier(RecordingSaleNotifier recorder, ISaleNotifier inner) : ISaleNotifier
    {
        public async Task PaymentReceivedAsync(int merchantId, int saleId, CancellationToken cancellationToken = default)
        {
            await recorder.PaymentReceivedAsync(merchantId, saleId, cancellationToken);
            await inner.PaymentReceivedAsync(merchantId, saleId, cancellationToken);
        }
    }
}
