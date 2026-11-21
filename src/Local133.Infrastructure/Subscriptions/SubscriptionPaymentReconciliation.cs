using System.Collections.Concurrent;
using Local133.Application.Payments;
using Local133.Application.Subscriptions;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>
/// Consulta a Pey el link de un cargo (<c>GET /payment-link/{id}</c>) y pasa lo que responda por el mismo
/// <see cref="ISubscriptionPaymentRecorder"/> del webhook. Si Pey respondió (también con <c>NOT_FOUND</c>) sella
/// <see cref="SubscriptionCharge.PaymentCheckedAt"/>; si falla, no lo sella y la siguiente corrida lo reintenta.
/// </summary>
internal sealed class SubscriptionPaymentReconciliation(
    AppDbContext db,
    IPaymentGateway gateway,
    ISubscriptionPaymentRecorder recorder,
    TimeProvider clock,
    ILogger<SubscriptionPaymentReconciliation> logger) : ISubscriptionPaymentReconciliation
{
    /// <summary>Último intento fallido por cargo (en memoria del proceso; solo alimenta el freno de <c>minInterval</c>).</summary>
    private static readonly ConcurrentDictionary<int, DateTimeOffset> FailedAttempts = new();

    public async Task<SubscriptionReconciliationResult> ReconcileChargeAsync(
        int chargeId, TimeSpan? minInterval = null, CancellationToken cancellationToken = default)
    {
        var charge = await db.SubscriptionCharges.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == chargeId)
            .Select(c => new { c.Id, c.MerchantId, c.Status, c.PeyPaymentLinkId, c.PaymentCheckedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (charge is null)
        {
            return new SubscriptionReconciliationResult.Skipped($"No existe el cargo {chargeId}.");
        }

        if (charge.Status == SubscriptionChargeStatus.Paid)
        {
            return new SubscriptionReconciliationResult.Skipped($"El cargo {chargeId} ya está pagado.");
        }

        if (string.IsNullOrWhiteSpace(charge.PeyPaymentLinkId))
        {
            return new SubscriptionReconciliationResult.Skipped($"El cargo {chargeId} no tiene link.");
        }

        var now = clock.GetUtcNow();
        if (minInterval is { } interval)
        {
            // El freno cuenta la última consulta sellada y también el último intento fallido (que no se sella, para
            // que el job reintente): si Pey está caído, el polling de la confirmación no le pega cada 3 s.
            var lastAttempt = FailedAttempts.TryGetValue(charge.Id, out var failedAt) && failedAt > (charge.PaymentCheckedAt ?? DateTimeOffset.MinValue)
                ? failedAt
                : charge.PaymentCheckedAt;
            if (lastAttempt is { } last && now - last < interval)
            {
                return new SubscriptionReconciliationResult.Skipped($"El cargo {chargeId} se consultó hace poco.");
            }
        }

        SubscriptionPaymentResult? result = null;
        try
        {
            var link = await gateway.GetPaymentLinkAsync(charge.PeyPaymentLinkId, cancellationToken);
            result = await recorder.RecordAsync(ToUpdate(link), cancellationToken);
        }
        catch (PaymentGatewayNotFoundException)
        {
            logger.LogWarning("Conciliación de links: Pey no encontró el link {PaymentLinkId} del cargo {ChargeId} del vendedor {MerchantId}",
                charge.PeyPaymentLinkId, charge.Id, charge.MerchantId);
        }
        catch (Exception ex) when (ex is PaymentGatewayException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Conciliación de links: no se pudo consultar el link del cargo {ChargeId}; se reintenta en la siguiente corrida",
                charge.Id);
            FailedAttempts[charge.Id] = now;
            return new SubscriptionReconciliationResult.GatewayFailed(ex.Message);
        }

        FailedAttempts.TryRemove(charge.Id, out _);
        await db.SubscriptionCharges.IgnoreQueryFilters()
            .Where(c => c.Id == charge.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.PaymentCheckedAt, now), CancellationToken.None);

        return new SubscriptionReconciliationResult.Checked(
            result ?? new SubscriptionPaymentResult.StatusUpdated(charge.Id, charge.Status));
    }

    internal static PaymentLinkUpdate ToUpdate(GatewayPaymentLink link) => new(
        link.Id,
        link.Status,
        link.ClientReferenceId,
        link.Metadata,
        link.Transactions.Select(t => new PaymentLinkUpdateTransaction(
            t.TransactionId,
            t.PaymentMethod,
            t.PaidAt,
            t.Amount,
            t.AmountBreakdown?.Base,
            t.AmountBreakdown?.Fee,
            t.ApplicationFee)).ToList());
}
