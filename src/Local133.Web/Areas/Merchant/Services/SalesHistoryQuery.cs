using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Services;

/// <summary>
/// Historial de ventas del vendedor (/app/ventas): de la más reciente a la más antigua, filtrado por estatus efectivo
/// y por días (hora de México), en páginas de <see cref="SalesHistoryViewModel.PageSize"/>.
/// </summary>
public static class SalesHistoryQuery
{
    public static async Task<SalesHistoryViewModel> BuildAsync(
        AppDbContext db, Domain.Merchants.Merchant merchant, MerchantPlan plan, SaleStatusFilter status, DateOnly? from,
        DateOnly? to, int? page, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Gratuito (5.5): solo los últimos días, con el filtro de estatus; el rango de fechas es de Pro.
        var historyStart = plan.HistoryStartUtc(now);
        if (historyStart is not null)
        {
            (from, to) = (null, null);
        }

        if (from is { } f && to is { } t && t < f)
        {
            (from, to) = (to, from);
        }

        // El filtro multi-tenant ya limita al vendedor de la sesión; el MerchantId explícito es la consulta en sí.
        var all = db.Sales.AsNoTracking().Where(s => s.MerchantId == merchant.Id);
        var visible = historyStart is { } cutoff ? all.Where(s => s.CreatedAt >= cutoff) : all;
        var sales = WithStatus(visible, status, now);
        if (from is { } start)
        {
            var startUtc = MexicoTime.DayRange(start).StartUtc;
            sales = sales.Where(s => s.CreatedAt >= startUtc);
        }

        if (to is { } end)
        {
            var endUtc = MexicoTime.DayRange(end).EndUtc;
            sales = sales.Where(s => s.CreatedAt < endUtc);
        }

        var pageInfo = PageInfo.For(page, SalesHistoryViewModel.PageSize, await sales.CountAsync(cancellationToken));
        var rows = await sales
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip(pageInfo.Skip).Take(pageInfo.PageSize)
            .Select(s => new { s.Id, s.Folio, s.CreatedAt, s.Concept, s.Amount, s.Status, s.ExpiresAt, s.Method, s.Source })
            .ToListAsync(cancellationToken);

        var today = MexicoTime.Today(now);
        var days = rows
            .Select(s => new SaleHistoryRow(
                s.Id,
                s.Folio,
                MexicoTime.DateOf(s.CreatedAt),
                SaleFormats.Time(s.CreatedAt),
                Sale.ConceptTextOf(s.Concept),
                SaleQrViewModel.FormatAmount(s.Amount),
                s.Status == SaleStatus.Pending && s.ExpiresAt is { } expiresAt && now >= expiresAt ? SaleStatus.Expired : s.Status,
                s.Method,
                s.Source))
            .GroupBy(r => r.Day)
            .Select(g => new SaleHistoryDay(SaleFormats.DayLabel(g.Key, today), g.ToList()))
            .ToList();

        return new SalesHistoryViewModel
        {
            Summary = await DaySummaryQuery.BuildAsync(db, merchant.Id, now, cancellationToken),
            Days = days,
            Page = pageInfo,
            Status = status,
            From = from,
            To = to,
            Today = today,
            HasAnySales = pageInfo.TotalCount > 0 || await all.AnyAsync(cancellationToken),
            HistoryDays = historyStart is null ? null : plan.FreeHistoryDays,
            HiddenOlderCount = historyStart is { } hiddenBefore
                ? await all.CountAsync(s => s.CreatedAt < hiddenBefore, cancellationToken)
                : 0,
        };
    }

    /// <summary>
    /// Filtro por estatus efectivo (<see cref="Sale.EffectiveStatus"/>): una pendiente con el QR vencido es vencida
    /// aunque el job de expiración no haya corrido.
    /// </summary>
    public static IQueryable<Sale> WithStatus(IQueryable<Sale> sales, SaleStatusFilter status, DateTimeOffset now) => status switch
    {
        SaleStatusFilter.Paid => sales.Where(s => s.Status == SaleStatus.Paid),
        SaleStatusFilter.Pending => sales.Where(s => s.Status == SaleStatus.Pending && (s.ExpiresAt == null || s.ExpiresAt > now)),
        SaleStatusFilter.Expired => sales.Where(s =>
            s.Status == SaleStatus.Expired || (s.Status == SaleStatus.Pending && s.ExpiresAt != null && s.ExpiresAt <= now)),
        SaleStatusFilter.Cancelled => sales.Where(s => s.Status == SaleStatus.Cancelled),
        _ => sales,
    };
}

/// <summary>Resumen del día en México: cobrado (suma de pagos recibidos hoy), ventas pagadas hoy y pendientes.</summary>
public static class DaySummaryQuery
{
    public static async Task<DaySummaryViewModel> BuildAsync(
        AppDbContext db, int merchantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = MexicoTime.DayRange(MexicoTime.Today(now));
        var payments = db.Payments.AsNoTracking()
            .Where(p => p.MerchantId == merchantId && p.PaidAt >= startUtc && p.PaidAt < endUtc);

        var collected = await payments.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var paidSales = await payments.Select(p => p.SaleId).Distinct().CountAsync(cancellationToken);
        var pending = await SalesHistoryQuery
            .WithStatus(db.Sales.AsNoTracking().Where(s => s.MerchantId == merchantId), SaleStatusFilter.Pending, now)
            .CountAsync(cancellationToken);

        return new DaySummaryViewModel(collected, paidSales, pending);
    }
}
