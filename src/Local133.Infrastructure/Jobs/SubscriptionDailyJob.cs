using Local133.Application.Notifications;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
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
/// recordatorio que le toca según <see cref="SubscriptionReminders"/>; el día 1 manda el resumen mensual. <b>No crea
/// links de Pey</b>: el correo lleva a «Mi plan» y el link nace cuando el vendedor elige un paquete (si no renueva, no
/// se generó un link en vano). Es idempotente: el aviso es único por <c>DedupKey</c>. La baja al gratuito es un
/// cálculo: el job solo avisa, no cambia ningún estatus.
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
        var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);

        // Prefiltro en SQL: vence en los próximos días (aviso del anual) o venció hace poco (gracia y baja). Las reglas
        // finas van en SubscriptionReminders.
        var from = now.AddDays(-(settings.GraceDays + SubscriptionReminders.DowngradeNoticeWindowDays + 1));
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

        logger.LogInformation("Suscripciones: {Checked} vendedores revisados, {Notified} avisos", merchantIds.Count, notified);
        return new SubscriptionDailyRunResult(merchantIds.Count, notified, summarySent);
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
        var reminder = SubscriptionReminders.For(status, subscription.LastPeriodKind, subscription.LastPackage, now);
        if (reminder is null)
        {
            return false;
        }

        var greetingName = await scopedDb.Merchants.AsNoTracking()
            .Where(m => m.Id == merchantId)
            .Select(m => Merchant.GreetingNameFor(m.OwnerName, m.BusinessName))
            .SingleAsync(cancellationToken);
        var message = MerchantEmails.SubscriptionReminder(reminder, greetingName, settings);
        var result = await services.GetRequiredService<INotificationService>().NotifyAsync(new NotificationRequest(
            merchantId, reminder.NotificationType, message.Title, message.Body, MerchantEmails.PlanPath,
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
public sealed record SubscriptionDailyRunResult(int Checked, int Notified, bool MonthlySummarySent);
