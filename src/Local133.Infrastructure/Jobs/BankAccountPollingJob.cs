using Local133.Application.BankAccounts;
using Local133.Domain.Pey;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Jobs;

/// <summary>
/// Sondeo de cuentas bancarias en revisión (Hangfire, cada 30 min). Es el respaldo del webhook
/// <c>bank.account.updated</c> y la única forma de detectar un <c>ERROR</c> (Pey no lo avisa).
/// </summary>
public sealed class BankAccountPollingJob(
    AppDbContext db,
    IServiceScopeFactory scopes,
    ILogger<BankAccountPollingJob> logger)
{
    public const string RecurringJobId = "sondeo-cuentas-bancarias";

    /// <summary>Cada 30 minutos.</summary>
    public const string Cron = "*/30 * * * *";

    /// <returns>Cuentas consultadas.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // Las que llevan más tiempo sin consultarse primero (nunca consultadas antes que todas).
        var ids = await db.BankAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.CodiStatus == PeyCodiStatus.Pending || b.CodiStatus == PeyCodiStatus.Processing)
            .OrderBy(b => b.StatusCheckedAt.HasValue)
            .ThenBy(b => b.StatusCheckedAt)
            .ThenBy(b => b.Id)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        var checkedCount = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Un scope (y un DbContext) por cuenta: una falla no contamina a las siguientes.
            await using var scope = scopes.CreateAsyncScope();
            var sync = scope.ServiceProvider.GetRequiredService<IBankAccountStatusSync>();
            try
            {
                var result = await sync.SyncAsync(id, minInterval: null, cancellationToken);
                if (result is BankAccountStatusSyncResult.GatewayFailed failed)
                {
                    logger.LogWarning("Sondeo: no se pudo consultar la cuenta {BankAccountId}: {Message}", id, failed.Message);
                }
                else
                {
                    checkedCount++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sondeo: error al consultar la cuenta {BankAccountId}; se sigue con la siguiente", id);
            }
        }

        logger.LogInformation("Sondeo de cuentas bancarias: {Checked} de {Total} consultadas", checkedCount, ids.Count);
        return checkedCount;
    }
}
