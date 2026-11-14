using Local133.Domain.Common;

namespace Local133.Domain.Payments;

/// <summary>
/// Pago recibido por CoDi (una transacción de Pey). Una venta puede tener 0..N pagos. Guarda la tasa y el monto de la
/// comisión del momento del cobro; el estado de cuenta semanal (Fase 5) los agrupa.
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

    /// <summary>Tasa de comisión aplicada (0.03 = 3 %), copiada al cobrarse.</summary>
    public decimal CommissionRate { get; set; }

    /// <summary>Comisión en MXN, sin IVA.</summary>
    public decimal CommissionAmount { get; set; }

    /// <summary>Estado de cuenta que la cobró (Fase 5); <c>null</c> = aún sin facturar.</summary>
    public int? StatementId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Registra el pago con la comisión calculada a la tasa vigente (<see cref="Commission.EffectiveRate"/>).</summary>
    public static Payment Record(
        int merchantId, int saleId, string peyTransactionId, decimal amount, DateTimeOffset paidAt,
        string? trackingKey, int? payerBankCode, decimal commissionRate, DateTimeOffset now)
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
            CommissionRate = commissionRate,
            CommissionAmount = Commission.Calculate(amount, commissionRate),
            CreatedAt = now,
        };
    }
}
