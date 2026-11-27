using Local133.Domain.Common;

namespace Local133.Domain.Audit;

/// <summary>Qué hizo el admin (6.2).</summary>
public enum AuditAction
{
    /// <summary>Cambió los parámetros globales (<c>SystemSettings</c>).</summary>
    SettingsUpdated,

    /// <summary>Regaló días de Pro a un vendedor.</summary>
    ProDaysGranted,

    /// <summary>Abonó a mano un cargo de la suscripción.</summary>
    ChargeCreditedManually,

    /// <summary>Anuló el premio de un referido (los días que no se habían usado).</summary>
    ReferralRewardRevoked,

    /// <summary>Dio los premios de referidos que se quedaron pendientes.</summary>
    PendingReferralRewardsGranted,

    /// <summary>Reprocesó un webhook guardado en la bitácora (6.4).</summary>
    WebhookReprocessed,

    /// <summary>La migración del cambio de planes (2026-12) le dio días de Básico de transición (actor «sistema»).</summary>
    PlanTransition,
}

/// <summary>
/// Bitácora de cambios del admin: precios, parámetros, acciones sobre el plan de un vendedor y reprocesos de webhooks. No pertenece a un
/// vendedor (sin filtro multi-tenant): solo la ve el admin. Se guarda en la misma transacción que el cambio.
/// </summary>
public class AuditLog : Entity
{
    public const int ActorUserIdMaxLength = 450;
    public const int ActorEmailMaxLength = 256;
    public const int SummaryMaxLength = 500;
    public const int NoteMaxLength = 500;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Usuario de Identity del admin.</summary>
    public string ActorUserId { get; set; } = string.Empty;

    /// <summary>Correo del admin al momento del cambio.</summary>
    public string ActorEmail { get; set; } = string.Empty;

    public AuditAction Action { get; set; }

    /// <summary>Vendedor afectado; <c>null</c> en los cambios globales.</summary>
    public int? MerchantId { get; set; }

    /// <summary>Texto legible: «Regaló 10 días de Pro (hasta el 12 dic 2026)».</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Motivo que escribió el admin.</summary>
    public string? Note { get; set; }

    /// <summary>JSON con el detalle (cambios antes/después, fechas, ids).</summary>
    public string? Details { get; set; }

    /// <summary>Webhook reprocesado (<see cref="AuditAction.WebhookReprocessed"/>); <c>null</c> en lo demás.</summary>
    public int? WebhookEventId { get; set; }

    public static AuditLog Create(
        string actorUserId, string actorEmail, AuditAction action, int? merchantId, string summary, string? note,
        string? details, DateTimeOffset now, int? webhookEventId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > NoteMaxLength })
        {
            throw new ArgumentException($"El motivo admite hasta {NoteMaxLength} caracteres.", nameof(note));
        }

        return new AuditLog
        {
            OccurredAt = now,
            ActorUserId = Truncate(actorUserId.Trim(), ActorUserIdMaxLength),
            ActorEmail = Truncate(actorEmail?.Trim() ?? string.Empty, ActorEmailMaxLength),
            Action = action,
            MerchantId = merchantId,
            Summary = Truncate(summary.Trim(), SummaryMaxLength),
            Note = trimmedNote,
            Details = details,
            WebhookEventId = webhookEventId,
        };
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
