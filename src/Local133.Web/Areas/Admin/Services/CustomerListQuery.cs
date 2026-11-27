using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Admin.Models;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Admin.Services;

/// <summary>
/// Listado de clientes del admin (/admin/clientes): búsqueda, filtros de estatus, plan, vencimiento y fecha de
/// registro, en páginas de <see cref="CustomerListViewModel.PageSize"/>. El admin no tiene tenant: ve a todos.
/// </summary>
public static class CustomerListQuery
{
    /// <summary>Datos de un vendedor que se consultan en SQL antes de calcular su plan.</summary>
    private sealed class MerchantData
    {
        public int Id { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public MerchantStatus Status { get; init; }

        public string? OwnerName { get; init; }

        public string? BusinessName { get; init; }

        public string? Email { get; init; }

        public DateTimeOffset? ProUntil { get; init; }

        public SubscriptionPeriodKind? LastPeriodKind { get; init; }

        public PeyCodiStatus? CodiStatus { get; init; }
    }

    public static async Task<CustomerListViewModel> BuildAsync(
        AppDbContext db, CustomerFilters filters, int? page, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (filters.From is { } f && filters.To is { } t && t < f)
        {
            filters = filters with { From = t, To = f };
        }

        var graceDays = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Id == SystemSettings.SingletonId).Select(s => s.GraceDays).SingleAsync(cancellationToken);

        var query = Merchants(db);
        if (filters.Query is { Length: > 0 } text)
        {
            var id = int.TryParse(text, out var parsed) ? parsed : (int?)null;
            query = query.Where(m =>
                (m.OwnerName != null && m.OwnerName.Contains(text))
                || (m.BusinessName != null && m.BusinessName.Contains(text))
                || (m.Email != null && m.Email.Contains(text))
                || (id != null && m.Id == id));
        }

        if (filters.Status is { } status)
        {
            query = query.Where(m => m.Status == status);
        }

        if (filters.Plan is { } tier)
        {
            query = WithPlan(db, query, tier, now, graceDays);
        }

        if (filters.Expiry is { } expiry)
        {
            query = expiry switch
            {
                ExpiryFilter.Expired => query.Where(m => m.ProUntil != null && m.ProUntil <= now),
                _ => WithExpiryWithin(query, now, MexicoTime.AddDays(now, expiry == ExpiryFilter.Within7Days ? 7 : 30)),
            };
        }

        if (filters.From is { } from)
        {
            var startUtc = MexicoTime.DayRange(from).StartUtc;
            query = query.Where(m => m.CreatedAt >= startUtc);
        }

        if (filters.To is { } to)
        {
            var endUtc = MexicoTime.DayRange(to).EndUtc;
            query = query.Where(m => m.CreatedAt < endUtc);
        }

        var pageInfo = PageInfo.For(page, CustomerListViewModel.PageSize, await query.CountAsync(cancellationToken));
        var ordered = filters.Expiry is null
            ? query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            : query.OrderBy(m => m.ProUntil).ThenBy(m => m.Id);
        var rows = await ordered.Skip(pageInfo.Skip).Take(pageInfo.PageSize).ToListAsync(cancellationToken);

        // El plan que se muestra se calcula exacto con los periodos (solo de esta página).
        var ids = rows.Select(r => r.Id).ToList();
        var periods = (await db.SubscriptionPeriods.AsNoTracking().Where(p => ids.Contains(p.MerchantId)).ToListAsync(cancellationToken))
            .ToLookup(p => p.MerchantId);
        var today = MexicoTime.Today(now);

        return new CustomerListViewModel
        {
            Rows = rows.Select(r =>
            {
                var plan = PlanStatus.For(periods[r.Id], now, graceDays);
                var (name, business) = NamesOf(r.OwnerName, r.BusinessName, r.Email);
                return new CustomerRow(
                    r.Id, name, business, r.Email ?? string.Empty, r.Status, plan, AdminLabels.PlanDate(plan, today),
                    r.CodiStatus, SaleFormats.Date(MexicoTime.DateOf(r.CreatedAt), today));
            }).ToList(),
            Page = pageInfo,
            Filters = filters,
            Today = today,
            HasAnyMerchants = pageInfo.TotalCount > 0 || await db.Merchants.AnyAsync(cancellationToken),
        };
    }

    /// <summary>
    /// Vendedores por estado del plan (tablero, 6.4): un <c>COUNT</c> por estado con el mismo filtro del listado
    /// (<see cref="WithPlan"/>), así que cada número coincide con lo que muestra <c>/admin/clientes?plan=…</c>.
    /// </summary>
    public static async Task<IReadOnlyDictionary<PlanTier, int>> CountByPlanAsync(
        AppDbContext db, DateTimeOffset now, int graceDays, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<PlanTier, int>();
        foreach (var tier in PlanTierFilters.All)
        {
            counts[tier] = await WithPlan(db, Merchants(db), tier, now, graceDays).CountAsync(cancellationToken);
        }

        return counts;
    }

