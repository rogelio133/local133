using System.Collections.Concurrent;
using Local133.Application.Subscriptions;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>Registra los umbrales del mes que detecta <see cref="IChargeLimitAlerts"/>; puede simular una falla.</summary>
public sealed class RecordingChargeLimitNotifier : IChargeLimitNotifier
{
    public ConcurrentQueue<(int MerchantId, int Threshold)> FreeThresholds { get; } = new();

    public ConcurrentQueue<int> ProFairUse { get; } = new();

    /// <summary>Si no es <c>null</c>, cada aviso lanza esta excepción.</summary>
    public Exception? FailWith { get; set; }

    public void Reset()
    {
        FreeThresholds.Clear();
        ProFairUse.Clear();
        FailWith = null;
    }

    public Task FreeLimitThresholdReachedAsync(
        int merchantId, int threshold, MonthlyChargeUsage month, CancellationToken cancellationToken = default)
    {
        if (FailWith is { } ex)
        {
            throw ex;
        }

        FreeThresholds.Enqueue((merchantId, threshold));
        return Task.CompletedTask;
    }

    public Task ProFairUseReachedAsync(int merchantId, MonthlyChargeUsage month, CancellationToken cancellationToken = default)
    {
        if (FailWith is { } ex)
        {
            throw ex;
        }

        ProFairUse.Enqueue(merchantId);
        return Task.CompletedTask;
    }
}
