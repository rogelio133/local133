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

internal static class PeyWebhookJson
{
    public static T? Deserialize<T>(string body) => JsonSerializer.Deserialize<T>(body, PeyJson.Options);
}
