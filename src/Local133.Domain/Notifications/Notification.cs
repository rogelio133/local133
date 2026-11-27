using Local133.Domain.Common;

namespace Local133.Domain.Notifications;

/// <summary>Tipo de aviso en el sitio. La 5.7 trae los de la suscripción; la 5.4 agrega los suyos.</summary>
public enum NotificationType
{
    /// <summary>El plan pagado vence pronto (15 días antes en el anual o 3 días antes).</summary>
    SubscriptionUpcoming,

    /// <summary>La prueba termina pronto.</summary>
    TrialEnding,

    /// <summary>
    /// Terminan los días regalados (premio de referido o regalo del admin) o los de transición, de Básico o de Pro.
    /// Antes de la 5.16 se llamaba <c>ProDaysEnding</c> (la migración <c>PlanChangeAnnouncement</c> renombró los renglones).
    /// </summary>
    PlanDaysEnding,

    /// <summary>El plan pagado vence hoy.</summary>
    SubscriptionDueToday,

    /// <summary>Día 3 de la gracia: quedan pocos días para pagar sin perder el plan.</summary>
    SubscriptionGrace,

    /// <summary>
    /// El plan venció: ya no genera cobros (5.16). Antes se llamaba <c>DowngradedToFree</c> (la migración
    /// <c>PlanChangeAnnouncement</c> renombró los renglones).
    /// </summary>
    PlanExpired,

    /// <summary>Seguimiento del vencido, el día 7 (5.16).</summary>
    PlanExpiredFollowUp,

    /// <summary>Anuncio del cambio de planes 2026-12: Básico y Pro, sin gratuito (5.16).</summary>
    PlanChangeAnnouncement,

    /// <summary>Un referido pagó: ganó días de Pro (5.8).</summary>
    ReferralRewardGranted,

    /// <summary>Un referido pagó; sus días llegan cuando empiece su prueba (5.8).</summary>
    ReferralRewardPending,

    /// <summary>Un referido pagó, pero no hubo premio (tope o dado de baja, 5.8).</summary>
    ReferralRewardSkipped,

    /// <summary>Resumen del mes que terminó: lo cobrado y la comisión que se ahorró (5.10).</summary>
    MonthlySummary,

    /// <summary>El admin le regaló días de Pro (6.2).</summary>
    ProDaysGranted,
}

/// <summary>
/// Aviso al vendedor en el sitio (5.4 y 5.7). <see cref="DedupKey"/> es único por vendedor: un job o un webhook que se
/// repite no duplica el aviso ni su correo.
/// </summary>
public class Notification : Entity
{
    public const int TitleMaxLength = 120;
    public const int BodyMaxLength = 500;
    public const int UrlMaxLength = 300;
    public const int DedupKeyMaxLength = 150;

    public int MerchantId { get; set; }

    public NotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Ruta del sitio a la que lleva el aviso (relativa, p. ej. <c>/app/plan</c>).</summary>
    public string? Url { get; set; }

    public string DedupKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>Cuándo salió su correo; <c>null</c> si no lleva correo o si el envío falló.</summary>
    public DateTimeOffset? EmailSentAt { get; set; }

    public static Notification Create(
        int merchantId, NotificationType type, string title, string body, string? url, string dedupKey, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(merchantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupKey);
        if (dedupKey.Length > DedupKeyMaxLength)
        {
            throw new ArgumentException($"La llave del aviso pasa de {DedupKeyMaxLength} caracteres.", nameof(dedupKey));
        }

        if (url is not null && (!url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal) || url.Length > UrlMaxLength))
        {
            throw new ArgumentException("La liga del aviso debe ser una ruta del sitio.", nameof(url));
        }

        return new Notification
        {
            MerchantId = merchantId,
            Type = type,
            Title = Truncate(title.Trim(), TitleMaxLength),
            Body = Truncate(body.Trim(), BodyMaxLength),
            Url = url,
            DedupKey = dedupKey,
            CreatedAt = now,
        };
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
