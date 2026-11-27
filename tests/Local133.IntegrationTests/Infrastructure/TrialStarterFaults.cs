using Local133.Application.Subscriptions;
using Local133.Domain.Subscriptions;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>
/// Falla simulada del disparador de la prueba al primer cobro (5.13). Con <see cref="FailWith"/> en <c>null</c> (lo
/// normal), <see cref="FaultyTrialStarter"/> delega en el <see cref="ITrialStarter"/> real.
/// </summary>
public sealed class TrialStarterFaults
{
    /// <summary>Si no es <c>null</c>, cada llamada lanza esta excepción.</summary>
    public Exception? FailWith { get; set; }

    public void Reset() => FailWith = null;
}

/// <summary>Envuelve el <see cref="ITrialStarter"/> real para poder simular una falla.</summary>
public sealed class FaultyTrialStarter(TrialStarterFaults faults, ITrialStarter inner) : ITrialStarter
{
    public Task<Subscription?> StartAsync(int merchantId, DateTimeOffset startsAt, CancellationToken cancellationToken = default) =>
        faults.FailWith is { } ex ? throw ex : inner.StartAsync(merchantId, startsAt, cancellationToken);
}
