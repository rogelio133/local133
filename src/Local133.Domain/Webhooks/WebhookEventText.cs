using Local133.Domain.Pey;

namespace Local133.Domain.Webhooks;

/// <summary>Textos de la bitácora de webhooks (admin y su auditoría, 6.4).</summary>
public static class WebhookEventText
{
    public static string Type(string? eventType) => eventType switch
    {
        PeyWebhookEventTypes.CodiQrPaymentReceived => "Pago con QR",
        PeyWebhookEventTypes.CodiPnPaymentReceived => "Cobro por celular",
        PeyWebhookEventTypes.BankAccountUpdated => "Cuenta bancaria",
        PeyWebhookEventTypes.PaymentLinkStatusUpdated => "Pago de suscripción",
        null or "" => "Sin tipo",
        _ => eventType,
    };

    public static string Status(WebhookEventStatus status) => status switch
    {
        WebhookEventStatus.Received => "Sin procesar",
        WebhookEventStatus.Processed => "Procesado",
        WebhookEventStatus.Ignored => "Ignorado",
        WebhookEventStatus.Failed => "Fallido",
        _ => status.ToString(),
    };
}
