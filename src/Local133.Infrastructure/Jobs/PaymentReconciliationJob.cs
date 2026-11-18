using Local133.Application.Payments;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Jobs;

/// <summary>
/// Conciliación de pagos (Hangfire, cada minuto): respaldo del webhook para las ventas sin pagar, según el calendario
/// de <see cref="PaymentReconciliationSchedule"/>. Sin ventas que consultar solo hace una consulta a la base.
/// </summary>
public sealed class PaymentReconciliationJob(
    AppDbContext db,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<PaymentReconciliationJob> logger)
{
    public const string RecurringJobId = "conciliar-pagos";

    /// <summary>Cada minuto (el calendario de cada venta decide si se consulta).</summary>
    public const string Cron = "* * * * *";

    /// <summary>Tope de ventas por corrida: las demás quedan para la siguiente.</summary>
    public const int MaxSalesPerRun = 50;

    /// <returns>Ventas consultadas en Pey.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var ids = await db.Sales.IgnoreQueryFilters().AsNoTracking()
            .Where(PaymentReconciliationSchedule.DueAt(now))
            .OrderBy(s => s.ExpiresAt)
            .ThenBy(s => s.Id)
            .Select(s => s.Id)
            .Take(MaxSalesPerRun)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        int checkedCount = 0, recorded = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Un scope (y un DbContext) por venta: una falla no contamina a las siguientes.
            await using var scope = scopes.CreateAsyncScope();
            var reconciliation = scope.ServiceProvider.GetRequiredService<IPaymentReconciliation>();
            try
            {
                var result = await reconciliation.ReconcileSaleAsync(id, cancellationToken);
                if (result is PaymentReconciliationResult.Checked done)
                {
                    checkedCount++;
                    recorded += done.Recorded;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Conciliación: error con la venta {SaleId}; se sigue con la siguiente", id);
            }
        }

        logger.LogInformation("Conciliación de pagos: {Checked} de {Total} ventas consultadas, {Recorded} pagos recuperados",
            checkedCount, ids.Count, recorded);
        return checkedCount;
    }
}
