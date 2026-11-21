namespace Local133.Domain.Pey;

/// <summary>
/// Estatus de un link de pago (en el cable va en minúsculas: <c>pending</c>, <c>processing</c>…).
/// Lo devuelven <c>GET /payment-link/{id}</c> y el webhook <c>payment_link.status_updated</c> (pago de la suscripción, 5.6).
/// </summary>
public enum PeyPaymentLinkStatus
{
    Pending,
    Processing,
    Paid,
    Partial,
}
