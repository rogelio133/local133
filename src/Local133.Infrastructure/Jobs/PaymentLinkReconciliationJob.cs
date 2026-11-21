using Local133.Application.Subscriptions;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Jobs;

/// <summary>
/// Conciliación de links de pago de la suscripción (Hangfire, cada 10 min): respaldo del webhook
/// <c>payment_link.status_updated</c> para los cargos sin abonar, según <see cref="PaymentLinkReconciliationSchedule"/>.
/// </summary>
public sealed class PaymentLinkReconciliationJob(
    AppDbContext db,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<PaymentLinkReconciliationJob> logger)
{
    public const string RecurringJobId = "conciliar-links";

    /// <summary>Cada 10 minutos (el calendario de cada cargo decide si se consulta).</summary>
    public const string Cron = "*/10 * * * *";

    /// <summary>Tope de cargos por corrida: los demás quedan para la siguiente.</summary>
    public const int MaxChargesPerRun = 50;

    /// <returns>Cargos consultados en Pey.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var ids = await db.SubscriptionCharges.IgnoreQueryFilters().AsNoTracking()
            .Where(PaymentLinkReconciliationSchedule.DueAt(now))
            .OrderBy(c => c.PaymentCheckedAt ?? c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => c.Id)
            .Take(MaxChargesPerRun)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        int checkedCount = 0, credited = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Un scope (y un DbContext) por cargo: una falla no contamina a los siguientes.
            await using var scope = scopes.CreateAsyncScope();
            var reconciliation = scope.ServiceProvider.GetRequiredService<ISubscriptionPaymentReconciliation>();
            try
            {
                if (await reconciliation.ReconcileChargeAsync(id, cancellationToken: cancellationToken)
                    is SubscriptionReconciliationResult.Checked done)
                {
                    checkedCount++;
                    if (done.Result is SubscriptionPaymentResult.Credited)
                    {
                        credited++;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Conciliación de links: error con el cargo {ChargeId}; se sigue con el siguiente", id);
            }
        }

        logger.LogInformation("Conciliación de links: {Checked} de {Total} cargos consultados, {Credited} abonados",
            checkedCount, ids.Count, credited);
        return checkedCount;
    }
}
