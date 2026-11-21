using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Infrastructure.Email;
using Local133.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>
/// Correo «Recibimos tu pago» (<see cref="MerchantEmails.SubscriptionPaidBody"/>) al abonarse un cargo. Uno por cargo:
/// el abono solo ocurre una vez. Nunca lanza: el pago ya quedó guardado. La 5.4 suma el aviso en el sitio.
/// </summary>
internal sealed class SubscriptionPaymentEmailNotifier(
    AppDbContext db,
    IEmailSender emailSender,
    IOptions<SiteUrlOptions> siteOptions,
    IHostEnvironment environment,
    ILogger<SubscriptionPaymentEmailNotifier> logger) : ISubscriptionPaymentNotifier
{
    public async Task PaymentCreditedAsync(SubscriptionPaymentCredited payment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payment);
        try
        {
            var baseUrl = siteOptions.Value.BaseUrl(environment.IsDevelopment());
            if (baseUrl is null)
            {
                logger.LogError("No se avisó al vendedor {MerchantId} del pago del cargo {ChargeId}: falta App:PublicBaseUrl para armar la liga",
                    payment.MerchantId, payment.ChargeId);
                return;
            }

            var recipient = await db.Merchants.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.Id == payment.MerchantId)
                .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.BusinessName, u.Email })
                .SingleAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(recipient.Email))
            {
                logger.LogWarning("El vendedor {MerchantId} no tiene correo: no se le avisó del pago del cargo {ChargeId}",
                    payment.MerchantId, payment.ChargeId);
                return;
            }

            await emailSender.SendEmailAsync(
                recipient.Email,
                MerchantEmails.SubscriptionPaidSubject,
                MerchantEmails.SubscriptionPaidBody(
                    recipient.BusinessName,
                    payment.Package,
                    payment.Amount,
                    MexicoTime.DateOf(payment.ProUntil.AddTicks(-1)),
                    payment.ExtendedExistingPro,
                    payment.FounderNumber,
                    baseUrl));
            logger.LogInformation("Se avisó al vendedor {MerchantId} del pago del cargo {ChargeId}", payment.MerchantId, payment.ChargeId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo avisar al vendedor {MerchantId} del pago del cargo {ChargeId}", payment.MerchantId, payment.ChargeId);
        }
    }
}
