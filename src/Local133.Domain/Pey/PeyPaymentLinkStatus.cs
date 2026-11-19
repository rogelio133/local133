namespace Local133.Domain.Pey;

/// <summary>
/// Estatus de un link de pago (en el cable va en minúsculas: <c>pending</c>, <c>processing</c>…).
/// Lo devuelve <c>GET /payment-link/{id}</c> (links de pago con tarjeta; hoy solo los usa la prueba de concepto).
/// </summary>
public enum PeyPaymentLinkStatus
{
    Pending,
    Processing,
    Paid,
    Partial,
}
