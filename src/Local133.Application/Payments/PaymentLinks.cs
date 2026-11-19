using Local133.Domain.Pey;

namespace Local133.Application.Payments;

/// <summary>
/// Creación de un link de pago (página de pago hospedada por Pey). A diferencia del QR y del push, Pey no acepta
/// <c>bankAccountId</c> ni concepto: el dinero cae en la cuenta ligada a la API key.
/// </summary>
/// <param name="Amount">Monto en MXN, mayor a 0, con hasta 2 decimales.</param>
/// <param name="SuccessUrl">URL absoluta HTTPS a la que vuelve el pagador después de pagar.</param>
/// <param name="BackUrl">URL absoluta HTTPS a la que vuelve el pagador si cancela.</param>
/// <param name="HasTip">Permite propina en la página de pago.</param>
/// <param name="ClientReferenceId">Referencia propia (hasta 255 caracteres).</param>
/// <param name="Metadata">Hasta 50 pares (llave ≤ 40, valor ≤ 500); vuelven en el link y en sus transacciones.</param>
public sealed record CreatePaymentLinkRequest(
    decimal Amount,
    Uri SuccessUrl,
    Uri BackUrl,
    bool HasTip = false,
    string? ClientReferenceId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>Link de pago creado.</summary>
/// <param name="Id">ULID del link (con él se consulta su estatus).</param>
/// <param name="Url">Página donde el cliente paga.</param>
public sealed record CreatedPaymentLink(string Id, Uri Url);

/// <summary>Link de pago consultado con <c>GET /payment-link/{id}</c>.</summary>
/// <param name="PaymentMethods">Medios de pago que ofrece la página (p. ej. <c>CARD</c>).</param>
/// <param name="Metadata">Pares que enviamos al crearlo (texto).</param>
public sealed record GatewayPaymentLink(
    string Id,
    decimal Amount,
    PeyPaymentLinkStatus Status,
    string? SuccessUrl,
    string? BackUrl,
    bool HasTip,
    IReadOnlyList<string> PaymentMethods,
    DateTimeOffset? CreatedAt,
    string? ClientReferenceId,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<GatewayPaymentLinkTransaction> Transactions);

/// <summary>Pago recibido en un link (un link <c>partial</c> puede tener varios).</summary>
public sealed record GatewayPaymentLinkTransaction(
    string TransactionId,
    string? PaymentMethod,
    DateTimeOffset? PaidAt,
    decimal Amount,
    GatewayAmountBreakdown? AmountBreakdown,
    decimal? ApplicationFee,
    IReadOnlyDictionary<string, string?> Metadata);
