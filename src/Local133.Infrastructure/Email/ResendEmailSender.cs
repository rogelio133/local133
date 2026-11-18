using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resend;

namespace Local133.Infrastructure.Email;

/// <summary>
/// <see cref="IEmailSender"/> real: envía por la API de Resend el HTML que arma el sitio (p. ej. <c>AccountEmails</c>).
/// </summary>
/// <remarks>
/// <para>Cada llamada lleva su propia llave de idempotencia: los reintentos HTTP del pipeline repiten la misma
/// cabecera y Resend no duplica el correo.</para>
/// <para>Si Resend no acepta el correo (después de los reintentos) se registra el error y no se lanza: el registro o la
/// recuperación de contraseña no se bloquean por el envío (el usuario puede pedir otro).</para>
/// <para>Nunca se loguea el cuerpo: trae la liga de confirmación o de recuperación.</para>
/// </remarks>
public sealed class ResendEmailSender(
    IResend resend,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var message = new EmailMessage
        {
            From = options.Value.From,
            Subject = subject,
            HtmlBody = htmlMessage,
        };
        message.To.Add(email);

        var idempotencyKey = $"account-email/{Guid.NewGuid():N}";
        try
        {
            var response = await resend.EmailSendAsync(idempotencyKey, message);
            logger.LogInformation(
                "Correo enviado por Resend {EmailId} · {Subject} · dominio {RecipientDomain}",
                response.Content, subject, DomainOf(email));
        }
        catch (ResendException ex)
        {
            logger.LogError(
                ex,
                "Resend no aceptó el correo · {Subject} · dominio {RecipientDomain} · {StatusCode} {ErrorType}: {ResendMessage}",
                subject, DomainOf(email), (int?)ex.StatusCode, ex.ErrorType, ex.Message);
        }
    }

    private static string DomainOf(string email) =>
        email.LastIndexOf('@') is var at and >= 0 ? email[(at + 1)..] : "?";
}
