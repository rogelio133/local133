using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Domain.Webhooks;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Admin.Models;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Areas.Merchant.Services;
using Local133.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Admin.Services;

/// <summary>
/// Transacciones globales del admin (/admin/transacciones, 6.4): ventas de todos los vendedores, el detalle de una venta
/// y los pagos de la suscripción. Sin tenant: el admin ve todo. El celular solo va enmascarado.
/// </summary>
public static class TransactionListQuery
{
    private const string Escape = "\\";

    public static async Task<TransactionListViewModel> SalesAsync(
        AppDbContext db, TransactionFilters filters, int? page, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (from, to) = AdminDates.Ordered(filters.From, filters.To);
        filters = filters with { From = from, To = to };
        var today = MexicoTime.Today(now);

        var sales = SalesHistoryQuery.WithStatus(db.Sales.AsNoTracking(), filters.Status, now);
        if (SaleMethodFilters.MethodOf(filters.Method) is { } method)
        {
            sales = sales.Where(s => s.Method == method);
        }

        if (filters.MerchantId is { } merchantId)
        {
            sales = sales.Where(s => s.MerchantId == merchantId);
        }

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

        if (filters.Query is { } query)
        {
            sales = Search(db, sales, query);
        }

        var pageInfo = PageInfo.For(page, TransactionListViewModel.PageSize, await sales.CountAsync(cancellationToken));
        var collected = await db.Payments.AsNoTracking()
            .Where(p => sales.Select(s => s.Id).Contains(p.SaleId))
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        var rows = await (
                from s in sales
                join m in db.Merchants on s.MerchantId equals m.Id
                join u in db.Users on m.UserId equals u.Id
                orderby s.CreatedAt descending, s.Id descending
                select new
                {
                    s.Id, s.MerchantId, m.OwnerName, m.BusinessName, u.Email, s.Folio, s.CreatedAt, s.Concept, s.Amount,
                    s.Status, s.ExpiresAt, s.Method, s.Source, s.PhoneNumber,
                    Payments = db.Payments.Count(p => p.SaleId == s.Id),
                })
            .Skip(pageInfo.Skip).Take(pageInfo.PageSize)
            .ToListAsync(cancellationToken);

        string? merchantName = null;
        if (filters.MerchantId is { } id)
        {
            var merchant = await (
                    from m in db.Merchants.AsNoTracking()
                    join u in db.Users on m.UserId equals u.Id
                    where m.Id == id
                    select new { m.OwnerName, m.BusinessName, u.Email })
                .SingleOrDefaultAsync(cancellationToken);
            merchantName = merchant is null ? null : CustomerListQuery.NamesOf(merchant.OwnerName, merchant.BusinessName, merchant.Email).Name;
        }

        return new TransactionListViewModel
        {
            Rows = rows.Select(r =>
            {
                var (name, business) = CustomerListQuery.NamesOf(r.OwnerName, r.BusinessName, r.Email);
                return new TransactionSaleRow(
                    r.Id, r.MerchantId, name, business, r.Folio, SaleFormats.DateTime(r.CreatedAt, today),
                    Sale.ConceptTextOf(r.Concept), SaleQrViewModel.FormatAmount(r.Amount),
                    SalesHistoryQuery.EffectiveStatus(r.Status, r.ExpiresAt, now), r.Method, r.Source,
                    r.PhoneNumber is null ? null : PhoneNumber.Mask(r.PhoneNumber), r.Payments);
            }).ToList(),
            Page = pageInfo,
            Filters = filters,
            Today = today,
            CollectedText = SaleQrViewModel.FormatAmount(collected),
            MerchantName = merchantName,
        };
    }

