using System.Linq.Expressions;
using Local133.Domain.Common;
using Local133.Domain.Payments;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Domain.Webhooks;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Admin.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Admin.Services;

/// <summary>
/// Tablero del admin (/admin/tablero, 6.4). Lo guarda <see cref="DashboardCache"/> unos minutos. El admin no tiene tenant: ve a todos los vendedores.
/// <list type="bullet">
/// <item>GMV = suma de <c>Payments.Amount</c> por <c>PaidAt</c> en hora de México (el mismo criterio del resumen del día);
/// las ventas se cuentan distintas.</item>
/// <item>Ingresos por suscripción = cargos <c>Paid</c> por <c>PaidAt</c> (también los abonados a mano), en total y por plan.</item>
/// <item>Vendedores por plan con el filtro del listado de clientes (<see cref="CustomerListQuery.CountByPlanAsync"/>).</item>
/// </list>
/// </summary>
public static class DashboardQuery
{
    public static async Task<DashboardViewModel> BuildAsync(AppDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Id == SystemSettings.SingletonId)
            .Select(s => new { s.GraceDays, s.FounderSlots, s.VatRate, s.FairUseMonthlyPaid })
            .SingleAsync(cancellationToken);
        var today = MexicoTime.Today(now);

        var (todayStart, todayEnd) = MexicoTime.DayRange(today);
        var gmvToday = await GmvAsync(db, todayStart, todayEnd, cancellationToken);

        var months = await MonthsAsync(db, today, settings.VatRate, cancellationToken);

        var byStatus = await db.Merchants.AsNoTracking()
            .GroupBy(m => m.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        foreach (var status in MerchantStatusFilters.All)
        {
            byStatus.TryAdd(status, 0);
        }

        var recentFrom = MexicoTime.AddDays(now, -DashboardViewModel.RecentTrialDays);
        var (monthStart, monthEnd) = MexicoTime.MonthRange(today);

        return new DashboardViewModel
        {
            Today = today,
            GeneratedAt = now,
            GmvToday = gmvToday,
            Months = months,
            ByPlan = await CustomerListQuery.CountByPlanAsync(db, now, settings.GraceDays, cancellationToken),
            ByStatus = byStatus,
            Conversion = await ConversionAsync(db, null, now, cancellationToken),
            RecentConversion = await ConversionAsync(db, recentFrom, now, cancellationToken),
            RecentUpgrades = await db.SubscriptionPeriods.AsNoTracking()
                .Where(p => p.Kind == SubscriptionPeriodKind.Conversion && p.CreatedAt > recentFrom && p.CreatedAt <= now)
                .Select(p => p.MerchantId)
                .Distinct()
                .CountAsync(cancellationToken),
            FoundersAssigned = await db.Subscriptions.CountAsync(s => s.FounderNumber != null, cancellationToken),
            FounderSlots = settings.FounderSlots,
            FailedWebhooks = await db.WebhookEvents.CountAsync(e => e.Status == WebhookEventStatus.Failed, cancellationToken),
            StuckWebhooks = await StuckWebhooks(db, now).CountAsync(cancellationToken),
            FairUseLimit = settings.FairUseMonthlyPaid,
            FairUse = await FairUseAsync(db, monthStart, monthEnd, settings.FairUseMonthlyPaid, cancellationToken),
        };
    }

    /// <summary>Eventos sin procesar desde hace más de <see cref="WebhookLog.StuckAfter"/>.</summary>
    public static IQueryable<WebhookEvent> StuckWebhooks(AppDbContext db, DateTimeOffset now)
    {
        var before = now - WebhookLog.StuckAfter;
        return db.WebhookEvents.Where(e => e.Status == WebhookEventStatus.Received && e.ReceivedAt <= before);
    }

    private static async Task<GmvFigure> GmvAsync(
        AppDbContext db, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var payments = db.Payments.AsNoTracking().Where(p => p.PaidAt >= start && p.PaidAt < end);
        var amount = await payments.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var sales = await payments.Select(p => p.SaleId).Distinct().CountAsync(cancellationToken);
        return new GmvFigure(amount, sales);
    }

    /// <summary>
    /// GMV e ingresos de los últimos <see cref="DashboardViewModel.MonthsShown"/> meses en dos consultas (pagos y cargos),
    /// agrupadas por mes con los mismos límites de <see cref="MexicoTime.MonthRange"/>: un <c>CASE</c> con los inicios de
    /// mes ya convertidos a UTC, sin conversión de zona horaria en SQL.
    /// </summary>
    private static async Task<List<DashboardMonthRow>> MonthsAsync(
        AppDbContext db, DateOnly today, decimal vatRate, CancellationToken cancellationToken)
    {
        var currentMonth = MexicoTime.MonthStart(today);
        var monthDays = Enumerable.Range(0, DashboardViewModel.MonthsShown)
            .Select(i => currentMonth.AddMonths(i - (DashboardViewModel.MonthsShown - 1)))
            .ToList();
        var starts = monthDays.Select(m => MexicoTime.MonthRange(m).StartUtc).ToList();
        var first = starts[0];
        var end = MexicoTime.MonthRange(currentMonth).EndUtc;

        var gmv = await db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= first && p.PaidAt < end)
            .GroupBy(MonthIndex<Payment>(p => p.PaidAt, starts))
            .Select(g => new { Month = g.Key, Amount = g.Sum(p => p.Amount), Sales = g.Select(p => p.SaleId).Distinct().Count() })
            .ToDictionaryAsync(g => g.Month, cancellationToken);

