using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>
/// Dos conteos por <c>MerchantId</c> explícito y sin filtro multi-tenant (el resultado no depende del claim de la
/// sesión): ventas con <c>PaidAt</c> en el mes (índice <c>(MerchantId, PaidAt)</c>) y ventas creadas en el mes
/// (índice <c>(MerchantId, CreatedAt)</c>).
/// </summary>
internal sealed class ChargeUsage(AppDbContext db, TimeProvider clock) : IChargeUsage
{
    public async Task<MonthlyChargeUsage> GetMonthAsync(int merchantId, CancellationToken cancellationToken = default)
    {
        var today = MexicoTime.Today(clock.GetUtcNow());
        var (startUtc, endUtc) = MexicoTime.MonthRange(today);
        var sales = db.Sales.IgnoreQueryFilters().AsNoTracking().Where(s => s.MerchantId == merchantId);

        var paid = await sales.CountAsync(s => s.PaidAt >= startUtc && s.PaidAt < endUtc, cancellationToken);
        var generated = await sales.CountAsync(s => s.CreatedAt >= startUtc && s.CreatedAt < endUtc, cancellationToken);
        var settings = await db.SystemSettings.AsNoTracking()
            .SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);

        return new MonthlyChargeUsage(
            MexicoTime.MonthStart(today),
            MexicoTime.NextMonthStart(today),
            MonthlyChargeAllowance.For(paid, generated, settings));
    }
}
