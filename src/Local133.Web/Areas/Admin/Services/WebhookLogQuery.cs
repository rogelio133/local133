using System.Globalization;
using System.Text.Json;
using Local133.Domain.Audit;
using Local133.Domain.Common;
using Local133.Domain.Pey;
using Local133.Domain.Webhooks;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Admin.Models;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Areas.Merchant.Services;
using Local133.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Admin.Services;

/// <summary>
/// Bitácora de webhooks del admin (/admin/webhooks, 6.4). El payload solo sale enmascarado
/// (<see cref="WebhookPayloadMask"/>): trae la CLABE del pagador o del vendedor.
/// </summary>
public static class WebhookLogQuery
{
    private const string Escape = "\\";

    public static async Task<WebhookListViewModel> ListAsync(
        AppDbContext db, WebhookFilters filters, int? page, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (from, to) = AdminDates.Ordered(filters.From, filters.To);
        filters = filters with { From = from, To = to };
        var today = MexicoTime.Today(now);

        var events = db.WebhookEvents.AsNoTracking();
        var stuckBefore = now - WebhookLog.StuckAfter;
        events = filters.Status switch
        {
            WebhookStatusFilter.Received => events.Where(e => e.Status == WebhookEventStatus.Received),
            WebhookStatusFilter.Stuck => events.Where(e => e.Status == WebhookEventStatus.Received && e.ReceivedAt <= stuckBefore),
            WebhookStatusFilter.Processed => events.Where(e => e.Status == WebhookEventStatus.Processed),
            WebhookStatusFilter.Ignored => events.Where(e => e.Status == WebhookEventStatus.Ignored),
            WebhookStatusFilter.Failed => events.Where(e => e.Status == WebhookEventStatus.Failed),
            _ => events,
        };

        if (filters.Type == WebhookTypeFilter.Other)
        {
            var known = WebhookTypeFilters.KnownTypes.ToList();
            events = events.Where(e => !known.Contains(e.EventType));
        }
        else if (WebhookTypeFilters.EventTypeOf(filters.Type) is { } type)
        {
            events = events.Where(e => e.EventType == type);
        }

        if (from is { } start)
        {
            var startUtc = MexicoTime.DayRange(start).StartUtc;
            events = events.Where(e => e.ReceivedAt >= startUtc);
        }

        if (to is { } end)
        {
            var endUtc = MexicoTime.DayRange(end).EndUtc;
            events = events.Where(e => e.ReceivedAt < endUtc);
        }

        var tooShort = false;
        if (filters.Query is { } query)
        {
            if (query.Length >= WebhookLog.PayloadSearchMinLength)
            {
                var like = "%" + SaleSearch.EscapeLike(query) + "%";
                events = events.Where(e => e.WebhookId == query || EF.Functions.Like(e.Payload, like, Escape));
            }
            else
            {
                tooShort = true;
                events = events.Where(e => e.WebhookId == query);
            }
        }

        var pageInfo = PageInfo.For(page, WebhookListViewModel.PageSize, await events.CountAsync(cancellationToken));
        var rows = await events
            .OrderByDescending(e => e.ReceivedAt).ThenByDescending(e => e.Id)
            .Skip(pageInfo.Skip).Take(pageInfo.PageSize)
            .Select(e => new { e.Id, e.WebhookId, e.EventType, e.Status, e.Attempts, e.ReceivedAt, e.Error })
            .ToListAsync(cancellationToken);

        return new WebhookListViewModel
        {
            Rows = rows.Select(e => new WebhookRow(
                e.Id, e.WebhookId, WebhookEventText.Type(e.EventType), e.Status, WebhookLog.IsStuck(e.Status, e.ReceivedAt, now),
                e.Attempts, SaleFormats.DateTime(e.ReceivedAt, today), Preview(e.Error))).ToList(),
            Page = pageInfo,
            Filters = filters,
            Today = today,
            FailedCount = await db.WebhookEvents.CountAsync(e => e.Status == WebhookEventStatus.Failed, cancellationToken),
            StuckCount = await DashboardQuery.StuckWebhooks(db, now).CountAsync(cancellationToken),
            QueryTooShortForPayload = tooShort,
        };
    }

    public static async Task<WebhookDetailViewModel?> DetailAsync(
        AppDbContext db, int id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var webhook = await db.WebhookEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (webhook is null)
        {
            return null;
        }

        var today = MexicoTime.Today(now);
        var (text, isJson) = WebhookPayloadMask.Format(webhook.Payload);
        var history = await db.AuditLogs.AsNoTracking()
            .Where(a => a.WebhookEventId == id && a.Action == AuditAction.WebhookReprocessed)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Select(a => new { a.OccurredAt, a.ActorEmail, a.Summary, a.Note })
            .ToListAsync(cancellationToken);

        return new WebhookDetailViewModel
        {
            Id = webhook.Id,
            WebhookId = webhook.WebhookId,
            EventType = webhook.EventType,
            TypeLabel = WebhookEventText.Type(webhook.EventType),
            Status = webhook.Status,
            IsStuck = WebhookLog.IsStuck(webhook.Status, webhook.ReceivedAt, now),
            Attempts = webhook.Attempts,
            ReceivedAtText = SaleFormats.DateTime(webhook.ReceivedAt, today),
            WebhookTimestampText = SaleFormats.DateTime(webhook.WebhookTimestamp, today),
            ProcessedAtText = webhook.ProcessedAt is { } processed ? SaleFormats.DateTime(processed, today) : null,
            Error = webhook.Error,
            PayloadText = text,
            PayloadIsJson = isJson,
            CanReprocess = webhook.CanReprocess,
            Relations = await RelationsAsync(db, webhook.Payload, cancellationToken),
            History = history.Select(a => new WebhookAuditRow(SaleFormats.DateTime(a.OccurredAt, today), a.ActorEmail, a.Summary, a.Note))
                .ToList(),
        };
    }

