using System.Globalization;
using System.Text.Json;
using Local133.Application.Admin;
using Local133.Application.Webhooks;
using Local133.Domain.Audit;
using Local133.Domain.Webhooks;
using Local133.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Admin;

/// <summary>
/// Reproceso de un webhook desde la bitácora del admin (6.4). La auditoría se guarda <b>después</b> de procesar, en otro
/// guardado: los manejadores (<c>PaymentRecorder</c>, <c>SubscriptionPaymentRecorder</c>…) hacen sus propias
/// transacciones, así que no caben en una sola. La entrada registra el resultado.
/// </summary>
internal sealed class AdminWebhookActions(
    AppDbContext db,
    IPeyWebhookProcessor processor,
    TimeProvider clock,
    ILogger<AdminWebhookActions> logger) : IAdminWebhookActions
{
    public async Task<AdminPlanResult> ReprocessAsync(
        int webhookEventId, string note, AdminActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(note))
        {
            return new AdminPlanResult.Rejected("Escribe el motivo.");
        }

        if (note.Trim().Length > AdminPlanLimits.NoteMaxLength)
        {
            return new AdminPlanResult.Rejected($"El motivo admite hasta {AdminPlanLimits.NoteMaxLength} caracteres.");
        }

        var result = await processor.ReprocessAsync(webhookEventId, cancellationToken);
        if (result is WebhookReprocessResult.NotFound)
        {
            return new AdminPlanResult.NotFound();
        }

        if (result is not WebhookReprocessResult.Done done)
        {
            return new AdminPlanResult.Rejected("Este webhook ya está procesado: no se reprocesa.");
        }

        var id = webhookEventId.ToString(CultureInfo.InvariantCulture);
        var summary = $"Reprocesó el webhook #{id} ({WebhookEventText.Type(done.EventType)}): "
            + $"{WebhookEventText.Status(done.Before)} → {WebhookEventText.Status(done.After)}.";
        db.ChangeTracker.Clear();
        db.AuditLogs.Add(AuditLog.Create(
            actor.UserId, actor.Email, AuditAction.WebhookReprocessed, merchantId: null, summary, note,
            JsonSerializer.Serialize(new
            {
                webhookEventId,
                webhookId = done.WebhookId,
                eventType = done.EventType,
                before = done.Before.ToString(),
                after = done.After.ToString(),
                result = done.Result.ToString(),
                reason = done.Reason,
                attempts = done.Attempts,
            }),
            clock.GetUtcNow(),
            webhookEventId));
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation("Admin {ActorUserId} reprocesó el webhook {WebhookId} ({EventType}): {Before} → {After}",
            actor.UserId, done.WebhookId, done.EventType, done.Before, done.After);

        return done.Result switch
        {
            WebhookHandlingResult.Processed => new AdminPlanResult.Done($"Listo: el webhook #{id} quedó procesado."),
            WebhookHandlingResult.Ignored => new AdminPlanResult.Done($"El webhook #{id} quedó ignorado: {done.Reason}"),
            WebhookHandlingResult.RetryLater => new AdminPlanResult.Rejected(
                $"El webhook #{id} falló por algo transitorio y sigue sin procesar. Intenta de nuevo en un momento."),
            _ => new AdminPlanResult.Rejected($"El webhook #{id} sigue fallando: {done.Reason}"),
        };
    }
}
