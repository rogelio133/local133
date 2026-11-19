using Local133.Application.BankAccounts;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Infrastructure.Email;
using Local133.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Local133.Infrastructure.BankAccounts;

/// <summary>
/// Correo «Tu cuenta ya está activa» (<see cref="MerchantEmails.BankAccountActiveBody"/>). Un solo correo por vendedor:
/// el candado es <see cref="Merchant.ActivationEmailSentAt"/>, que se toma con un <c>UPDATE</c> condicionado antes de
/// enviar. Sin filtro multi-tenant: lo llaman el webhook y el job, que corren sin vendedor en sesión.
/// </summary>
internal sealed class BankAccountActivationEmailNotifier(
    AppDbContext db,
    IEmailSender emailSender,
    IOptions<SiteUrlOptions> siteOptions,
    IHostEnvironment environment,
    TimeProvider clock,
    ILogger<BankAccountActivationEmailNotifier> logger) : IBankAccountActivationNotifier
{
    public async Task NotifyActivatedAsync(int merchantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var baseUrl = siteOptions.Value.BaseUrl(environment.IsDevelopment());
            if (baseUrl is null)
            {
                logger.LogError("No se avisó al vendedor {MerchantId} que su cuenta quedó activa: falta App:PublicBaseUrl para armar la liga",
                    merchantId);
                return;
            }

            var claimed = await db.Merchants.IgnoreQueryFilters()
                .Where(m => m.Id == merchantId && m.Status == MerchantStatus.Active && m.ActivationEmailSentAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ActivationEmailSentAt, clock.GetUtcNow()), cancellationToken);
            if (claimed == 0)
            {
                return; // ya se avisó (otro camino vio la misma activación) o el vendedor no está activo
            }

            var recipient = await db.Merchants.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.Id == merchantId)
                .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.BusinessName, u.Email })
                .SingleAsync(cancellationToken);
            var clabe = await db.BankAccounts.IgnoreQueryFilters().AsNoTracking()
                .Where(b => b.MerchantId == merchantId)
                .Select(b => b.Clabe)
                .SingleAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(recipient.Email))
            {
                logger.LogWarning("El vendedor {MerchantId} no tiene correo: no se le avisó que su cuenta quedó activa", merchantId);
                return;
            }

            await emailSender.SendEmailAsync(
                recipient.Email,
                MerchantEmails.BankAccountActiveSubject,
                MerchantEmails.BankAccountActiveBody(recipient.BusinessName, Clabe.Mask(clabe), baseUrl));
            logger.LogInformation("Se avisó al vendedor {MerchantId} que su cuenta {Clabe} quedó activa", merchantId, Clabe.Mask(clabe));
        }
        catch (Exception ex)
        {
            // El cambio de estatus ya se guardó: un error aquí no debe tumbar el webhook (Pey reintentaría sin efecto) ni el job.
            logger.LogError(ex, "No se pudo avisar al vendedor {MerchantId} que su cuenta quedó activa", merchantId);
        }
    }
}
