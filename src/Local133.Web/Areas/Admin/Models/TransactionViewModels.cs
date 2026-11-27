using System.Globalization;
using Local133.Domain.Sales;
using Local133.Domain.Subscriptions;
using Local133.Domain.Webhooks;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Models;

namespace Local133.Web.Areas.Admin.Models;

/// <summary>Rutas de Transacciones en el admin (relativas a la raíz, para <c>Url.Content("~" + …)</c>).</summary>
public static class TransactionUrls
{
    public const string Sales = "/admin/transacciones";

    public const string Subscriptions = "/admin/transacciones/suscripciones";

    public static string SaleDetail(int saleId) => $"{Sales}/ventas/{saleId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Ventas de un cliente en la lista global.</summary>
    public static string SalesOf(int merchantId) => Sales + new TransactionFilters(MerchantId: merchantId).QueryString();
}

/// <summary>Pestañas de Transacciones.</summary>
public enum TransactionTab
{
    Sales,
    Subscriptions,
}

/// <summary>Filtro por método de cobro (<c>?metodo=</c>).</summary>
public enum SaleMethodFilter
{
    All,
    DynamicQr,
    PushNotification,
    StaticQr,
}

public static class SaleMethodFilters
{
    public const string QueryKey = "metodo";

    public static readonly IReadOnlyList<SaleMethodFilter> All =
        [SaleMethodFilter.DynamicQr, SaleMethodFilter.PushNotification, SaleMethodFilter.StaticQr];

    public static string? Slug(SaleMethodFilter filter) => filter switch
    {
        SaleMethodFilter.DynamicQr => "qr",
        SaleMethodFilter.PushNotification => "celular",
        SaleMethodFilter.StaticQr => "fijo",
        _ => null,
    };

    public static string Label(SaleMethodFilter filter) => filter switch
    {
        SaleMethodFilter.DynamicQr => "QR",
        SaleMethodFilter.PushNotification => "Celular",
        SaleMethodFilter.StaticQr => "QR fijo",
        _ => "Todos",
    };

    public static ChargeMethod? MethodOf(SaleMethodFilter filter) => filter switch
    {
        SaleMethodFilter.DynamicQr => ChargeMethod.DynamicQr,
        SaleMethodFilter.PushNotification => ChargeMethod.PushNotification,
        SaleMethodFilter.StaticQr => ChargeMethod.StaticQr,
        _ => null,
    };

    public static SaleMethodFilter Parse(string? slug) =>
        All.FirstOrDefault(f => string.Equals(Slug(f), slug?.Trim(), StringComparison.OrdinalIgnoreCase), SaleMethodFilter.All);
}

/// <summary>Búsqueda y filtros de las ventas globales.</summary>
/// <param name="MerchantId">Solo las de un cliente (<c>?cliente=</c>).</param>
public sealed record TransactionFilters(
    string? Query = null, SaleStatusFilter Status = SaleStatusFilter.All, SaleMethodFilter Method = SaleMethodFilter.All,
    DateOnly? From = null, DateOnly? To = null, int? MerchantId = null)
{
    public const int QueryMaxLength = 100;

    public const string MerchantKey = "cliente";

    public bool IsFiltered => Query is not null || Status != SaleStatusFilter.All || Method != SaleMethodFilter.All
        || From is not null || To is not null || MerchantId is not null;

    public string QueryString(int? page = null) => AdminQueryString.Build(
        ("q", Query),
        (SaleStatusFilters.QueryKey, SaleStatusFilters.Slug(Status)),
        (SaleMethodFilters.QueryKey, SaleMethodFilters.Slug(Method)),
        ("desde", AdminQueryString.Date(From)),
        ("hasta", AdminQueryString.Date(To)),
        (MerchantKey, MerchantId?.ToString(CultureInfo.InvariantCulture)),
        (PageInfo.QueryKey, AdminQueryString.Page(page)));
}

/// <summary>Una venta en la lista global.</summary>
/// <param name="Status">Estatus efectivo (una pendiente con el QR vencido es vencida).</param>
public sealed record TransactionSaleRow(
    int Id, int MerchantId, string MerchantName, string? BusinessName, int Folio, string CreatedAtText, string? ConceptText,
    string AmountText, SaleStatus Status, ChargeMethod Method, SaleSource Source, string? MaskedPhone, int PaymentsCount);

/// <summary>Ventas globales (/admin/transacciones).</summary>
public sealed class TransactionListViewModel
{
    public const int PageSize = 20;

    public required IReadOnlyList<TransactionSaleRow> Rows { get; init; }

    public required PageInfo Page { get; init; }

    public required TransactionFilters Filters { get; init; }

    public required DateOnly Today { get; init; }

    /// <summary>Suma de los pagos de las ventas del filtro.</summary>
    public required string CollectedText { get; init; }

    /// <summary>Nombre del cliente del filtro <c>?cliente=</c> (si existe).</summary>
    public string? MerchantName { get; init; }

    public string UrlFor(int? page = null) => TransactionUrls.Sales + Filters.QueryString(page);
}

public sealed record TransactionItemRow(string Name, int Quantity, string UnitPriceText, string SubtotalText);

public sealed record TransactionPaymentRow(
    string PeyTransactionId, string AmountText, string PaidAtText, string? TrackingKey, int? PayerBankCode);

