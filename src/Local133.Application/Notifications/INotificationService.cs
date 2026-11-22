using Local133.Domain.Notifications;

namespace Local133.Application.Notifications;

/// <summary>
/// Avisos al vendedor en el sitio, con correo opcional (base de la 5.4, adelantada en la 5.7). Idempotente por
/// <see cref="NotificationRequest.DedupKey"/> (único por vendedor): repetir la llamada no duplica el aviso ni el correo.
/// No usa la sesión (jobs y webhooks). El correo sale a lo más una vez: si falla, se loguea y no se reintenta.
/// </summary>
public interface INotificationService
{
    Task<NotificationResult> NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}

/// <param name="Url">Ruta del sitio a la que lleva el aviso (p. ej. <c>/app/plan</c>).</param>
/// <param name="Email">Correo que acompaña al aviso; <c>null</c> si solo va en el sitio.</param>
public sealed record NotificationRequest(
    int MerchantId,
    NotificationType Type,
    string Title,
    string Body,
    string? Url,
    string DedupKey,
    NotificationEmail? Email = null);

/// <summary>Correo del aviso, sin HTML: el servicio lo arma con el layout de la marca y la base pública del sitio.</summary>
/// <param name="ActionPath">Ruta del botón (relativa al sitio); se vuelve absoluta con <c>App:PublicBaseUrl</c>.</param>
public sealed record NotificationEmail(
    string Subject,
    string Preheader,
    string Title,
    IReadOnlyList<string> Paragraphs,
    string ActionText,
    string ActionPath,
    string? Footer);

public abstract record NotificationResult
{
    private NotificationResult()
    {
    }

    /// <param name="EmailSent">El correo salió (falso si no llevaba, si falló o si no hay base para las ligas).</param>
    public sealed record Created(int NotificationId, bool EmailSent) : NotificationResult;

    /// <summary>Ya existía un aviso con esa llave para el vendedor: no se hizo nada.</summary>
    public sealed record AlreadyExists : NotificationResult;
}
