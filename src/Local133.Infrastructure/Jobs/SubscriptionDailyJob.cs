using Local133.Application.Notifications;
using Local133.Application.Subscriptions;
using Local133.Domain.Audit;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Notifications;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Email;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Jobs;

/// <summary>
/// Job diario de la suscripción (Hangfire, 9:00 en México, 5.7): por cada vendedor con un vencimiento cerca, manda el
/// recordatorio que le toca según <see cref="SubscriptionReminders"/>; luego, durante 30 días desde el cambio de planes,
/// el anuncio de §10 (5.16); el día 1 manda el resumen mensual. <b>No crea links de Pey</b>: el correo lleva al checkout
/// y el link nace solo cuando el vendedor pulsa «Pagar» en el resumen (si no renueva, no se generó un link en vano). Es idempotente: el aviso es único por <c>DedupKey</c>. El vencimiento es un cálculo: el
/// job solo avisa, no cambia ningún estatus. Antes, de respaldo (5.13), empieza la prueba de los vendedores activos que
/// ya cobraron y no la tienen (el disparador de <c>PaymentRecorder</c> falló), con el <c>PaidAt</c> de su primer pago.
/// </summary>
public sealed class SubscriptionDailyJob(
    AppDbContext db,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<SubscriptionDailyJob> logger)
{
    public const string RecurringJobId = "suscripciones-diario";

    /// <summary>Todos los días a las 9:00 (el job se programa en hora de México).</summary>
    public const string Cron = "0 9 * * *";

    /// <summary>Tope de vendedores por corrida (los demás quedan para la siguiente; las ventanas duran varios días).</summary>
    public const int MaxMerchantsPerRun = 2000;

    /// <returns>Vendedores revisados y avisos creados.</returns>
    public async Task<SubscriptionDailyRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var trialsStarted = await StartMissingTrialsAsync(cancellationToken);
        var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);

        // Prefiltro en SQL: vence en los próximos días (aviso del anual) o venció hace poco (gracia, vencido y su
        // seguimiento del día 7). Las reglas finas van en SubscriptionReminders.
        var from = now.AddDays(-(settings.GraceDays + SubscriptionReminders.LastExpiredNoticeDay + 1));
        var to = now.AddDays(SubscriptionReminders.AnnualNoticeDays + 2);
        var merchantIds = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.ProUntil >= from && s.ProUntil <= to)
            .Join(db.Merchants.Where(m => m.Status != MerchantStatus.Disabled), s => s.MerchantId, m => m.Id, (s, m) => s.MerchantId)
            .OrderBy(id => id)
            .Take(MaxMerchantsPerRun)
            .ToListAsync(cancellationToken);

        var notified = 0;
        foreach (var merchantId in merchantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Un scope (y un DbContext) por vendedor: una falla no contamina a los siguientes.
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                notified += await RemindAsync(scope.ServiceProvider, merchantId, settings, now, cancellationToken) ? 1 : 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Suscripciones: error con el vendedor {MerchantId}; se sigue con el siguiente", merchantId);
            }
        }

        var announcements = await SendPlanChangeAnnouncementAsync(settings, now, cancellationToken);

        var summarySent = false;
        var today = MexicoTime.Today(now);
        if (today.Day == 1)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IMonthlySummaryNotifier>()
                    .SendAsync(MexicoTime.MonthStart(today.AddDays(-1)), cancellationToken);
                summarySent = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Suscripciones: falló el resumen mensual de {Month:yyyy-MM}", today.AddDays(-1));
            }
        }

        logger.LogInformation("Suscripciones: {Checked} vendedores revisados, {Notified} avisos, {TrialsStarted} pruebas de respaldo, {Announcements} anuncios",
            merchantIds.Count, notified, trialsStarted, announcements);
        return new SubscriptionDailyRunResult(merchantIds.Count, notified, summarySent, trialsStarted, announcements);
    }

    /// <summary>
    /// Respaldo de la prueba al primer cobro (5.13): vendedores <c>Active</c> con algún pago y sin suscripción. Un scope
    /// por vendedor; una falla no detiene a los demás.
    /// </summary>
    /// <returns>Pruebas que se empezaron.</returns>
    private async Task<int> StartMissingTrialsAsync(CancellationToken cancellationToken)
    {
        var missing = await db.Merchants.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.Status == MerchantStatus.Active
                && !db.Subscriptions.IgnoreQueryFilters().Any(s => s.MerchantId == m.Id))
            .Select(m => new
            {
                m.Id,
                FirstPaidAt = db.Payments.IgnoreQueryFilters().Where(p => p.MerchantId == m.Id).Min(p => (DateTimeOffset?)p.PaidAt),
            })
            .Where(m => m.FirstPaidAt != null)
            .OrderBy(m => m.Id)
            .Take(MaxMerchantsPerRun)
            .ToListAsync(cancellationToken);

        var started = 0;
        foreach (var merchant in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                var subscription = await scope.ServiceProvider.GetRequiredService<ITrialStarter>()
                    .StartAsync(merchant.Id, merchant.FirstPaidAt!.Value, cancellationToken);
                if (subscription is not null)
                {
                    started++;
                    logger.LogWarning("Suscripciones: la prueba del vendedor {MerchantId} la empezó el respaldo, desde su primer pago ({FirstPaidAt:o})",
                        merchant.Id, merchant.FirstPaidAt);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Suscripciones: no se pudo empezar la prueba del vendedor {MerchantId}; se sigue con el siguiente", merchant.Id);
            }
        }

        return started;
    }

    /// <summary>
    /// Anuncio del cambio de planes (§10, 5.16): a los vendedores registrados antes del corte (la hora del renglón
    /// <see cref="AuditAction.PlanChange"/> que dejó la migración), no dados de baja y sin el aviso, durante
    /// <see cref="PlanChangeAnnouncement.SendDays"/> días. Sin ese renglón no se manda nada. Un scope por vendedor.
    /// </summary>
    /// <returns>Anuncios nuevos.</returns>
    private async Task<int> SendPlanChangeAnnouncementAsync(SystemSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.PlanChange)
            .MinAsync(a => (DateTimeOffset?)a.OccurredAt, cancellationToken);
        if (cutoff is not { } cut || !PlanChangeAnnouncement.IsDue(cut, now))
        {
            return 0;
        }

        var merchantIds = await db.Merchants.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.CreatedAt < cut && m.Status != MerchantStatus.Disabled
                && !db.Notifications.IgnoreQueryFilters().Any(n => n.MerchantId == m.Id && n.DedupKey == PlanChangeAnnouncement.DedupKey))
            .OrderBy(m => m.Id)
            .Select(m => m.Id)
            .Take(MaxMerchantsPerRun)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var merchantId in merchantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                sent += await AnnounceAsync(scope.ServiceProvider, merchantId, settings, now, cancellationToken) ? 1 : 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Suscripciones: no se pudo mandar el anuncio de planes al vendedor {MerchantId}; se sigue con el siguiente", merchantId);
            }
        }

        return sent;
    }

    private static async Task<bool> AnnounceAsync(
        IServiceProvider services, int merchantId, SystemSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var scopedDb = services.GetRequiredService<AppDbContext>();
        var subscription = await scopedDb.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Include(s => s.Periods)
            .SingleOrDefaultAsync(s => s.MerchantId == merchantId, cancellationToken);
        var greetingName = await scopedDb.Merchants.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.Id == merchantId)
            .Select(m => Merchant.GreetingNameFor(m.OwnerName, m.BusinessName))
            .SingleAsync(cancellationToken);
        var situation = PlanChangeSituation.For(subscription, now, settings.GraceDays);
        var message = MerchantEmails.PlanChangeAnnouncement(greetingName, settings, subscription?.IsFounder ?? false, situation);
        var result = await services.GetRequiredService<INotificationService>().NotifyAsync(new NotificationRequest(
            merchantId, NotificationType.PlanChangeAnnouncement, message.Title, message.Body, message.Email.ActionPath,
            PlanChangeAnnouncement.DedupKey, message.Email), cancellationToken);
        return result is NotificationResult.Created;
    }

    /// <returns><c>true</c> si se creó un aviso nuevo.</returns>
    private async Task<bool> RemindAsync(
        IServiceProvider services, int merchantId, SystemSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var scopedDb = services.GetRequiredService<AppDbContext>();
        var subscription = await scopedDb.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Include(s => s.Periods)
            .SingleAsync(s => s.MerchantId == merchantId, cancellationToken);
        var status = subscription.StatusAt(now, settings.GraceDays);
        var reminder = SubscriptionReminders.For(
            status, subscription.LastPeriodKind, subscription.ChainEndPlan, subscription.LastPackage, now);
        if (reminder is null)
        {
            return false;
        }

        var greetingName = await scopedDb.Merchants.AsNoTracking()
            .Where(m => m.Id == merchantId)
            .Select(m => Merchant.GreetingNameFor(m.OwnerName, m.BusinessName))
            .SingleAsync(cancellationToken);
        // El botón va al checkout (§6): el resumen de su último pedido, la sección de su plan o el paso 1. Nunca a Pey.
        var actionPath = SubscriptionPaths.ChoosePlanFor(subscription.LastPlan, subscription.LastPackage);
        var message = MerchantEmails.SubscriptionReminder(reminder, greetingName, settings, subscription.IsFounder, actionPath);
        var result = await services.GetRequiredService<INotificationService>().NotifyAsync(new NotificationRequest(
            merchantId, reminder.NotificationType, message.Title, message.Body, actionPath,
            reminder.DedupKey, message.Email), cancellationToken);
        if (result is NotificationResult.Created)
        {
            logger.LogInformation("Suscripciones: recordatorio {Stage} ({Kind}) para el vendedor {MerchantId}",
                reminder.Stage, reminder.EndingKind, merchantId);
            return true;
        }

        return false;
    }
}

/// <param name="Checked">Vendedores con un vencimiento cerca que se revisaron.</param>
/// <param name="Notified">Avisos nuevos (los repetidos no cuentan).</param>
/// <param name="MonthlySummarySent">Se llamó al resumen mensual (día 1).</param>
/// <param name="TrialsStarted">Pruebas que empezó el respaldo (5.13): vendedores que cobraron y no la tenían.</param>
/// <param name="AnnouncementsSent">Anuncios nuevos del cambio de planes (5.16).</param>
public sealed record SubscriptionDailyRunResult(
    int Checked, int Notified, bool MonthlySummarySent, int TrialsStarted = 0, int AnnouncementsSent = 0);
