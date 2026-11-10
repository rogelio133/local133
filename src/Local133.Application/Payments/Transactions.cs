using Local133.Domain.Pey;

namespace Local133.Application.Payments;

/// <summary>Transacción (pago) de Pey. Un QR puede tener 0..N transacciones.</summary>
/// <param name="Amount">Total pagado (MXN).</param>
/// <param name="ApplicationFee">Comisión de Pey sobre la transacción, si aplica.</param>
/// <param name="PayableId">Recurso pagado (QR, push o link), si aplica.</param>
public sealed record GatewayTransaction(
    string Id,
    PeyTransactionStatus Status,
    decimal Amount,
    GatewayAmountBreakdown? AmountBreakdown,
    string? Currency,
    PeyPaymentMethod? PaymentMethod,
    PeyPayableType? PayableType,
    string? PayableId,
    string? Description,
    decimal? ApplicationFee,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? PaidAt,
    GatewayTransactionMetadata Metadata);

/// <summary>Desglose del monto: base + propina + cargo al pagador.</summary>
public sealed record GatewayAmountBreakdown(decimal? Base, decimal? Tip, decimal? Fee);

/// <summary>Metadatos de la transacción; los campos dependen del medio de pago.</summary>
/// <param name="PayerClabe">CLABE del pagador (CoDi).</param>
/// <param name="BankCode">Código Banxico del banco del pagador (5 dígitos).</param>
/// <param name="AccountType">Código Banxico del tipo de cuenta del pagador.</param>
/// <param name="TrackingKey">Clave de rastreo SPEI.</param>
/// <param name="External">
/// Pares que enviamos al crear el cobro (<c>metadata.externalMetadata</c>); siempre como texto. Vacío si no hubo.
/// </param>
public sealed record GatewayTransactionMetadata(
    string? PayerClabe,
    int? BankCode,
    int? AccountType,
    string? Description,
    string? TrackingKey,
    IReadOnlyDictionary<string, string?> External)
{
    public static GatewayTransactionMetadata Empty { get; } =
        new(null, null, null, null, null, new Dictionary<string, string?>());
}

/// <summary>Página de transacciones; <see cref="NextToken"/> es <c>null</c> en la última.</summary>
public sealed record GatewayTransactionPage(IReadOnlyList<GatewayTransaction> Items, string? NextToken);
