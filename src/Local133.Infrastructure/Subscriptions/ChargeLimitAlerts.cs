using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>
/// Uso justo del mes al registrar un cobro concretado (5.14; Básico y Pro). El pago que acaba de dejar la venta pagada
/// es el último del conteo, así que se compara <c>n − 1</c> contra <c>n</c>. Un pago tardío (con <c>PaidAt</c> de otro
/// mes) no avisa. El conteo es por <c>MerchantId</c> explícito y sin filtro multi-tenant (índice
/// <c>(MerchantId, PaidAt)</c>). Lo detectado va a <see cref="IChargeLimitNotifier"/>.
/// </summary>
internal sealed class ChargeLimitAlerts(
    AppDbContext db,
    IChargeLimitNotifier notifier,
    TimeProvider clock,
    ILogger<ChargeLimitAlerts> logger) : IChargeLimitAlerts
{
    public async Task PaidChargeRecordedAsync(int merchantId, int saleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var sales = db.Sales.IgnoreQueryFilters().AsNoTracking().Where(s => s.MerchantId == merchantId);
            var paidAt = await sales.Where(s => s.Id == saleId).Select(s => s.PaidAt).SingleOrDefaultAsync(cancellationToken);
            var today = MexicoTime.Today(clock.GetUtcNow());
            var (startUtc, endUtc) = MexicoTime.MonthRange(today);
            if (paidAt is not { } at || at < startUtc || at >= endUtc)
            {
                return;
            }

            var current = await sales.CountAsync(s => s.PaidAt >= startUtc && s.PaidAt < endUtc, cancellationToken);
            var settings = await db.SystemSettings.AsNoTracking()
                .SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);
            if (FairUse.Crossed(current - 1, current, settings))
            {
                await notifier.FairUseReachedAsync(merchantId, MexicoTime.MonthStart(today), current, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo revisar el uso justo del mes del vendedor {MerchantId} tras la venta {SaleId}",
                merchantId, saleId);
        }
    }
}

/// <summary>Aviso del uso justo en el log. El tablero del admin (6.4) lo muestra con su propia consulta.</summary>
internal sealed class LoggingChargeLimitNotifier(ILogger<LoggingChargeLimitNotifier> logger) : IChargeLimitNotifier
{
    public Task FairUseReachedAsync(int merchantId, DateOnly month, int paidCount, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Vendedor {MerchantId} pasó el uso justo: {Count} cobros concretados en {Month:yyyy-MM}",
            merchantId, paidCount, month);
        return Task.CompletedTask;
    }
}
