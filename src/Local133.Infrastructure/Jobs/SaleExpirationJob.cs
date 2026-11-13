using Local133.Application.Sales;

namespace Local133.Infrastructure.Jobs;

/// <summary>Expira las ventas pendientes con el QR vencido (Hangfire, cada minuto): no hay webhook de expiración.</summary>
public sealed class SaleExpirationJob(ISaleExpiration expiration)
{
    public const string RecurringJobId = "expirar-ventas";

    /// <summary>Cada minuto.</summary>
    public const string Cron = "* * * * *";

    public Task<int> RunAsync(CancellationToken cancellationToken) => expiration.ExpireDueAsync(cancellationToken);
}
