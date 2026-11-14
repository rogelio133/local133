namespace Local133.Application.Payments;

/// <summary>
/// Pago CoDi recibido en un QR, venga del webhook <c>codi.qr.payment_received</c> o de la conciliación (2.9).
/// </summary>
/// <param name="TransactionId">Id (ULID) de la transacción en Pey: llave de idempotencia.</param>
/// <param name="CodiQrId">IDC del QR pagado (con él se encuentra la venta).</param>
/// <param name="External">
/// <c>metadata.externalMetadata</c>: los pares que enviamos al crear el cobro (<c>SaleMetadata</c>), como texto.
/// </param>
public sealed record ReceivedCodiPayment(
    string TransactionId,
    string? CodiQrId,
    decimal Amount,
    DateTimeOffset PaidAt,
    string? TrackingKey,
    int? PayerBankCode,
    IReadOnlyDictionary<string, string?> External);

/// <summary>Registra pagos recibidos: crea el <c>Payment</c> (con la comisión vigente) y marca la venta pagada.</summary>
public interface IPaymentRecorder
{
    Task<PaymentRecordResult> RecordCodiQrPaymentAsync(ReceivedCodiPayment payment, CancellationToken cancellationToken = default);
}

public abstract record PaymentRecordResult
{
    private PaymentRecordResult()
    {
    }

    /// <summary>Pago nuevo registrado en la venta.</summary>
    public sealed record Recorded(int PaymentId, int SaleId, int MerchantId) : PaymentRecordResult;

    /// <summary>Esa transacción ya estaba registrada (otra entrega del mismo pago).</summary>
    public sealed record AlreadyRecorded(int SaleId) : PaymentRecordResult;

    /// <summary>Pago válido que no es de una venta (estado de cuenta, QR fijo): lo atiende otra fase.</summary>
    public sealed record Ignored(string Reason) : PaymentRecordResult;

    /// <summary>No se pudo asociar a una venta; requiere revisión.</summary>
    public sealed record Unmatched(string Reason) : PaymentRecordResult;
}
