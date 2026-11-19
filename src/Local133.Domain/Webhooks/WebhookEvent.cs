using Local133.Domain.Common;

namespace Local133.Domain.Webhooks;

/// <summary>Estatus del procesamiento de un webhook.</summary>
public enum WebhookEventStatus
{
    /// <summary>Guardado y aún sin procesar, o un intento falló por algo transitorio y espera el reintento de Pey.</summary>
    Received,

    /// <summary>Procesado.</summary>
    Processed,

    /// <summary>Válido pero sin acción (tipo sin manejador todavía, pago del QR fijo…).</summary>
    Ignored,

    /// <summary>No se pudo procesar (venta no encontrada, payload inválido); requiere revisión.</summary>
    Failed,
}

/// <summary>
/// Bitácora de webhooks de Pey: uno por <c>webhook-id</c> (Standard Webhooks). Guarda el body crudo para revisarlo o
/// reprocesarlo. No pertenece a un vendedor: solo lo ve el admin.
/// </summary>
public class WebhookEvent : Entity
{
    public const int WebhookIdMaxLength = 100;
    public const int EventTypeMaxLength = 64;
    public const int ErrorMaxLength = 2000;

    public string WebhookId { get; set; } = null!;

    /// <summary>Campo <c>type</c> del payload (vacío si no se pudo leer).</summary>
    public string EventType { get; set; } = "";

    /// <summary>Body tal como llegó (el que se firmó).</summary>
    public string Payload { get; set; } = null!;

    /// <summary>Header <c>webhook-timestamp</c>.</summary>
    public DateTimeOffset WebhookTimestamp { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public WebhookEventStatus Status { get; set; } = WebhookEventStatus.Received;

    /// <summary>Veces que se intentó procesar (cada entrega de Pey con este id).</summary>
    public int Attempts { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Motivo de <see cref="WebhookEventStatus.Ignored"/> o <see cref="WebhookEventStatus.Failed"/>, o el último error transitorio.</summary>
    public string? Error { get; set; }

    /// <summary>Ya terminó: otra entrega con el mismo id no se vuelve a procesar.</summary>
    public bool IsSettled => Status is WebhookEventStatus.Processed or WebhookEventStatus.Ignored;

    public static WebhookEvent Receive(
        string webhookId, string? eventType, string payload, DateTimeOffset webhookTimestamp, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookId);
        ArgumentNullException.ThrowIfNull(payload);
        if (webhookId.Length > WebhookIdMaxLength)
        {
            throw new ArgumentException($"El webhook-id admite hasta {WebhookIdMaxLength} caracteres.", nameof(webhookId));
        }

        return new WebhookEvent
        {
            WebhookId = webhookId,
            EventType = Truncate(eventType, EventTypeMaxLength) ?? "",
            Payload = payload,
            WebhookTimestamp = webhookTimestamp,
            ReceivedAt = now,
            Status = WebhookEventStatus.Received,
        };
    }

    /// <summary>Empieza un intento de procesamiento (una entrega de Pey).</summary>
    public void StartAttempt() => Attempts++;

    public void MarkProcessed(DateTimeOffset now) => Settle(WebhookEventStatus.Processed, null, now);

    public void MarkIgnored(string reason, DateTimeOffset now) => Settle(WebhookEventStatus.Ignored, reason, now);

    public void MarkFailed(string error, DateTimeOffset now) => Settle(WebhookEventStatus.Failed, error, now);

    /// <summary>Falla transitoria: queda en <see cref="WebhookEventStatus.Received"/> para que el reintento lo procese.</summary>
    public void MarkRetryable(string error)
    {
        Status = WebhookEventStatus.Received;
        Error = Truncate(error, ErrorMaxLength);
    }

    private void Settle(WebhookEventStatus status, string? error, DateTimeOffset now)
    {
        Status = status;
        Error = Truncate(error, ErrorMaxLength);
        ProcessedAt = now;
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
