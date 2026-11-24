using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Services;

/// <summary>
/// Corte de caja de un día en hora de México (5.9, Pro). Lo cobrado sale de los pagos recibidos ese día
/// (<see cref="DaySummaryQuery.PaymentsOn"/>, igual que el resumen de Ventas), así que una venta creada ayer y pagada
/// hoy cuenta hoy. Las pendientes y vencidas son las ventas <b>creadas</b> ese día, con el estatus efectivo
/// (<see cref="SalesHistoryQuery.WithStatus"/>).
/// </summary>
public static class CashCutQuery
{
    public static async Task<CashCutViewModel> BuildAsync(
        AppDbContext db, Domain.Merchants.Merchant merchant, DateOnly day, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var today = MexicoTime.Today(now);
        var payments = DaySummaryQuery.PaymentsOn(db, merchant.Id, day);

        // Un renglón por venta pagada en el día (una venta con dos pagos el mismo día cuenta una vez).
        var paidSales = await payments
            .Join(db.Sales.AsNoTracking(), p => p.SaleId, s => s.Id, (p, s) => new { p.SaleId, p.Amount, s.Method, s.Source })
            .GroupBy(x => new { x.SaleId, x.Method, x.Source })
            .Select(g => new { g.Key.SaleId, g.Key.Method, g.Key.Source, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var saleIds = paidSales.Select(s => s.SaleId).ToList();
        var products = saleIds.Count == 0
            ? []
            : await db.SaleItems.AsNoTracking()
                .Where(i => i.MerchantId == merchant.Id && saleIds.Contains(i.SaleId))
                .GroupBy(i => i.Name)
                .Select(g => new CashCutProductRow(g.Key, g.Sum(i => i.Quantity), g.Sum(i => i.Amount)))
                .ToListAsync(cancellationToken);

        var (startUtc, endUtc) = MexicoTime.DayRange(day);
        var createdThatDay = db.Sales.AsNoTracking()
            .Where(s => s.MerchantId == merchant.Id && s.CreatedAt >= startUtc && s.CreatedAt < endUtc);
        var pending = await OpenSalesAsync(createdThatDay, SaleStatusFilter.Pending, day, now, cancellationToken);
        var expired = await OpenSalesAsync(createdThatDay, SaleStatusFilter.Expired, day, now, cancellationToken);

        return new CashCutViewModel
        {
            Day = day,
            Today = today,
            BusinessName = merchant.BusinessName,
            Collected = paidSales.Sum(s => s.Amount),
            PaidSalesCount = paidSales.Count,
            ByMethod = Breakdown(
                [ChargeMethod.DynamicQr, ChargeMethod.PushNotification],
                ChargeMethod.StaticQr,
                paidSales.Select(s => (s.Method, s.Amount)),
                SaleChannelText.Method,
                SaleChannelText.MethodIcon),
            BySource = Breakdown(
                [SaleSource.DirectAmount, SaleSource.Cart],
                SaleSource.OpenAmount,
                paidSales.Select(s => (s.Source, s.Amount)),
                SaleChannelText.Source,
                SaleChannelText.SourceIcon),
            Products = products
                .OrderByDescending(p => p.Quantity).ThenByDescending(p => p.Amount).ThenBy(p => p.Name, StringComparer.CurrentCulture)
                .ToList(),
            Pending = pending,
            Expired = expired,
        };
    }

    /// <summary>
    /// Renglones fijos (siempre se muestran, aunque sean cero) y uno opcional que solo sale si hubo ventas
    /// (el QR fijo, que todavía no existe para la mayoría).
    /// </summary>
    private static List<CashCutBreakdownRow> Breakdown<T>(
        IReadOnlyList<T> always, T optional, IEnumerable<(T Key, decimal Amount)> sales, Func<T, string> label,
        Func<T, string> icon)
        where T : struct, Enum
    {
        var byKey = sales.GroupBy(s => s.Key).ToDictionary(g => g.Key, g => (Count: g.Count(), Amount: g.Sum(s => s.Amount)));
        var keys = byKey.ContainsKey(optional) ? [.. always, optional] : always.ToList();
        return keys
            .Select(k => byKey.TryGetValue(k, out var value)
                ? new CashCutBreakdownRow(label(k), icon(k), value.Count, value.Amount)
                : new CashCutBreakdownRow(label(k), icon(k), 0, 0m))
            .ToList();
    }

    private static async Task<CashCutOpenSales> OpenSalesAsync(
        IQueryable<Sale> createdThatDay, SaleStatusFilter status, DateOnly day, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sales = SalesHistoryQuery.WithStatus(createdThatDay, status, now);
        var count = await sales.CountAsync(cancellationToken);
        var amount = count == 0 ? 0m : await sales.SumAsync(s => s.Amount, cancellationToken);
        var iso = SaleFormats.IsoDate(day);
        var url = $"{SalesUrls.List}?{SaleStatusFilters.QueryKey}={SaleStatusFilters.Slug(status)}&desde={iso}&hasta={iso}";
        return new CashCutOpenSales(count, amount, url);
    }
}
