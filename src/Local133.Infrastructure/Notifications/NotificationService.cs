using Local133.Application.Notifications;
using Local133.Domain.Notifications;
using Local133.Infrastructure.Email;
using Local133.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Local133.Infrastructure.Notifications;

/// <summary>
/// Guarda el aviso (único por vendedor y <c>DedupKey</c>) y, si lleva correo, lo manda después de guardarlo. El correo
/// sale a lo más una vez: si falla, queda en el log con <c>EmailSentAt</c> nulo y no se reintenta (el aviso ya existe).
/// Nunca loguea el cuerpo ni el destinatario completo.
/// </summary>
internal sealed class NotificationService(
    AppDbContext db,
    IEmailSender emailSender,
    IOptions<SiteUrlOptions> siteOptions,
    IHostEnvironment environment,
    TimeProvider clock,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task<NotificationResult> NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var exists = await db.Notifications.IgnoreQueryFilters()
            .AnyAsync(n => n.MerchantId == request.MerchantId && n.DedupKey == request.DedupKey, cancellationToken);
        if (exists)
        {
            return new NotificationResult.AlreadyExists();
        }

        var notification = Notification.Create(
            request.MerchantId, request.Type, request.Title, request.Body, request.Url, request.DedupKey, clock.GetUtcNow());
        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Otra corrida lo guardó entre la búsqueda y el insert.
            db.Entry(notification).State = EntityState.Detached;
            return new NotificationResult.AlreadyExists();
        }

        logger.LogInformation("Aviso {NotificationId} ({Type}) para el vendedor {MerchantId}", notification.Id, notification.Type, notification.MerchantId);
        var emailSent = request.Email is { } email && await SendEmailAsync(notification, email);
        return new NotificationResult.Created(notification.Id, emailSent);
    }

    private async Task<bool> SendEmailAsync(Notification notification, NotificationEmail email)
    {
        try
        {
            var baseUrl = siteOptions.Value.BaseUrl(environment.IsDevelopment());
            if (baseUrl is null)
            {
                logger.LogError("Sin correo del aviso {NotificationId} ({Type}) del vendedor {MerchantId}: falta App:PublicBaseUrl para armar la liga",
                    notification.Id, notification.Type, notification.MerchantId);
                return false;
            }

            var recipient = await db.Merchants.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.Id == notification.MerchantId)
                .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => u.Email)
                .SingleOrDefaultAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(recipient))
            {
                logger.LogWarning("El vendedor {MerchantId} no tiene correo: el aviso {NotificationId} solo queda en el sitio",
                    notification.MerchantId, notification.Id);
                return false;
            }

            var html = EmailLayout.Render(new EmailContent(
                email.Subject, email.Preheader, email.Title, email.Paragraphs, email.ActionText,
                baseUrl + email.ActionPath, email.Footer), baseUrl);
            await emailSender.SendEmailAsync(recipient, email.Subject, html);

            notification.EmailSentAt = clock.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogInformation("Correo del aviso {NotificationId} ({Type}) enviado a un buzón de {Domain}",
                notification.Id, notification.Type, DomainOf(recipient));
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo mandar el correo del aviso {NotificationId} ({Type}) del vendedor {MerchantId}",
                notification.Id, notification.Type, notification.MerchantId);
            return false;
        }
    }

    private static string DomainOf(string email) => email[(email.LastIndexOf('@') + 1)..];
}
