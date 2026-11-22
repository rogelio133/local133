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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Local133.Infrastructure.Jobs;

/// <summary>
/// Job diario de la suscripción (Hangfire, 9:00 en México, 5.7): por cada vendedor con un vencimiento cerca, crea el
/// link de renovación (con el paquete del último pago) y manda el recordatorio que le toca según
/// <see cref="SubscriptionReminders"/>; el día 1 manda el resumen mensual. Es idempotente: el aviso es único por
/// <c>DedupKey</c> y el link pendiente del mismo paquete y monto se reutiliza, así que repetir la corrida no duplica nada.
/// La baja al gratuito es un cálculo: el job solo avisa, no cambia ningún estatus.
/// </summary>
public sealed class SubscriptionDailyJob(
    AppDbContext db,
    IServiceScopeFactory scopes,
    IOptions<SiteUrlOptions> siteOptions,
    IHostEnvironment environment,
    TimeProvider clock,
    ILogger<SubscriptionDailyJob> logger)
{
    public const string RecurringJobId = "suscripciones-diario";

    /// <summary>Todos los días a las 9:00 (el job se programa en hora de México).</summary>
    public const string Cron = "0 9 * * *";

    /// <summary>Tope de vendedores por corrida (los demás quedan para la siguiente; las ventanas duran varios días).</summary>
    public const int MaxMerchantsPerRun = 2000;

    /// <returns>Vendedores revisados, avisos creados y links de renovación listos.</returns>
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

        var baseUri = RenewalBaseUri();
        int notified = 0, links = 0;
        foreach (var merchantId in merchantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Un scope (y un DbContext) por vendedor: una falla no contamina a los siguientes.
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                var outcome = await RemindAsync(scope.ServiceProvider, merchantId, settings, baseUri, now, cancellationToken);
                notified += outcome.Notified ? 1 : 0;
                links += outcome.LinkReady ? 1 : 0;
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

        logger.LogInformation("Suscripciones: {Checked} vendedores revisados, {Notified} avisos, {Links} links de renovación listos",
            merchantIds.Count, notified, links);
        return new SubscriptionDailyRunResult(merchantIds.Count, notified, links, summarySent);
    }

    private async Task<(bool Notified, bool LinkReady)> RemindAsync(
        IServiceProvider services, int merchantId, SystemSettings settings, Uri? baseUri, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var scopedDb = services.GetRequiredService<AppDbContext>();
        var subscription = await scopedDb.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Include(s => s.Periods)
            .SingleAsync(s => s.MerchantId == merchantId, cancellationToken);
        var status = subscription.StatusAt(now, settings.GraceDays);
        var reminder = SubscriptionReminders.For(status, subscription.LastPeriodKind, subscription.LastPackage, now);
        if (reminder is null)
        {
            return (false, false);
        }

        // El link se asegura cada día de la ventana (si ayer falló Pey, hoy se reintenta); el aviso sale una vez.
        decimal? renewalAmount = null;
        if (reminder.RenewalPackage is { } package)
        {
            renewalAmount = await EnsureRenewalLinkAsync(services, merchantId, package, baseUri, cancellationToken);
        }

        var notifications = services.GetRequiredService<INotificationService>();
        var businessName = await scopedDb.Merchants.AsNoTracking()
            .Where(m => m.Id == merchantId)
            .Select(m => m.BusinessName)
            .SingleAsync(cancellationToken);
        var message = MerchantEmails.SubscriptionReminder(reminder, businessName, renewalAmount, settings);
        var result = await notifications.NotifyAsync(new NotificationRequest(
            merchantId, reminder.NotificationType, message.Title, message.Body, MerchantEmails.PlanPath,
            reminder.DedupKey, message.Email), cancellationToken);
        if (result is NotificationResult.Created)
        {
            logger.LogInformation("Suscripciones: recordatorio {Stage} ({Kind}) para el vendedor {MerchantId}",
                reminder.Stage, reminder.EndingKind, merchantId);
        }

        return (result is NotificationResult.Created, renewalAmount is not null);
    }

    /// <returns>Monto del link listo (creado o reutilizado); <c>null</c> si no se pudo.</returns>
    private async Task<decimal?> EnsureRenewalLinkAsync(
        IServiceProvider services, int merchantId, SubscriptionPackage package, Uri? baseUri, CancellationToken cancellationToken)
    {
        if (baseUri is null)
        {
            logger.LogWarning("Suscripciones: sin link de renovación para el vendedor {MerchantId}: App:PublicBaseUrl debe ser HTTPS", merchantId);
            return null;
        }

        var checkout = services.GetRequiredService<ISubscriptionCheckout>();
        var result = await checkout.StartAsync(
            new StartCheckoutCommand(merchantId, package, baseUri, SubscriptionChargeOrigin.Renewal), cancellationToken);
        switch (result)
        {
            case CheckoutResult.Redirect redirect:
                return await services.GetRequiredService<AppDbContext>().SubscriptionCharges.IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.Id == redirect.ChargeId)
                    .Select(c => (decimal?)c.Amount)
                    .SingleAsync(cancellationToken);
            case CheckoutResult.GatewayFailed failed:
                logger.LogWarning("Suscripciones: Pey no generó el link de renovación del vendedor {MerchantId} (cargo {ChargeId}); se reintenta mañana",
                    merchantId, failed.ChargeId);
                return null;
            default:
                logger.LogWarning("Suscripciones: sin link de renovación para el vendedor {MerchantId} ({Result})", merchantId, result.GetType().Name);
                return null;
        }
    }

    /// <summary>Base HTTPS para las URLs de regreso del link (Pey las exige); <c>null</c> si no hay.</summary>
    private Uri? RenewalBaseUri() =>
        siteOptions.Value.BaseUrl(environment.IsDevelopment()) is { } baseUrl
        && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : null;
}

/// <param name="Checked">Vendedores con un vencimiento cerca que se revisaron.</param>
/// <param name="Notified">Avisos nuevos (los repetidos no cuentan).</param>
/// <param name="LinksReady">Links de renovación creados o reutilizados.</param>
/// <param name="MonthlySummarySent">Se llamó al resumen mensual (día 1).</param>
public sealed record SubscriptionDailyRunResult(int Checked, int Notified, int LinksReady, bool MonthlySummarySent);
