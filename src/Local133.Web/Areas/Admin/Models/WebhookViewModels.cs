using System.Globalization;
using Local133.Domain.Pey;
using Local133.Domain.Webhooks;
using Local133.Web.Models;

namespace Local133.Web.Areas.Admin.Models;

/// <summary>Rutas de la bitácora de webhooks (relativas a la raíz, para <c>Url.Content("~" + …)</c>).</summary>
public static class WebhookUrls
{
    public const string List = "/admin/webhooks";

    public static string Detail(int id) => $"{List}/{id.ToString(CultureInfo.InvariantCulture)}";

    public static string Reprocess(int id) => $"{Detail(id)}/reprocesar";

    public static string WithStatus(WebhookStatusFilter status) => List + new WebhookFilters(Status: status).QueryString();
}

/// <summary>Filtro por estatus (<c>?estatus=</c>). «Atorados» = sin procesar desde hace más de <see cref="WebhookLog.StuckAfter"/>.</summary>
public enum WebhookStatusFilter
{
    All,
    Received,
    Stuck,
    Processed,
    Ignored,
    Failed,
}

public static class WebhookStatusFilters
{
    public const string QueryKey = "estatus";

    public static readonly IReadOnlyList<WebhookStatusFilter> All =
    [
        WebhookStatusFilter.Failed, WebhookStatusFilter.Stuck, WebhookStatusFilter.Received, WebhookStatusFilter.Ignored,
        WebhookStatusFilter.Processed,
    ];

    public static string? Slug(WebhookStatusFilter filter) => filter switch
    {
        WebhookStatusFilter.Received => "recibidos",
        WebhookStatusFilter.Stuck => "atorados",
        WebhookStatusFilter.Processed => "procesados",
        WebhookStatusFilter.Ignored => "ignorados",
        WebhookStatusFilter.Failed => "fallidos",
        _ => null,
    };

    public static string Label(WebhookStatusFilter filter) => filter switch
    {
        WebhookStatusFilter.Received => "Sin procesar",
        WebhookStatusFilter.Stuck => "Atorados",
        WebhookStatusFilter.Processed => "Procesados",
        WebhookStatusFilter.Ignored => "Ignorados",
        WebhookStatusFilter.Failed => "Fallidos",
        _ => "Todos",
    };

    public static WebhookStatusFilter Parse(string? slug) =>
        All.FirstOrDefault(f => string.Equals(Slug(f), slug?.Trim(), StringComparison.OrdinalIgnoreCase), WebhookStatusFilter.All);
}

/// <summary>Filtro por tipo de evento (<c>?tipo=</c>).</summary>
public enum WebhookTypeFilter
{
    All,
    CodiQr,
    CodiPush,
    BankAccount,
    PaymentLink,
    Other,
}

public static class WebhookTypeFilters
{
    public const string QueryKey = "tipo";

    public static readonly IReadOnlyList<WebhookTypeFilter> All =
    [
        WebhookTypeFilter.CodiQr, WebhookTypeFilter.CodiPush, WebhookTypeFilter.BankAccount, WebhookTypeFilter.PaymentLink,
        WebhookTypeFilter.Other,
    ];

    /// <summary>Los tipos que Pey documenta (todo lo demás es «Otro»).</summary>
    public static readonly IReadOnlyList<string> KnownTypes =
    [
        PeyWebhookEventTypes.CodiQrPaymentReceived, PeyWebhookEventTypes.CodiPnPaymentReceived,
        PeyWebhookEventTypes.BankAccountUpdated, PeyWebhookEventTypes.PaymentLinkStatusUpdated,
    ];

    public static string? Slug(WebhookTypeFilter filter) => filter switch
    {
        WebhookTypeFilter.CodiQr => "qr",
        WebhookTypeFilter.CodiPush => "celular",
        WebhookTypeFilter.BankAccount => "cuenta",
        WebhookTypeFilter.PaymentLink => "suscripcion",
        WebhookTypeFilter.Other => "otro",
        _ => null,
    };

    public static string Label(WebhookTypeFilter filter) => filter switch
    {
        WebhookTypeFilter.Other => "Otro",
        WebhookTypeFilter.All => "Todos",
        _ => WebhookEventText.Type(EventTypeOf(filter)),
    };

    public static string? EventTypeOf(WebhookTypeFilter filter) => filter switch
    {
        WebhookTypeFilter.CodiQr => PeyWebhookEventTypes.CodiQrPaymentReceived,
        WebhookTypeFilter.CodiPush => PeyWebhookEventTypes.CodiPnPaymentReceived,
        WebhookTypeFilter.BankAccount => PeyWebhookEventTypes.BankAccountUpdated,
        WebhookTypeFilter.PaymentLink => PeyWebhookEventTypes.PaymentLinkStatusUpdated,
        _ => null,
    };