    /// <summary>
    /// «#12» o solo dígitos ⇒ folio. Si no: un id exacto de Pey (QR, transacción de la venta o del pago, clave de
    /// rastreo) o el nombre, el negocio o el correo del cliente.
    /// </summary>
    private static IQueryable<Sale> Search(AppDbContext db, IQueryable<Sale> sales, string query)
    {
        if (SaleSearch.FolioOf(query) is { } folio)
        {
            return sales.Where(s => s.Folio == folio);
        }

        var like = "%" + SaleSearch.EscapeLike(query) + "%";
        return sales.Where(s =>
            s.PeyQrId == query
            || s.PeyTransactionId == query
            || db.Payments.Any(p => p.SaleId == s.Id && (p.PeyTransactionId == query || p.TrackingKey == query))
            || db.Merchants.Any(m => m.Id == s.MerchantId
                && ((m.OwnerName != null && EF.Functions.Like(m.OwnerName, like, Escape))
                    || (m.BusinessName != null && EF.Functions.Like(m.BusinessName, like, Escape))
                    || db.Users.Any(u => u.Id == m.UserId && u.Email != null && EF.Functions.Like(u.Email, like, Escape)))));
    }

    public static async Task<TransactionSaleDetailViewModel?> SaleAsync(
        AppDbContext db, int saleId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sale = await db.Sales.AsNoTracking().Include(s => s.Items).SingleOrDefaultAsync(s => s.Id == saleId, cancellationToken);
        if (sale is null)
        {
            return null;
        }

        var today = MexicoTime.Today(now);
        var merchant = await (
                from m in db.Merchants.AsNoTracking()
                join u in db.Users on m.UserId equals u.Id
                where m.Id == sale.MerchantId
                select new { m.OwnerName, m.BusinessName, u.Email })
            .SingleAsync(cancellationToken);
        var (name, business) = CustomerListQuery.NamesOf(merchant.OwnerName, merchant.BusinessName, merchant.Email);

        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.SaleId == saleId)
            .OrderBy(p => p.PaidAt).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        // Webhooks que mencionan el QR o alguna transacción de la venta (búsqueda en el payload: pocas filas por venta).
        var ids = new List<string>();
        if (!string.IsNullOrEmpty(sale.PeyQrId))
        {
            ids.Add(sale.PeyQrId);
        }

        if (!string.IsNullOrEmpty(sale.PeyTransactionId))
        {
            ids.Add(sale.PeyTransactionId);
        }

        ids.AddRange(payments.Select(p => p.PeyTransactionId));
        var webhooks = new List<RelatedWebhookRow>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            if (webhooks.Count >= TransactionSaleDetailViewModel.WebhooksShown)
            {
                break;
            }