        // Una consulta por plan (agrupada por mes como la del GMV); los totales del mes se suman aquí.
        var revenue = new List<(SubscriptionPlan Plan, int Month, decimal Amount, int Charges, int Manual)>();
        foreach (var plan in RevenueFigure.Plans)
        {
            var rows = await db.SubscriptionCharges.AsNoTracking()
                .Where(c => c.Status == SubscriptionChargeStatus.Paid && c.Plan == plan && c.PaidAt >= first && c.PaidAt < end)
                .GroupBy(MonthIndex<SubscriptionCharge>(c => c.PaidAt!.Value, starts))
                .Select(g => new
                {
                    Month = g.Key,
                    Amount = g.Sum(c => c.Amount),
                    Charges = g.Count(),
                    Manual = g.Count(c => c.CreditedManuallyAt != null),
                })
                .ToListAsync(cancellationToken);
            revenue.AddRange(rows.Select(r => (plan, r.Month, r.Amount, r.Charges, r.Manual)));
        }

        var revenueByMonth = revenue.ToLookup(r => r.Month);

        return monthDays.Select((month, i) =>
        {
            var g = gmv.GetValueOrDefault(i);
            var r = revenueByMonth[i].ToList();
            var amount = r.Sum(x => x.Amount);
            var (net, vat) = SubscriptionPricing.VatBreakdown(amount, vatRate);
            var byPlan = RevenueFigure.Plans.ToDictionary(
                plan => plan,
                plan => new PlanRevenue(r.Where(x => x.Plan == plan).Sum(x => x.Amount), r.Where(x => x.Plan == plan).Sum(x => x.Charges)));
            return new DashboardMonthRow(
                month,
                DashboardFormats.Month(month),
                new GmvFigure(g?.Amount ?? 0m, g?.Sales ?? 0),
                new RevenueFigure(amount, net, vat, r.Sum(x => x.Charges), r.Sum(x => x.Manual), byPlan));
        }).ToList();
    }

    /// <summary>
    /// <c>x =&gt; instante &lt; inicio[1] ? 0 : instante &lt; inicio[2] ? 1 : … : n-1</c> (EF lo traduce a un <c>CASE</c>). La
    /// consulta ya filtra al rango de los meses.
    /// </summary>
    private static Expression<Func<T, int>> MonthIndex<T>(Expression<Func<T, DateTimeOffset>> instant, IReadOnlyList<DateTimeOffset> starts)
    {
        Expression body = Expression.Constant(starts.Count - 1);
        for (var i = starts.Count - 2; i >= 0; i--)
        {
            body = Expression.Condition(
                Expression.LessThan(instant.Body, Expression.Constant(starts[i + 1])), Expression.Constant(i), body);
        }

        return Expression.Lambda<Func<T, int>>(body, instant.Parameters);
    }

    /// <summary>
    /// De los vendedores cuya prueba terminó (desde <paramref name="endedFrom"/>, o en cualquier momento), los que
    /// tienen al menos un cargo pagado, de cualquier plan.
    /// </summary>
    private static async Task<TrialConversion> ConversionAsync(
        AppDbContext db, DateTimeOffset? endedFrom, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var trials = db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.Kind == SubscriptionPeriodKind.Trial && p.EndsAt <= now);
        if (endedFrom is { } from)
        {
            trials = trials.Where(p => p.EndsAt > from);
        }

        var merchants = trials.Select(p => p.MerchantId).Distinct();
        var ended = await merchants.CountAsync(cancellationToken);
        var paid = await merchants
            .CountAsync(id => db.SubscriptionCharges.Any(c => c.MerchantId == id && c.Status == SubscriptionChargeStatus.Paid), cancellationToken);
        return new TrialConversion(ended, paid);
    }

    /// <summary>Vendedores con cobros concretados este mes en el uso justo o más, en los dos planes (los que más cobran primero).</summary>
    private static async Task<IReadOnlyList<FairUseRow>> FairUseAsync(
        AppDbContext db, DateTimeOffset monthStart, DateTimeOffset monthEnd, int limit, CancellationToken cancellationToken)
    {
        var counts = await db.Sales.AsNoTracking()
            .Where(s => s.PaidAt >= monthStart && s.PaidAt < monthEnd)
            .GroupBy(s => s.MerchantId)
            .Select(g => new { MerchantId = g.Key, Count = g.Count() })
            .Where(g => g.Count >= limit)
            .OrderByDescending(g => g.Count)
            .Take(20)
            .ToListAsync(cancellationToken);
        if (counts.Count == 0)
        {
            return [];
        }

        var ids = counts.Select(c => c.MerchantId).ToList();
        var names = await (
                from m in db.Merchants.AsNoTracking()
                join u in db.Users on m.UserId equals u.Id
                where ids.Contains(m.Id)
                select new { m.Id, m.OwnerName, m.BusinessName, u.Email })
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        return counts.Select(c =>
        {
            var m = names.GetValueOrDefault(c.MerchantId);
            var (name, business) = CustomerListQuery.NamesOf(m?.OwnerName, m?.BusinessName, m?.Email);
            return new FairUseRow(c.MerchantId, name, business, c.Count);
        }).ToList();
    }
}
