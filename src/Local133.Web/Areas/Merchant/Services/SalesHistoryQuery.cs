using System.Globalization;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Payments;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Services;

/// <summary>Filtros del historial tal como llegan en la URL (<c>?estado=&amp;desde=&amp;hasta=&amp;q=</c>).</summary>
public sealed record SalesHistoryFilters(
    SaleStatusFilter Status = SaleStatusFilter.All, DateOnly? From = null, DateOnly? To = null, string? Query = null);

/// <summary>Ventas que dejan ver los filtros, con los filtros ya normalizados.</summary>
/// <param name="All">Todas las ventas del vendedor, sin filtros.</param>
public sealed record FilteredSales(IQueryable<Sale> Sales, IQueryable<Sale> All, SalesHistoryFilters Filters);

/// <summary>
/// Historial de ventas del vendedor (/app/ventas): de la más reciente a la más antigua, filtrado por estatus efectivo,
/// por días (hora de México) y por búsqueda (folio o concepto), en páginas de <see cref="SalesHistoryViewModel.PageSize"/>.
/// La exportación a Excel (5.9) usa el mismo <see cref="Filter"/>, sin paginar.
/// </summary>
public static class SalesHistoryQuery
{
    public static async Task<SalesHistoryViewModel> BuildAsync(
        AppDbContext db, Domain.Merchants.Merchant merchant, MerchantPlan plan, SalesHistoryFilters filters, int? page,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (sales, all, applied) = Filter(db, merchant.Id, filters, now);

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
                EffectiveStatus(s.Status, s.ExpiresAt, now),
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
            Status = applied.Status,
            From = applied.From,
            To = applied.To,
            Query = applied.Query,
            Today = today,
            HasAnySales = pageInfo.TotalCount > 0 || await all.AnyAsync(cancellationToken),
            CanExport = plan.Entitlements.CanExportHistory,
            HasCashCut = plan.Entitlements.HasDailyCashCut,
        };
    }

    /// <summary>Aplica los filtros. Todos los planes ven el historial completo, con fechas y búsqueda (5.14).</summary>
    public static FilteredSales Filter(AppDbContext db, int merchantId, SalesHistoryFilters filters, DateTimeOffset now)
    {
        var (from, to) = (filters.From, filters.To);
        if (from is { } f && to is { } t && t < f)
        {
            (from, to) = (to, from);
        }

        var query = SaleSearch.Normalize(filters.Query);

        // El filtro multi-tenant ya limita al vendedor de la sesión; el MerchantId explícito es la consulta en sí.
        var all = db.Sales.AsNoTracking().Where(s => s.MerchantId == merchantId);
        var sales = WithStatus(all, filters.Status, now);
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

        if (query is not null)
        {
            sales = SaleSearch.Apply(sales, query);
        }

        return new FilteredSales(sales, all, new SalesHistoryFilters(filters.Status, from, to, query));
    }

    /// <summary>Estatus efectivo con columnas sueltas (proyecciones): una pendiente con el QR vencido es vencida.</summary>
    public static SaleStatus EffectiveStatus(SaleStatus status, DateTimeOffset? expiresAt, DateTimeOffset now) =>
        status == SaleStatus.Pending && expiresAt is { } expires && now >= expires ? SaleStatus.Expired : status;

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

/// <summary>
/// Búsqueda del historial (5.9; todos los planes desde la 5.14): por folio o por el concepto que escribió el vendedor, sin distinguir mayúsculas
/// ni acentos. En las ventas anteriores al 2026-11-26, que guardan «Tacos Juan · 2 tacos», no busca en el nombre del
/// negocio: si no, «tacos» encontraría todas. Las ventas nuevas guardan solo el concepto y se busca en todo él.
/// </summary>
public static class SaleSearch
{
    /// <summary>Parámetro de la URL: <c>?q=</c>.</summary>
    public const string QueryKey = "q";

    public const int MaxLength = Sale.ConceptTextMaxLength;

    /// <summary>Intercalación insensible a mayúsculas y acentos («platano» encuentra «Plátano»).</summary>
    private const string Collation = "Latin1_General_CI_AI";

    private const string Escape = "\\";

    /// <summary>Texto de la búsqueda sin espacios en los extremos y recortado; vacío ⇒ <c>null</c> (sin búsqueda).</summary>
    public static string? Normalize(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength].TrimEnd();
    }

    /// <summary>
    /// Folio: solo dígitos, con <c>#</c> opcional al inicio («#12»). Un número sin <c>#</c> también se busca en el
    /// concepto («Mesa 12»).
    /// </summary>
    public static int? FolioOf(string query)
    {
        var digits = query.StartsWith('#') ? query[1..].Trim() : query;
        return digits.Length is > 0 and <= 9 && digits.All(char.IsAsciiDigit)
            ? int.Parse(digits, CultureInfo.InvariantCulture)
            : null;
    }

    public static IQueryable<Sale> Apply(IQueryable<Sale> sales, string query)
    {
        var folio = FolioOf(query);
        if (folio is not null && query.StartsWith('#'))
        {
            // «#12» es solo el folio.
            return sales.Where(s => s.Folio == folio);
        }

        var escaped = EscapeLike(query);

        // Ni el nombre del negocio ni el concepto admiten «·», así que el separador aparece a lo más una vez: lo que
        // va después de él es el concepto. Las ventas sin negocio, y todas desde el 2026-11-26, guardan solo el concepto.
        var afterSeparator = "%" + Sale.ConceptSeparator + "%" + escaped + "%";
        var withSeparator = "%" + Sale.ConceptSeparator + "%";
        var anywhere = "%" + escaped + "%";

        return sales.Where(s =>
            (folio != null && s.Folio == folio)
            || (s.Concept != null
                && (EF.Functions.Like(EF.Functions.Collate(s.Concept, Collation), afterSeparator, Escape)
                    || (!EF.Functions.Like(s.Concept, withSeparator, Escape)
                        && EF.Functions.Like(EF.Functions.Collate(s.Concept, Collation), anywhere, Escape)))));
    }

    /// <summary>Escapa los comodines de <c>LIKE</c> (<c>%</c>, <c>_</c>, <c>[</c>) y el propio escape.</summary>
    public static string EscapeLike(string text) =>
        text.Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal)
            .Replace("[", Escape + "[", StringComparison.Ordinal);
}

/// <summary>Resumen del día en México: cobrado (suma de pagos recibidos hoy), ventas pagadas hoy y pendientes.</summary>
public static class DaySummaryQuery
{
    public static async Task<DaySummaryViewModel> BuildAsync(
        AppDbContext db, int merchantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var payments = PaymentsOn(db, merchantId, MexicoTime.Today(now));

        var collected = await payments.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var paidSales = await payments.Select(p => p.SaleId).Distinct().CountAsync(cancellationToken);
        var pending = await SalesHistoryQuery
            .WithStatus(db.Sales.AsNoTracking().Where(s => s.MerchantId == merchantId), SaleStatusFilter.Pending, now)
            .CountAsync(cancellationToken);

        return new DaySummaryViewModel(collected, paidSales, pending);
    }

    /// <summary>Pagos recibidos en ese día (hora de México). Lo comparten el resumen del día y el corte de caja.</summary>
    public static IQueryable<Payment> PaymentsOn(AppDbContext db, int merchantId, DateOnly day)
    {
        var (startUtc, endUtc) = MexicoTime.DayRange(day);
        return db.Payments.AsNoTracking()
            .Where(p => p.MerchantId == merchantId && p.PaidAt >= startUtc && p.PaidAt < endUtc);
    }
}