            var like = "%\"" + SaleSearch.EscapeLike(id) + "\"%";
            var known = webhooks.Select(w => w.Id).ToList();
            var found = await db.WebhookEvents.AsNoTracking()
                .Where(e => !known.Contains(e.Id) && EF.Functions.Like(e.Payload, like, Escape))
                .OrderByDescending(e => e.ReceivedAt)
                .Take(TransactionSaleDetailViewModel.WebhooksShown - webhooks.Count)
                .Select(e => new { e.Id, e.EventType, e.Status, e.ReceivedAt })
                .ToListAsync(cancellationToken);
            webhooks.AddRange(found.Select(e => new RelatedWebhookRow(
                e.Id, WebhookEventText.Type(e.EventType), e.Status, SaleFormats.DateTime(e.ReceivedAt, today))));
        }

        return new TransactionSaleDetailViewModel
        {
            Id = sale.Id,
            MerchantId = sale.MerchantId,
            MerchantName = name,
            BusinessName = business,
            Folio = sale.Folio,
            Status = sale.EffectiveStatus(now),
            Method = sale.Method,
            Source = sale.Source,
            AmountText = SaleQrViewModel.FormatAmount(sale.Amount),
            ConceptText = Sale.ConceptTextOf(sale.Concept),
            CreatedAtText = SaleFormats.DateTime(sale.CreatedAt, today),
            ExpiresAtText = sale.ExpiresAt is { } expires ? SaleFormats.DateTime(expires, today) : null,
            PaidAtText = sale.PaidAt is { } paid ? SaleFormats.DateTime(paid, today) : null,
            CancelledAtText = sale.CancelledAt is { } cancelled ? SaleFormats.DateTime(cancelled, today) : null,
            MaskedPhone = sale.PhoneNumber is null ? null : PhoneNumber.Mask(sale.PhoneNumber),
            PeyQrId = sale.PeyQrId,
            PeyTransactionId = sale.PeyTransactionId,
            Items = sale.Items.OrderBy(i => i.Id).Select(i => new TransactionItemRow(
                i.Name, i.Quantity, SaleQrViewModel.FormatAmount(i.UnitPrice), SaleQrViewModel.FormatAmount(i.Amount))).ToList(),
            Payments = payments.Select(p => new TransactionPaymentRow(
                p.PeyTransactionId, SaleQrViewModel.FormatAmount(p.Amount), SaleFormats.DateTime(p.PaidAt, today), p.TrackingKey,
                p.PayerBankCode)).ToList(),
            Webhooks = webhooks.OrderByDescending(w => w.Id).ToList(),
        };
    }

    public static async Task<SubscriptionPaymentListViewModel> SubscriptionPaymentsAsync(
        AppDbContext db, SubscriptionPaymentFilters filters, int? page, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (from, to) = AdminDates.Ordered(filters.From, filters.To);
        filters = filters with { From = from, To = to };
        var today = MexicoTime.Today(now);

        var payments = db.SubscriptionPayments.AsNoTracking();
        if (SubscriptionPaymentMethodFilters.PeyValue(filters.Method) is { } method)
        {
            payments = payments.Where(p => p.PaymentMethod == method);
        }

        if (from is { } start)
        {
            var startUtc = MexicoTime.DayRange(start).StartUtc;
            payments = payments.Where(p => p.PaidAt >= startUtc);
        }

        if (to is { } end)
        {
            var endUtc = MexicoTime.DayRange(end).EndUtc;
            payments = payments.Where(p => p.PaidAt < endUtc);
        }

        if (filters.Query is { } query)
        {
            var chargeId = SaleSearch.FolioOf(query);
            var like = "%" + SaleSearch.EscapeLike(query) + "%";
            payments = payments.Where(p =>
                (chargeId != null && p.SubscriptionChargeId == chargeId)
                || p.PeyTransactionId == query
                || db.SubscriptionCharges.Any(c => c.Id == p.SubscriptionChargeId && c.PeyPaymentLinkId == query)
                || db.Merchants.Any(m => m.Id == p.MerchantId
                    && ((m.OwnerName != null && EF.Functions.Like(m.OwnerName, like, Escape))
                        || (m.BusinessName != null && EF.Functions.Like(m.BusinessName, like, Escape))
                        || db.Users.Any(u => u.Id == m.UserId && u.Email != null && EF.Functions.Like(u.Email, like, Escape)))));
        }

        var pageInfo = PageInfo.For(page, SubscriptionPaymentListViewModel.PageSize, await payments.CountAsync(cancellationToken));
        var total = await payments.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var rows = await (
                from p in payments
                join c in db.SubscriptionCharges on p.SubscriptionChargeId equals c.Id
                join m in db.Merchants on p.MerchantId equals m.Id
                join u in db.Users on m.UserId equals u.Id
                orderby p.PaidAt descending, p.Id descending
                select new
                {
                    p.Id, p.MerchantId, m.OwnerName, m.BusinessName, u.Email, ChargeId = c.Id, c.Plan, c.Package, ChargeStatus = c.Status,
                    p.PaidAt, p.PaymentMethod, p.Amount, p.Base, p.Fee, p.PeyTransactionId,
                })
            .Skip(pageInfo.Skip).Take(pageInfo.PageSize)
            .ToListAsync(cancellationToken);

        return new SubscriptionPaymentListViewModel
        {
            Rows = rows.Select(r =>
            {
                var (name, business) = CustomerListQuery.NamesOf(r.OwnerName, r.BusinessName, r.Email);
                return new SubscriptionPaymentRow(
                    r.Id, r.MerchantId, name, business, r.ChargeId, r.Plan, r.Package, r.ChargeStatus, SaleFormats.DateTime(r.PaidAt, today),
                    SubscriptionPaymentMethodFilters.Text(r.PaymentMethod), SaleQrViewModel.FormatAmount(r.Amount),
                    SaleQrViewModel.FormatAmount(r.Base), r.Fee is { } fee ? SaleQrViewModel.FormatAmount(fee) : null,
                    r.PeyTransactionId);
            }).ToList(),
            Page = pageInfo,
            Filters = filters,
            Today = today,
            TotalText = SaleQrViewModel.FormatAmount(total),
        };
    }
}
