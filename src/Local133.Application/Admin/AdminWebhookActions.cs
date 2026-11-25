namespace Local133.Application.Admin;

/// <summary>Acciones del admin sobre la bitácora de webhooks (6.4).</summary>
public interface IAdminWebhookActions
{
    /// <summary>
    /// Reprocesa un evento guardado (fallido, sin procesar o ignorado) con su body, sin llamar a Pey, y deja el
    /// resultado en <c>AuditLogs</c>. Un evento ya procesado se rechaza.
    /// </summary>
    Task<AdminPlanResult> ReprocessAsync(int webhookEventId, string note, AdminActor actor, CancellationToken cancellationToken = default);
}
