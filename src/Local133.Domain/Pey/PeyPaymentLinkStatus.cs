namespace Local133.Domain.Pey;

/// <summary>
/// Estatus de un link de pago (en el cable va en minúsculas: <c>pending</c>, <c>processing</c>…).
/// Reservado para cobros con tarjeta (fuera del MVP); el cliente de Pey aún no lo usa.
/// </summary>
public enum PeyPaymentLinkStatus
{
    Pending,
    Processing,
    Paid,
    Partial,
}
