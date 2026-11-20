using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>
/// Umbrales del mes al registrar un cobro concretado. El pago que acaba de dejar la venta pagada es el último del
/// conteo, así que se compara <c>n − 1</c> contra <c>n</c>. Un pago tardío (con <c>PaidAt</c> de otro mes) no avisa.
/// Lo detectado va a <see cref="IChargeLimitNotifier"/>.
/// </summary>
internal sealed class ChargeLimitAlerts(
    AppDbContext db,
    IPlanAccess plans,
    IChargeUsage usage,
    IChargeLimitNotifier notifier,
    TimeProvider clock,
    ILogger<ChargeLimitAlerts> logger) : IChargeLimitAlerts
{
    public async Task PaidChargeRecordedAsync(int merchantId, int saleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var paidAt = await db.Sales.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.Id == saleId && s.MerchantId == merchantId)
                .Select(s => s.PaidAt)
                .SingleOrDefaultAsync(cancellationToken);
            var (startUtc, endUtc) = MexicoTime.MonthRange(MexicoTime.Today(clock.GetUtcNow()));
            if (paidAt is not { } at || at < startUtc || at >= endUtc)
            {
                return;
            }

            var plan = await plans.GetForMerchantAsync(merchantId, cancellationToken);
            var month = await usage.GetMonthAsync(merchantId, cancellationToken);
            var settings = await db.SystemSettings.AsNoTracking()
                .SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);
            var current = month.Paid;

            if (plan.Entitlements.HasMonthlyChargeLimit)
            {
                foreach (var threshold in MonthlyChargeAllowance.ThresholdsCrossed(current - 1, current, settings))
                {
                    await notifier.FreeLimitThresholdReachedAsync(merchantId, threshold, month, cancellationToken);
                }
            }
            else if (MonthlyChargeAllowance.ProFairUseCrossed(current - 1, current, settings))
            {
                await notifier.ProFairUseReachedAsync(merchantId, month, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudieron revisar los límites del mes del vendedor {MerchantId} tras la venta {SaleId}",
                merchantId, saleId);
        }
    }
}

/// <summary>Aviso de los umbrales en el log (5.5). La 5.4 lo reemplaza por avisos en el sitio y correos.</summary>
internal sealed class LoggingChargeLimitNotifier(ILogger<LoggingChargeLimitNotifier> logger) : IChargeLimitNotifier
{
    public Task FreeLimitThresholdReachedAsync(
        int merchantId, int threshold, MonthlyChargeUsage month, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Vendedor {MerchantId} (plan gratuito) llegó a {Threshold} cobros concretados en {Month:yyyy-MM} (límite {Limit})",
            merchantId, threshold, month.Month, month.Allowance.PaidLimit);
        return Task.CompletedTask;
    }

    public Task ProFairUseReachedAsync(int merchantId, MonthlyChargeUsage month, CancellationToken cancellationToken = default)
    {
        // Fase 6: alerta en el admin.
        logger.LogWarning("Vendedor {MerchantId} pasó el uso justo de Pro: {Count} cobros concretados en {Month:yyyy-MM}",
            merchantId, month.Paid, month.Month);
        return Task.CompletedTask;
    }
}
