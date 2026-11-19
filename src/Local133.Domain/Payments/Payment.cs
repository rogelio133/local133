using Local133.Domain.Common;

namespace Local133.Domain.Payments;

/// <summary>
/// Pago recibido por CoDi (una transacción de Pey). Una venta puede tener 0..N pagos. El dinero llega completo a la
/// CLABE del vendedor: la plataforma cobra aparte la suscripción.
/// </summary>
public class Payment : Entity
{
    public const int PeyIdMaxLength = 64;
    public const int TrackingKeyMaxLength = 64;

    public int MerchantId { get; set; }

    public int SaleId { get; set; }

    /// <summary>Id (ULID) de la transacción en Pey; único: el mismo pago nunca se registra dos veces.</summary>
    public string PeyTransactionId { get; set; } = null!;

    /// <summary>Monto pagado (MXN).</summary>
    public decimal Amount { get; set; }

    public DateTimeOffset PaidAt { get; set; }

    /// <summary>Clave de rastreo SPEI.</summary>
    public string? TrackingKey { get; set; }

    /// <summary>Código Banxico del banco del pagador.</summary>
    public int? PayerBankCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Registra el pago de una venta.</summary>
    public static Payment Record(
        int merchantId, int saleId, string peyTransactionId, decimal amount, DateTimeOffset paidAt,
        string? trackingKey, int? payerBankCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peyTransactionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        peyTransactionId = peyTransactionId.Trim();
        if (peyTransactionId.Length > PeyIdMaxLength)
        {
            throw new ArgumentException($"El id de la transacción admite hasta {PeyIdMaxLength} caracteres.", nameof(peyTransactionId));
        }

        var tracking = string.IsNullOrWhiteSpace(trackingKey) ? null : trackingKey.Trim();
        if (tracking is { Length: > TrackingKeyMaxLength })
        {
            tracking = tracking[..TrackingKeyMaxLength];
        }

        return new Payment
        {
            MerchantId = merchantId,
            SaleId = saleId,
            PeyTransactionId = peyTransactionId,
            Amount = amount,
            PaidAt = paidAt,
            TrackingKey = tracking,
            PayerBankCode = payerBankCode,
            CreatedAt = now,
        };
    }
}