    public static WebhookTypeFilter Parse(string? slug) =>
        All.FirstOrDefault(f => string.Equals(Slug(f), slug?.Trim(), StringComparison.OrdinalIgnoreCase), WebhookTypeFilter.All);
}

/// <summary>Reglas de la bitácora.</summary>
public static class WebhookLog
{
    /// <summary>Un evento sin procesar desde hace más de esto ya no es una entrega en curso: Pey no lo reintentó o se perdió.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10);

    /// <summary>Búsqueda dentro del payload: a partir de estos caracteres (antes solo el <c>webhook-id</c> exacto).</summary>
    public const int PayloadSearchMinLength = 6;

    public static bool IsStuck(WebhookEventStatus status, DateTimeOffset receivedAt, DateTimeOffset now) =>
        status == WebhookEventStatus.Received && receivedAt <= now - StuckAfter;

    public static string StatusBadge(WebhookEventStatus status, bool stuck) => status switch
    {
        WebhookEventStatus.Processed => "text-bg-success",
        WebhookEventStatus.Ignored => "text-bg-light border",
        WebhookEventStatus.Failed => "text-bg-danger",
        _ => stuck ? "text-bg-warning" : "text-bg-info",
    };

    public static string StatusText(WebhookEventStatus status, bool stuck) =>
        stuck ? "Atorado" : WebhookEventText.Status(status);
}

/// <summary>Búsqueda y filtros de la bitácora.</summary>
public sealed record WebhookFilters(
    string? Query = null, WebhookStatusFilter Status = WebhookStatusFilter.All, WebhookTypeFilter Type = WebhookTypeFilter.All,
    DateOnly? From = null, DateOnly? To = null)
{
    public const int QueryMaxLength = 100;

    public bool IsFiltered => Query is not null || Status != WebhookStatusFilter.All || Type != WebhookTypeFilter.All
        || From is not null || To is not null;

    public string QueryString(int? page = null) => AdminQueryString.Build(
        ("q", Query),
        (WebhookStatusFilters.QueryKey, WebhookStatusFilters.Slug(Status)),
        (WebhookTypeFilters.QueryKey, WebhookTypeFilters.Slug(Type)),
        ("desde", AdminQueryString.Date(From)),
        ("hasta", AdminQueryString.Date(To)),
        (PageInfo.QueryKey, AdminQueryString.Page(page)));
}

/// <summary>Un evento en la bitácora.</summary>
public sealed record WebhookRow(
    int Id, string WebhookId, string TypeLabel, WebhookEventStatus Status, bool IsStuck, int Attempts, string ReceivedAtText,
    string? Error);

/// <summary>Bitácora de webhooks (/admin/webhooks).</summary>
public sealed class WebhookListViewModel
{
    public const int PageSize = 20;

    /// <summary>Caracteres del error que se muestran en la lista.</summary>
    public const int ErrorPreviewLength = 140;

    public required IReadOnlyList<WebhookRow> Rows { get; init; }

    public required PageInfo Page { get; init; }

    public required WebhookFilters Filters { get; init; }

    public required DateOnly Today { get; init; }

    public required int FailedCount { get; init; }

    public required int StuckCount { get; init; }

    /// <summary>La búsqueda era muy corta para buscar en el payload: solo se buscó el <c>webhook-id</c> exacto.</summary>
    public bool QueryTooShortForPayload { get; init; }

    public string UrlFor(int? page = null) => WebhookUrls.List + Filters.QueryString(page);
}

/// <summary>Una entrada del historial de reprocesos.</summary>
public sealed record WebhookAuditRow(string OccurredAtText, string ActorEmail, string Summary, string? Note);

/// <summary>Con qué se relaciona el evento (venta, cargo de la suscripción o cuenta bancaria).</summary>
public sealed record WebhookRelation(string Text, string Url);

/// <summary>Detalle de un evento (/admin/webhooks/{id}).</summary>
public sealed class WebhookDetailViewModel
{
    public required int Id { get; init; }

    public required string WebhookId { get; init; }

    public required string EventType { get; init; }

    public required string TypeLabel { get; init; }

    public required WebhookEventStatus Status { get; init; }

    public required bool IsStuck { get; init; }

    public required int Attempts { get; init; }

    public required string ReceivedAtText { get; init; }

    public required string WebhookTimestampText { get; init; }

    public string? ProcessedAtText { get; init; }

    public string? Error { get; init; }

    /// <summary>Payload enmascarado (<see cref="WebhookPayloadMask"/>): nunca el body crudo.</summary>
    public required string PayloadText { get; init; }

    public required bool PayloadIsJson { get; init; }

    public required bool CanReprocess { get; init; }

    public required IReadOnlyList<WebhookRelation> Relations { get; init; }

    public required IReadOnlyList<WebhookAuditRow> History { get; init; }
}
