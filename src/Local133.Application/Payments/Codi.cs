namespace Local133.Application.Payments;

/// <summary>Creación de un QR CoDi.</summary>
/// <param name="Amount">Monto en MXN con hasta 2 decimales; <c>0</c> = el pagador escribe el monto (QR fijo).</param>
/// <param name="ExpirationSeconds"><c>0</c> = no expira; si no, ≥ 360.</param>
/// <param name="Concept">Concepto que ve el pagador.</param>
/// <param name="NumRef">Referencia numérica de hasta 7 dígitos (folio corto de la venta).</param>
/// <param name="BankAccountId">Cuenta del vendedor (debe estar <c>ACTIVE</c>); <c>null</c> = cuenta de la plataforma.</param>
/// <param name="Metadata">Hasta 50 pares (llave ≤ 40, valor ≤ 500); vuelven en <c>metadata.externalMetadata</c>.</param>
public sealed record CreateCodiQrRequest(
    decimal Amount,
    int ExpirationSeconds,
    string? Concept = null,
    int? NumRef = null,
    string? BankAccountId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>QR CoDi creado.</summary>
/// <param name="Id">IDC asignado por Banxico (con él se consultan sus pagos).</param>
/// <param name="QrString">Contenido a codificar en la imagen del QR.</param>
public sealed record CodiQr(string Id, string QrString);

/// <summary>Cobro por notificación push al celular del pagador.</summary>
/// <param name="Amount">Monto en MXN, mayor a 0 y hasta 1,000,000, con hasta 2 decimales.</param>
/// <param name="PhoneNumber">Celular de 10 dígitos.</param>
/// <param name="ExpirationSeconds">≥ 360.</param>
public sealed record CreateCodiPushNotificationRequest(
    decimal Amount,
    string PhoneNumber,
    int ExpirationSeconds = Domain.Pey.PeyLimits.MinExpirationSeconds,
    string? Concept = null,
    int? NumRef = null,
    string? BankAccountId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>Notificación push enviada.</summary>
/// <param name="TransactionId">Id (ULID) de la transacción: llega igual en el webhook <c>codi.pn.payment_received</c>.</param>
/// <param name="CodiId">Folio CoDi de Banxico (20 dígitos).</param>
public sealed record CodiPushNotification(string TransactionId, string CodiId);
