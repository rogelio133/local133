namespace Local133.Domain.Pey;

/// <summary>Recurso que se pagó con la transacción (<c>payableType</c>).</summary>
public enum PeyPayableType
{
    /// <summary><c>PAYMENT_LINK</c>: link de pago (pago de la suscripción, 5.6; cae en la cuenta de la plataforma).</summary>
    PaymentLink,

    /// <summary><c>API_CODI_QR</c>: QR CoDi creado con <c>POST /codi/qr</c>.</summary>
    ApiCodiQr,

    /// <summary><c>API_CODI_PN</c>: notificación push CoDi creada con <c>POST /codi/pn</c>.</summary>
    ApiCodiPn,
}