    /// <summary>Vendedores con su usuario, suscripción y cuenta bancaria (base del listado y de los conteos).</summary>
    private static IQueryable<MerchantData> Merchants(AppDbContext db) =>
        from m in db.Merchants.AsNoTracking()
        join u in db.Users on m.UserId equals u.Id
        join s in db.Subscriptions on m.Id equals s.MerchantId into subscriptions
        from s in subscriptions.DefaultIfEmpty()
        join b in db.BankAccounts on m.Id equals b.MerchantId into accounts
        from b in accounts.DefaultIfEmpty()
        select new MerchantData
        {
            Id = m.Id,
            CreatedAt = m.CreatedAt,
            Status = m.Status,
            OwnerName = m.OwnerName,
            BusinessName = m.BusinessName,
            Email = u.Email,
            ProUntil = (DateTimeOffset?)s.ProUntil,
            LastPeriodKind = (SubscriptionPeriodKind?)s.LastPeriodKind,
            CodiStatus = (PeyCodiStatus?)b.CodiStatus,
        };

    /// <summary>
    /// Nombre a mostrar: el de la persona; sin él, el negocio; sin ninguno, el correo. El negocio va aparte solo si no
    /// es ya el nombre.
    /// </summary>
    public static (string Name, string? BusinessName) NamesOf(string? ownerName, string? businessName, string? email)
    {
        var business = string.IsNullOrWhiteSpace(businessName) ? null : businessName.Trim();
        if (!string.IsNullOrWhiteSpace(ownerName))
        {
            return (ownerName.Trim(), business);
        }

        return business is not null ? (business, null) : (email ?? "Sin nombre", null);
    }

    private static IQueryable<MerchantData> WithExpiryWithin(IQueryable<MerchantData> query, DateTimeOffset now, DateTimeOffset until) =>
        query.Where(m => m.ProUntil != null && m.ProUntil > now && m.ProUntil <= until);

    /// <summary>
    /// Filtro del plan traducible a SQL, equivalente a <see cref="PlanStatus.For"/>. Usa que los periodos van
    /// encadenados (cada uno empieza donde termina el anterior o, si ya venció, al crearse), así que el instante está
    /// cubierto si y solo si es anterior a <c>ProUntil</c>:
    /// <list type="bullet">
    /// <item>sin suscripción ⇒ onboarding;</item>
    /// <item>antes de <c>ProUntil</c>: prueba si solo la prueba cubre el instante y no hay un pago de Pro por delante; si
    /// no, Pro (hasta la 5.17 incluye a Básico);</item>
    /// <item>después: gracia si el último periodo es pagado y venció hace menos de <c>GraceDays</c>; si no, vencido.</item>
    /// </list>
    /// </summary>
    private static IQueryable<MerchantData> WithPlan(
        AppDbContext db, IQueryable<MerchantData> query, PlanTier tier, DateTimeOffset now, int graceDays)
    {
        var periods = db.SubscriptionPeriods;
        // La gracia termina en MexicoTime.AddDays(ProUntil, GraceDays) > now ⇔ ProUntil > MexicoTime.AddDays(now, -GraceDays).
        var graceFrom = MexicoTime.AddDays(now, -graceDays);
        return tier switch
        {
            PlanTier.Onboarding => query.Where(m => m.ProUntil == null),
            PlanTier.Trial => query.Where(m => m.ProUntil != null && m.ProUntil > now
                && periods.Any(p => p.MerchantId == m.Id && p.Kind == SubscriptionPeriodKind.Trial && p.StartsAt <= now && p.EndsAt > now)
                && !periods.Any(p => p.MerchantId == m.Id
                    && ((p.Kind != SubscriptionPeriodKind.Trial && p.StartsAt <= now && p.EndsAt > now)
                        || (p.Kind == SubscriptionPeriodKind.Paid && p.Plan == SubscriptionPlan.Pro && p.EndsAt > now)))),
            PlanTier.Pro => query.Where(m => m.ProUntil != null && m.ProUntil > now
                && !(periods.Any(p => p.MerchantId == m.Id && p.Kind == SubscriptionPeriodKind.Trial && p.StartsAt <= now && p.EndsAt > now)
                    && !periods.Any(p => p.MerchantId == m.Id
                        && ((p.Kind != SubscriptionPeriodKind.Trial && p.StartsAt <= now && p.EndsAt > now)
                            || (p.Kind == SubscriptionPeriodKind.Paid && p.Plan == SubscriptionPlan.Pro && p.EndsAt > now))))),
            PlanTier.Grace => query.Where(m => m.ProUntil != null && m.ProUntil <= now && m.ProUntil > graceFrom
                && m.LastPeriodKind == SubscriptionPeriodKind.Paid),
            PlanTier.Expired => query.Where(m => m.ProUntil != null && m.ProUntil <= now
                && !(m.ProUntil > graceFrom && m.LastPeriodKind == SubscriptionPeriodKind.Paid)),
            _ => query,
        };
    }
}
