namespace Local133.Application.Webhooks;

/// <summary>
/// Procesa un webhook de Pey con la firma ya verificada: lo guarda en <c>WebhookEvents</c> (único por
/// <c>webhook-id</c>) y lo despacha por <c>type</c> en la misma petición.
/// </summary>
public interface IPeyWebhookProcessor
{
    /// <param name="webhookId">Header <c>webhook-id</c> (Pey lo repite en cada reintento de la misma entrega).</param>
    /// <param name="timestamp">Header <c>webhook-timestamp</c>.</param>
    /// <param name="body">Body crudo (el que se firmó).</param>
    Task<WebhookHandlingResult> HandleAsync(string webhookId, DateTimeOffset timestamp, string body, CancellationToken cancellationToken = default);
}

/// <summary>Qué pasó con el webhook; el endpoint lo traduce a código HTTP.</summary>
public enum WebhookHandlingResult
{
    /// <summary>Procesado ⇒ 200.</summary>
    Processed,

    /// <summary>Ese <c>webhook-id</c> ya se procesó ⇒ 200 sin hacer nada.</summary>
    Duplicate,

    /// <summary>Válido pero sin acción (tipo sin manejador, pago de otro tipo) ⇒ 200.</summary>
    Ignored,

    /// <summary>Error permanente (payload inválido, venta no encontrada) ⇒ 200: reintentar no lo arregla. Queda para revisión.</summary>
    Failed,

    /// <summary>Falla transitoria (base de datos) ⇒ 500: Pey reintenta con el mismo id y se vuelve a procesar.</summary>
    RetryLater,
}
