using Local133.Application.Notifications;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Notifications;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Email;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>
/// Resumen mensual (5.10, §7 del diseño): el día 1 el job <c>suscripciones-diario</c> lo pide para el mes que terminó.
/// Va a cada vendedor que no está dado de baja y tuvo al menos un cobro concretado ese mes (pagos con <c>PaidAt</c> en
/// el mes, en hora de México): aviso en el sitio y correo con lo cobrado, la comisión estimada de una terminal y el
/// precio de Pro. Idempotente por mes (<c>DedupKey</c> <c>summary:{yyyy-MM}</c>): repetir la corrida no duplica nada.
/// Un scope por vendedor: la falla de uno no detiene a los demás.
/// </summary>
public sealed class MonthlySummaryNotifier(
    AppDbContext db,
    IServiceScopeFactory scopes,
    ILogger<MonthlySummaryNotifier> logger) : IMonthlySummaryNotifier
{
    /// <summary>Llave del aviso del mes.</summary>
    public static string DedupKeyFor(DateOnly month) => $"summary:{month:yyyy-MM}";

    public async Task SendAsync(DateOnly month, CancellationToken cancellationToken = default)
    {
        var monthStart = MexicoTime.MonthStart(month);
        var (startUtc, endUtc) = MexicoTime.MonthRange(monthStart);
        var settings = await db.SystemSettings.AsNoTracking()
            .SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);

        // Sin filtro multi-tenant (job): todos los vendedores con pagos en el mes, salvo los dados de baja.
        var rows = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.PaidAt >= startUtc && p.PaidAt < endUtc
                && db.Merchants.Any(m => m.Id == p.MerchantId && m.Status != MerchantStatus.Disabled))
            .GroupBy(p => p.MerchantId)
            .Select(g => new { MerchantId = g.Key, Collected = g.Sum(p => p.Amount), SalesCount = g.Select(p => p.SaleId).Distinct().Count() })
            .OrderBy(t => t.MerchantId)
            .ToListAsync(cancellationToken);
        var totals = rows.Select(r => new MerchantMonthTotal(r.MerchantId, r.Collected, r.SalesCount)).ToList();

        var sent = 0;
        foreach (var total in totals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                sent += await NotifyAsync(scope.ServiceProvider, total, monthStart, settings, cancellationToken) ? 1 : 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Resumen mensual: error con el vendedor {MerchantId}; se sigue con el siguiente", total.MerchantId);
            }
        }

        logger.LogInformation("Resumen mensual de {Month:yyyy-MM}: {Merchants} vendedores con cobros, {Sent} avisos nuevos",
            monthStart, totals.Count, sent);
    }

    /// <returns><c>true</c> si se creó el aviso (no existía).</returns>
    private static async Task<bool> NotifyAsync(
        IServiceProvider services, MerchantMonthTotal total, DateOnly month, SystemSettings settings,
        CancellationToken cancellationToken)
    {
        var scopedDb = services.GetRequiredService<AppDbContext>();
        var merchant = await scopedDb.Merchants.AsNoTracking()
            .Where(m => m.Id == total.MerchantId)
            .Select(m => new { m.OwnerName, m.BusinessName })
            .SingleAsync(cancellationToken);
        var isFounder = await scopedDb.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.MerchantId == total.MerchantId)
            .Select(s => s.IsFounder)
            .SingleOrDefaultAsync(cancellationToken);
        var plan = await services.GetRequiredService<IPlanAccess>().GetForMerchantAsync(total.MerchantId, cancellationToken);
        // Hasta la 5.16 (resumen por plan) el vencido recibe el texto que tenía el gratuito.
        var isFree = plan.Tier == PlanTier.Expired;

        var summary = Domain.Subscriptions.MonthlySummary.For(total.Collected, total.SalesCount, settings, isFounder);
        var message = MerchantEmails.MonthlySummary(
            summary, month, isFree, Merchant.GreetingNameFor(merchant.OwnerName, merchant.BusinessName));
        var result = await services.GetRequiredService<INotificationService>().NotifyAsync(new NotificationRequest(
            total.MerchantId, NotificationType.MonthlySummary, message.Title, message.Body,
            isFree ? MerchantEmails.PlanPath : MerchantEmails.SalesPath, DedupKeyFor(month), message.Email), cancellationToken);
        return result is NotificationResult.Created;
    }

    private sealed record MerchantMonthTotal(int MerchantId, decimal Collected, int SalesCount);
}
