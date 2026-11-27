using System.Collections.Concurrent;
using Local133.Application.Subscriptions;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>Registra el uso justo que detecta <see cref="IChargeLimitAlerts"/> (5.14); puede simular una falla.</summary>
public sealed class RecordingChargeLimitNotifier : IChargeLimitNotifier
{
    public ConcurrentQueue<(int MerchantId, int PaidCount)> FairUse { get; } = new();

    /// <summary>Si no es <c>null</c>, cada aviso lanza esta excepción.</summary>
    public Exception? FailWith { get; set; }

    public void Reset()
    {
        FairUse.Clear();
        FailWith = null;
    }

    public Task FairUseReachedAsync(int merchantId, DateOnly month, int paidCount, CancellationToken cancellationToken = default)
    {
        if (FailWith is { } ex)
        {
            throw ex;
        }

        FairUse.Enqueue((merchantId, paidCount));
        return Task.CompletedTask;
    }
}
