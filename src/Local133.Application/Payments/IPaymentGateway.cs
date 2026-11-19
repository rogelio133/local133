namespace Local133.Application.Payments;

/// <summary>
/// Pasarela de cobro CoDi (hoy Pey). Una sola API key de la plataforma: cada cobro elige la cuenta destino
/// con <c>BankAccountId</c>; sin él, el dinero cae en la cuenta de la plataforma (estados de cuenta).
/// </summary>
/// <remarks>
/// Errores: cualquier respuesta no exitosa se lanza como un <see cref="PaymentGatewayException"/> (ver sus
/// subclases). Argumentos fuera de las reglas de Pey ⇒ <see cref="ArgumentException"/> sin llamar a Pey.
/// Las altas (<c>Register…</c>, <c>Create…</c>) no son idempotentes en Pey: solo se reintentan si Pey
/// responde 503/429; ante un timeout o un 500 el resultado es incierto y no se repite la petición.
/// </remarks>
public interface IPaymentGateway
{
    /// <summary><c>POST /bank-account</c>: registra la CLABE; nace en <c>PENDING</c> (hasta 5 días hábiles).</summary>
    Task<RegisteredBankAccount> RegisterBankAccountAsync(RegisterBankAccountRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>GET /bank-account/{id}</c>: estatus CoDi y, si falló, <c>errorMessage</c>.</summary>
    Task<GatewayBankAccount> GetBankAccountAsync(string bankAccountId, CancellationToken cancellationToken = default);

    /// <summary><c>POST /codi/qr</c>: crea un QR CoDi (dinámico con monto, o fijo con monto 0 y sin expiración).</summary>
    Task<CodiQr> CreateCodiQrAsync(CreateCodiQrRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>POST /codi/pn</c>: envía una solicitud de cobro al celular del pagador.</summary>
    Task<CodiPushNotification> CreateCodiPushNotificationAsync(CreateCodiPushNotificationRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>GET /transaction/{id}</c>.</summary>
    Task<GatewayTransaction> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary><c>GET /transaction/api-key</c>: todas las transacciones de la API key, 80 por página, recientes primero.</summary>
    Task<GatewayTransactionPage> GetApiKeyTransactionsAsync(string? nextToken = null, CancellationToken cancellationToken = default);

    /// <summary><c>GET /transaction/bank-account/{id}</c>: transacciones pagadas de una cuenta, 80 por página.</summary>
    Task<GatewayTransactionPage> GetBankAccountTransactionsAsync(string bankAccountId, string? nextToken = null, CancellationToken cancellationToken = default);

    /// <summary><c>GET /transaction/codi/qr/{idc}</c>: pagos de un QR, 20 por página (conciliación).</summary>
    Task<GatewayTransactionPage> GetCodiQrTransactionsAsync(string codiQrId, string? nextToken = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /payment-link/create</c>: crea una página de pago hospedada por Pey. No lleva <c>bankAccountId</c>:
    /// el dinero cae en la cuenta ligada a la API key (la de la plataforma).
    /// </summary>
    Task<CreatedPaymentLink> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>GET /payment-link/{id}</c>: estatus, medios de pago y pagos del link.</summary>
    Task<GatewayPaymentLink> GetPaymentLinkAsync(string paymentLinkId, CancellationToken cancellationToken = default);
}
