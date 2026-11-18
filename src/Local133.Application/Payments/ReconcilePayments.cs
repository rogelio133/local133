namespace Local133.Application.Payments;

/// <summary>
/// Conciliación de pagos (respaldo del webhook): le pregunta a Pey si una venta sin pagar recibió pagos y registra
/// los que falten con <see cref="IPaymentRecorder"/> (idempotente por <c>transactionId</c>). QR:
/// <c>GET /transaction/codi/qr/{idc}</c>; cobro por celular: <c>GET /transaction/{id}</c>. No depende de la sesión:
/// la usa el job <c>conciliar-pagos</c>.
/// </summary>
public interface IPaymentReconciliation
{
    Task<PaymentReconciliationResult> ReconcileSaleAsync(int saleId, CancellationToken cancellationToken = default);
}

public abstract record PaymentReconciliationResult
{
    private PaymentReconciliationResult()
    {
    }

    /// <summary>Se consultó a Pey y se registró la consulta (<c>Sales.PaymentCheckedAt</c>).</summary>
    /// <param name="Recorded">Pagos nuevos registrados.</param>
    /// <param name="AlreadyRecorded">Pagos que ya estaban (llegó el webhook).</param>
    /// <param name="Unmatched">Pagos que no se pudieron asociar a la venta (requieren revisión).</param>
    public sealed record Checked(int Recorded, int AlreadyRecorded, int Unmatched) : PaymentReconciliationResult;

    /// <summary>La venta no existe, ya está pagada o no tiene a quién preguntar (sin QR ni transacción).</summary>
    public sealed record SaleNotEligible(string Reason) : PaymentReconciliationResult;

    /// <summary>Pey no respondió o respondió con error: no se registra la consulta y se reintenta después.</summary>
    public sealed record GatewayFailed(string Message) : PaymentReconciliationResult;
}