/// <summary>Webhook relacionado (en el detalle de una venta).</summary>
public sealed record RelatedWebhookRow(int Id, string TypeLabel, WebhookEventStatus Status, string ReceivedAtText);

/// <summary>Detalle de una venta en el admin (/admin/transacciones/ventas/{id}).</summary>
public sealed class TransactionSaleDetailViewModel
{
    /// <summary>Webhooks relacionados que se muestran (búsqueda en el payload).</summary>
    public const int WebhooksShown = 10;

    public required int Id { get; init; }

    public required int MerchantId { get; init; }

    public required string MerchantName { get; init; }

    public string? BusinessName { get; init; }

    public required int Folio { get; init; }

    public required SaleStatus Status { get; init; }

    public required ChargeMethod Method { get; init; }

    public required SaleSource Source { get; init; }

    public required string AmountText { get; init; }

    public string? ConceptText { get; init; }

    public required string CreatedAtText { get; init; }

    public string? ExpiresAtText { get; init; }

    public string? PaidAtText { get; init; }

    public string? CancelledAtText { get; init; }

    public string? MaskedPhone { get; init; }

    public string? PeyQrId { get; init; }

    public string? PeyTransactionId { get; init; }

    public required IReadOnlyList<TransactionItemRow> Items { get; init; }

    public required IReadOnlyList<TransactionPaymentRow> Payments { get; init; }

    public required IReadOnlyList<RelatedWebhookRow> Webhooks { get; init; }
}

/// <summary>Filtro por método del pago de la suscripción (<c>?metodo=</c>).</summary>
public enum SubscriptionPaymentMethodFilter
{
    All,
    Codi,
    Card,
}

public static class SubscriptionPaymentMethodFilters
{
    public const string QueryKey = "metodo";

    public static readonly IReadOnlyList<SubscriptionPaymentMethodFilter> All =
        [SubscriptionPaymentMethodFilter.Codi, SubscriptionPaymentMethodFilter.Card];

    public static string? Slug(SubscriptionPaymentMethodFilter filter) => filter switch
    {
        SubscriptionPaymentMethodFilter.Codi => "codi",
        SubscriptionPaymentMethodFilter.Card => "tarjeta",
        _ => null,
    };

    public static string Label(SubscriptionPaymentMethodFilter filter) => filter switch
    {
        SubscriptionPaymentMethodFilter.Codi => "CoDi",
        SubscriptionPaymentMethodFilter.Card => "Tarjeta",
        _ => "Todos",
    };

    /// <summary>Valor de Pey (<c>paymentMethod</c>).</summary>
    public static string? PeyValue(SubscriptionPaymentMethodFilter filter) => filter switch
    {
        SubscriptionPaymentMethodFilter.Codi => "CODI",
        SubscriptionPaymentMethodFilter.Card => "CARD",
        _ => null,
    };

    /// <summary>Texto del método que guardó Pey.</summary>
    public static string Text(string? peyValue) => peyValue?.ToUpperInvariant() switch
    {
        "CODI" => "CoDi",
        "CARD" => "Tarjeta",
        null or "" => "—",
        _ => peyValue,
    };

    public static SubscriptionPaymentMethodFilter Parse(string? slug) =>
        All.FirstOrDefault(f => string.Equals(Slug(f), slug?.Trim(), StringComparison.OrdinalIgnoreCase), SubscriptionPaymentMethodFilter.All);
}

/// <summary>Búsqueda y filtros de los pagos de la suscripción.</summary>
public sealed record SubscriptionPaymentFilters(
    string? Query = null, SubscriptionPaymentMethodFilter Method = SubscriptionPaymentMethodFilter.All,
    DateOnly? From = null, DateOnly? To = null)
{
    public const int QueryMaxLength = 100;

    public bool IsFiltered => Query is not null || Method != SubscriptionPaymentMethodFilter.All || From is not null || To is not null;

    public string QueryString(int? page = null) => AdminQueryString.Build(
        ("q", Query),
        (SubscriptionPaymentMethodFilters.QueryKey, SubscriptionPaymentMethodFilters.Slug(Method)),
        ("desde", AdminQueryString.Date(From)),
        ("hasta", AdminQueryString.Date(To)),
        (PageInfo.QueryKey, AdminQueryString.Page(page)));
}

/// <summary>Un pago de la suscripción recibido de Pey.</summary>
public sealed record SubscriptionPaymentRow(
    int Id, int MerchantId, string MerchantName, string? BusinessName, int ChargeId, SubscriptionPlan Plan, SubscriptionPackage Package,
    SubscriptionChargeStatus ChargeStatus, string PaidAtText, string MethodText, string AmountText, string BaseText,
    string? FeeText, string PeyTransactionId);

/// <summary>Pagos de la suscripción (/admin/transacciones/suscripciones).</summary>
public sealed class SubscriptionPaymentListViewModel
{
    public const int PageSize = 20;

    public required IReadOnlyList<SubscriptionPaymentRow> Rows { get; init; }

    public required PageInfo Page { get; init; }

    public required SubscriptionPaymentFilters Filters { get; init; }

    public required DateOnly Today { get; init; }

    /// <summary>Suma de lo que pagaron (con comisión, si la hubo) en el filtro.</summary>
    public required string TotalText { get; init; }

    public string UrlFor(int? page = null) => TransactionUrls.Subscriptions + Filters.QueryString(page);
}
