using System.Collections.Concurrent;
using Local133.Application.Sales;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>
/// Envuelve al notificador real (SignalR) y registra cada aviso, para revisar a quién se avisó sin abrir un cliente
/// de SignalR en cada prueba.
/// </summary>
public sealed class RecordingSaleNotifier : ISaleNotifier
{
    public ConcurrentQueue<(int MerchantId, int SaleId)> Notifications { get; } = new();

    /// <summary>Notificador real; lo asigna la fábrica al construir el sitio.</summary>
    public ISaleNotifier? Inner { get; set; }

    public void Reset() => Notifications.Clear();

    public async Task PaymentReceivedAsync(int merchantId, int saleId, CancellationToken cancellationToken = default)
    {
        Notifications.Enqueue((merchantId, saleId));
        if (Inner is not null)
        {
            await Inner.PaymentReceivedAsync(merchantId, saleId, cancellationToken);
        }
    }
}
