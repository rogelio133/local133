using System.Text.Json;

using Local133.Domain.Pey;

namespace Local133.Infrastructure.Pey;

// Payloads de los webhooks de Pey (docs.pey.mx/docs/webhooks, revisado el 2026-11-12). Todo opcional al leer;
// se valida al convertirlos.

/// <summary>Solo el <c>type</c>, para despachar.</summary>
internal sealed record PeyWebhookEnvelope(string? Type);

/// <summary>
/// <c>codi.qr.payment_received</c>: <c>amount</c> en MXN (number), <c>datePaid</c> en segundos Unix, <c>numRef</c> como
/// texto y la metadata de la transacción (con <c>externalMetadata</c>) anidada.
/// </summary>
internal sealed record PeyCodiQrPaymentReceived(
    string? Type,
    decimal? Amount,
    string? ApiKeyId,
    string? BankAccountId,
    string? CodiQrId,
    string? Concept,
    long? DatePaid,
    string? NumRef,
    string? TransactionId,
    PeyTransactionMetadata? Metadata);

/// <summary>
/// <c>codi.pn.payment_received</c> (docs.pey.mx/docs/webhooks/codi/pn, revisado el 2026-11-13): como el de QR pero sin
/// <c>codiQrId</c>; el <c>transactionId</c> es el que devolvió <c>POST /codi/pn</c>. <c>numRef</c> es el de Banxico.
/// </summary>
internal sealed record PeyCodiPnPaymentReceived(
    string? Type,
    decimal? Amount,
    string? ApiKeyId,
    string? BankAccountId,
    string? Concept,
    long? DatePaid,
    string? NumRef,
    string? TransactionId,
    PeyTransactionMetadata? Metadata);

/// <summary><c>bank.account.updated</c>: solo se envía al pasar a <c>ACTIVE</c>; no trae <c>errorMessage</c>.</summary>
internal sealed record PeyBankAccountUpdated(
    string? Type,
    string? BankAccountId,
    string? Name,
    PeyOwnerType? OwnerType,
    string? OwnerId,
    string? TaxId,
    PeyCodiStatus? CodiStatus,
    string? Clabe,
    PeyEntity? Entity);

/// <summary>
/// <c>payment_link.status_updated</c> (docs.pey.mx/docs/webhooks/payment-link/status-update, revisado el 2026-11-20):
/// plano, con <c>status</c> en minúsculas (<c>pending|processing|paid</c>; <c>partial</c> solo en el GET) y las
/// transacciones del link. La metadata que enviamos vuelve en <c>transactions[].metadata.externalMetadata</c>.
/// </summary>
internal sealed record PeyPaymentLinkStatusUpdated(
    string? Type,
    string? ApiKeyId,
    string? PaymentLinkId,
    string? Status,
    string? ClientReferenceId,
    List<PeyPaymentLinkWebhookTransaction>? Transactions);

/// <param name="DatePaid">Segundos Unix.</param>
/// <param name="Amount">Total pagado (base + propina + comisión al pagador).</param>
internal sealed record PeyPaymentLinkWebhookTransaction(
    string? TransactionId,
    string? PaymentMethod,
    long? DatePaid,
    decimal? Amount,
    PeyAmountBreakdown? AmountBreakdown,
    decimal? ApplicationFee,
    PeyTransactionMetadata? Metadata);

internal static class PeyWebhookJson
{
    public static T? Deserialize<T>(string body) => JsonSerializer.Deserialize<T>(body, PeyJson.Options);
}
