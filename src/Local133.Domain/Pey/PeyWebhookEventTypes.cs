namespace Local133.Domain.Pey;

/// <summary>Valores del campo <c>type</c> de los webhooks de Pey.</summary>
public static class PeyWebhookEventTypes
{
    /// <summary>Se pagó un QR CoDi (match por <c>codiQrId</c>).</summary>
    public const string CodiQrPaymentReceived = "codi.qr.payment_received";

    /// <summary>Se pagó una notificación push CoDi (match por <c>transactionId</c>).</summary>
    public const string CodiPnPaymentReceived = "codi.pn.payment_received";

    /// <summary>Una cuenta bancaria pasó a <c>ACTIVE</c> (solo se envía en esa transición).</summary>
    public const string BankAccountUpdated = "bank.account.updated";

    /// <summary>Cambio de estatus de un link de pago (fuera del MVP).</summary>
    public const string PaymentLinkStatusUpdated = "payment_link.status_updated";
}
