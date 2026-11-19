using System.Text.Json;

using Local133.Domain.Pey;

namespace Local133.Infrastructure.Pey;

// Cuerpos tal como viajan a/desde https://api.pey.mx (OpenAPI 2026-10-20). Las respuestas se leen con
// todo opcional y se validan al convertirlas a los DTOs de Application.

internal sealed record PeyRegisterBankAccountBody(string Alias, string Name, string Clabe, PeyEntity Entity);

internal sealed record PeyRegisterBankAccountResponse(string? Id, PeyCodiStatus? CodiStatus);

internal sealed record PeyBankAccountResponse(
    string? Id,
    string? Alias,
    string? Name,
    string? Clabe,
    PeyEntity? Entity,
    string? TaxId,
    PeyCodiStatus? CodiStatus,
    string? ErrorMessage,
    long? DateCreated);

/// <param name="GenerateImage">
/// Siempre <c>false</c>: el QR se dibuja en el navegador (QRCode.js) a partir de <c>qrString</c>; con imagen Pey
/// responde más lento. Es un <c>bool</c> no nulo, así que viaja explícito aunque se omitan los nulos.
/// </param>
internal sealed record PeyCreateQrBody(
    decimal Amount,
    string? Concept,
    int Expiration,
    string? NumRef,
    bool GenerateImage,
    string? BankAccountId,
    IReadOnlyDictionary<string, string>? Metadata);

internal sealed record PeyCreateQrResponse(string? Id, string? QrString);

internal sealed record PeyCreatePushBody(
    decimal Amount,
    string PhoneNumber,
    string? Concept,
    int Expiration,
    string? NumRef,
    string? BankAccountId,
    IReadOnlyDictionary<string, string>? Metadata);

internal sealed record PeyCreatePushResponse(string? TransactionId, string? CodiId);

internal sealed record PeyTransactionResponse(
    string? Id,
    PeyTransactionStatus? Status,
    decimal? Amount,
    PeyAmountBreakdown? AmountBreakdown,
    string? Currency,
    PeyPaymentMethod? PaymentMethod,
    PeyPayableType? PayableType,
    string? PayableId,
    string? Description,
    decimal? ApplicationFee,
    long? DateCreated,
    long? DatePaid,
    PeyTransactionMetadata? Metadata);

internal sealed record PeyAmountBreakdown(decimal? Base, decimal? Tip, decimal? Fee);

internal sealed record PeyTransactionMetadata(
    string? Clabe,
    int? BankCode,
    int? AccountType,
    string? Description,
    string? TrackingKey,
    Dictionary<string, JsonElement>? ExternalMetadata);

internal sealed record PeyTransactionPageResponse(List<PeyTransactionResponse>? Items, string? NextToken);

internal sealed record PeyCreatePaymentLinkBody(
    decimal Amount,
    string SuccessUrl,
    string BackUrl,
    bool HasTip,
    string? ClientReferenceId,
    IReadOnlyDictionary<string, string>? Metadata);

internal sealed record PeyCreatePaymentLinkResponse(string? Id, string? Url);

/// <param name="Status">En minúsculas (<c>pending</c>…): se lee como texto y se convierte en <c>PeyClient</c>.</param>
internal sealed record PeyPaymentLinkResponse(
    string? Id,
    decimal? Amount,
    string? Status,
    string? SuccessUrl,
    string? BackUrl,
    bool? HasTip,
    List<string>? PaymentMethods,
    long? DateCreated,
    string? ClientReferenceId,
    Dictionary<string, JsonElement>? Metadata,
    List<PeyPaymentLinkTransactionResponse>? Transactions);

internal sealed record PeyPaymentLinkTransactionResponse(
    string? TransactionId,
    string? PaymentMethod,
    long? DatePaid,
    decimal? Amount,
    PeyAmountBreakdown? AmountBreakdown,
    decimal? ApplicationFee,
    Dictionary<string, JsonElement>? Metadata);

/// <summary>Error de Pey: <c>{ "error": { message, type, requestId } }</c>; <c>message</c> suelto por si responde el gateway.</summary>
internal sealed record PeyErrorEnvelope(PeyErrorBody? Error, string? Message);

internal sealed record PeyErrorBody(string? Message, string? Type, string? RequestId);
