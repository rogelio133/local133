using System.Collections.Concurrent;
using Local133.Application.Subscriptions;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>Registra los meses con los que el job diario pidió el resumen mensual (5.7; su contenido es de la 5.10).</summary>
public sealed class RecordingMonthlySummaryNotifier : IMonthlySummaryNotifier
{
    public ConcurrentQueue<DateOnly> Months { get; } = new();

    public void Reset() => Months.Clear();

    public Task SendAsync(DateOnly month, CancellationToken cancellationToken = default)
    {
        Months.Enqueue(month);
        return Task.CompletedTask;
    }
}
