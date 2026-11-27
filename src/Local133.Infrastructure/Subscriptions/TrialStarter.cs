using Local133.Application.Subscriptions;
using Local133.Domain.Merchants;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>
/// Prueba de Pro al primer cobro pagado (5.13). Una sola vez por vendedor: la búsqueda previa, el candado
/// <c>UPDLOCK, HOLDLOCK</c> sobre su fila de <c>Subscriptions</c> (el mismo de <see cref="SubscriptionCrediting.LockAsync"/>
/// y de <see cref="ReferralRewarder.QualifyAsync"/>, así un premio que califica a la vez no queda pendiente) y, como
/// última defensa, la llave alterna única <c>Subscriptions.MerchantId</c>. Los premios de referidos que esperaban su
/// suscripción se encadenan después de la prueba.
/// </summary>
internal sealed class TrialStarter(AppDbContext db, TimeProvider clock, ILogger<TrialStarter> logger) : ITrialStarter
{
    public async Task<Subscription?> StartAsync(int merchantId, DateTimeOffset startsAt, CancellationToken cancellationToken = default)
    {
        // Camino rápido sin transacción: casi todos los pagos son de vendedores que ya tienen suscripción.
        if (await ExistsAsync(merchantId, cancellationToken))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        try
        {
            Subscription subscription;
            int granted;
            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM [Subscriptions] WITH (UPDLOCK, HOLDLOCK) WHERE [MerchantId] = {merchantId}",
                    cancellationToken);
                if (await ExistsAsync(merchantId, cancellationToken))
                {
                    return null; // otro pago la creó mientras esperábamos el candado
                }

                var status = await db.Merchants.IgnoreQueryFilters().AsNoTracking()
                    .Where(m => m.Id == merchantId)
                    .Select(m => (MerchantStatus?)m.Status)
                    .SingleOrDefaultAsync(cancellationToken);
                if (status is null or MerchantStatus.Disabled)
                {
                    return null;
                }

                var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);
                subscription = Subscription.StartTrial(merchantId, startsAt < now ? startsAt : now, settings.TrialDays);
                db.Subscriptions.Add(subscription);

                // Premios de referidos que pagaron antes de su primer cobro (5.8): se encadenan después de la prueba. Ya
                // se le avisó al calificar cada uno.
                granted = await ReferralRewarder.GrantPendingAsync(db, subscription, status.Value, now, settings, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            logger.LogInformation("Vendedor {MerchantId}: empezó su prueba de Pro con su primer cobro ({StartsAt:o}), vigente hasta {ProUntil:o}; {Granted} premios pendientes",
                merchantId, subscription.TrialStartedAt, subscription.ProUntil, granted);
            return subscription;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Otro pago ganó la carrera: ya tiene su prueba.
            db.ChangeTracker.Clear();
            return null;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private Task<bool> ExistsAsync(int merchantId, CancellationToken cancellationToken) =>
        db.Subscriptions.IgnoreQueryFilters().AnyAsync(s => s.MerchantId == merchantId, cancellationToken);
}