    private static string? Preview(string? error) =>
        error is null ? null
        : error.Length <= WebhookListViewModel.ErrorPreviewLength ? error
        : error[..WebhookListViewModel.ErrorPreviewLength].TrimEnd() + "…";

    /// <summary>
    /// Venta, cargo de la suscripción o cuenta bancaria que menciona el payload (por los ids de Pey: <c>codiQrId</c>,
    /// <c>transactionId</c>, <c>paymentLinkId</c>, <c>bankAccountId</c>).
    /// </summary>
    private static async Task<IReadOnlyList<WebhookRelation>> RelationsAsync(
        AppDbContext db, string payload, CancellationToken cancellationToken)
    {
        var ids = TopLevelIds(payload);
        var relations = new List<WebhookRelation>();

        var saleIds = new List<int>();
        if (ids.GetValueOrDefault("codiQrId") is { } qrId)
        {
            saleIds.AddRange(await db.Sales.AsNoTracking().Where(s => s.PeyQrId == qrId).Select(s => s.Id).ToListAsync(cancellationToken));
        }

        if (ids.GetValueOrDefault("transactionId") is { } transactionId)
        {
            saleIds.AddRange(await db.Payments.AsNoTracking().Where(p => p.PeyTransactionId == transactionId)
                .Select(p => p.SaleId).ToListAsync(cancellationToken));
            saleIds.AddRange(await db.Sales.AsNoTracking().Where(s => s.PeyTransactionId == transactionId)
                .Select(s => s.Id).ToListAsync(cancellationToken));
        }

        if (saleIds.Count > 0)
        {
            var distinct = saleIds.Distinct().ToList();
            var sales = await (
                    from s in db.Sales.AsNoTracking()
                    join m in db.Merchants on s.MerchantId equals m.Id
                    join u in db.Users on m.UserId equals u.Id
                    where distinct.Contains(s.Id)
                    select new { s.Id, s.Folio, m.OwnerName, m.BusinessName, u.Email })
                .ToListAsync(cancellationToken);
            relations.AddRange(sales.Select(s => new WebhookRelation(
                $"Venta {s.Folio.ToString(CultureInfo.InvariantCulture)} de {CustomerListQuery.NamesOf(s.OwnerName, s.BusinessName, s.Email).Name}",
                TransactionUrls.SaleDetail(s.Id))));
        }

        if (ids.GetValueOrDefault("paymentLinkId") is { } linkId)
        {
            var charge = await (
                    from c in db.SubscriptionCharges.AsNoTracking()
                    join m in db.Merchants on c.MerchantId equals m.Id
                    join u in db.Users on m.UserId equals u.Id
                    where c.PeyPaymentLinkId == linkId
                    select new { c.Id, c.MerchantId, m.OwnerName, m.BusinessName, u.Email })
                .SingleOrDefaultAsync(cancellationToken);
            if (charge is not null)
            {
                relations.Add(new WebhookRelation(
                    $"Cargo #{charge.Id.ToString(CultureInfo.InvariantCulture)} de la suscripción de {CustomerListQuery.NamesOf(charge.OwnerName, charge.BusinessName, charge.Email).Name}",
                    CustomerUrls.Tab(charge.MerchantId, CustomerTab.Plan)));
            }
        }

        if (ids.GetValueOrDefault("bankAccountId") is { } bankAccountId)
        {
            var account = await (
                    from b in db.BankAccounts.AsNoTracking()
                    join m in db.Merchants on b.MerchantId equals m.Id
                    join u in db.Users on m.UserId equals u.Id
                    where b.PeyBankAccountId == bankAccountId
                    select new { b.MerchantId, m.OwnerName, m.BusinessName, u.Email })
                .SingleOrDefaultAsync(cancellationToken);
            if (account is not null)
            {
                relations.Add(new WebhookRelation(
                    $"Cuenta bancaria de {CustomerListQuery.NamesOf(account.OwnerName, account.BusinessName, account.Email).Name}",
                    CustomerUrls.Tab(account.MerchantId, CustomerTab.Account)));
            }
        }

        return relations;
    }

    /// <summary>Ids de Pey del primer nivel del payload (solo cadenas no vacías).</summary>
    private static Dictionary<string, string> TopLevelIds(string payload)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ids;
            }

            foreach (var key in (string[])["codiQrId", "transactionId", "paymentLinkId", "bankAccountId"])
            {
                if (document.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 and <= 100 } text)
                {
                    ids[key] = text.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // Payload inválido: no hay con qué relacionarlo.
        }

        return ids;
    }
}
