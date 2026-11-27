using Local133.Application.Notifications;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Notifications;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Jobs;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.7: job <c>suscripciones-diario</c> con el reloj congelado a las 9:00 de México: recordatorios idempotentes, fin de
/// la prueba, baja al gratuito y resumen del día 1. El job <b>nunca</b> crea links de Pey: el link nace cuando el
/// vendedor elige su paquete en «Mi plan».
/// </summary>
public class SubscriptionDailyJobTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    private const string ThirdEmail = "tercero@prueba.mx";

    private DateTimeOffset _now;

    [Fact]
    public async Task El_anual_15_dias_antes_avisa_una_sola_vez_sin_crear_links()
    {
        await ActiveMerchantAsync(Email);
        Freeze();
        await SeedPaidAsync(Email, SubscriptionPackage.Annual, endsInDays: 15, lockedAnnualPrice: 1200m);

        var first = await RunJobAsync();

        Assert.Equal(new SubscriptionDailyRunResult(1, 1, false), first);
        AssertNoPey();
        Assert.Empty(await ChargesAsync());

        var notification = Assert.Single(await NotificationsAsync());
        Assert.Equal(NotificationType.SubscriptionUpcoming, notification.Type);
        Assert.Equal("/app/plan", notification.Url);
        Assert.StartsWith("sub:AnnualUpcoming:", notification.DedupKey, StringComparison.Ordinal);
        Assert.NotNull(notification.EmailSentAt);
        var email = Assert.Single(EmailsTo(Email));
        Assert.Contains("anual", email.Subject, StringComparison.Ordinal);
        Assert.Equal("http://localhost:5231/app/plan", email.Link); // el correo lleva a «Mi plan», nunca a Pey
        Assert.Contains("elige tu paquete", email.Html, StringComparison.Ordinal);

        var second = await RunJobAsync();

        Assert.Equal(new SubscriptionDailyRunResult(1, 0, false), second);
        Assert.Single(await NotificationsAsync());
        Assert.Single(EmailsTo(Email));
        AssertNoPey();
    }

    [Fact]
    public async Task El_mensual_avisa_3_dias_antes_el_dia_del_vencimiento_y_en_la_gracia_sin_crear_links()
    {
        await ActiveMerchantAsync(Email);
        var today = MexicoTime.Today(Freeze());
        await SeedPaidAsync(Email, SubscriptionPackage.OneMonth, endsInDays: 3);

        await RunJobAsync();                                    // 3 días antes
        await RunAtAsync(today.AddDays(1));                     // 2 días antes: mismo aviso
        await RunAtAsync(today.AddDays(3));                     // vence hoy
        await RunAtAsync(today.AddDays(4));                     // día 1 de la gracia: nada
        await RunAtAsync(today.AddDays(6));                     // día 3 de la gracia
        await RunAtAsync(today.AddDays(7));                     // mismo aviso de la gracia

        Assert.Equal(
            [NotificationType.SubscriptionUpcoming, NotificationType.SubscriptionDueToday, NotificationType.SubscriptionGrace],
            (await NotificationsAsync()).Select(n => n.Type));
        Assert.Equal(3, EmailsTo(Email).Count);
        AssertNoPey();
        Assert.Empty(await ChargesAsync());
    }

    [Fact]
    public async Task Fuera_de_las_ventanas_no_avisa()
    {
        await ActiveMerchantAsync(Email);
        Freeze();
        await SeedPaidAsync(Email, SubscriptionPackage.OneMonth, endsInDays: 10); // el mensual no avisa 15 días antes
        await CreateMerchantAsync(OtherEmail);
        await SeedTrialEndingAsync(OtherEmail, endsInDays: -5);                    // bajó hace 5 días: ya no se avisa

        var result = await RunJobAsync();

        Assert.Equal(new SubscriptionDailyRunResult(2, 0, false), result);
        Assert.Empty(await NotificationsAsync());
        AssertNoPey();
    }

    [Fact]
    public async Task La_prueba_avisa_3_dias_antes_y_al_bajar_al_gratuito()
    {
        await CreateMerchantAsync(Email);
        var today = MexicoTime.Today(Freeze());
        await SeedTrialEndingAsync(Email, endsInDays: 3);

        await RunJobAsync();
        await RunAtAsync(today.AddDays(4)); // terminó ayer a las 15:00: sin gracia

        Assert.Equal([NotificationType.TrialEnding, NotificationType.DowngradedToFree], (await NotificationsAsync()).Select(n => n.Type));
        AssertNoPey();
        Assert.Empty(await ChargesAsync());
        var emails = EmailsTo(Email);
        Assert.Contains("prueba", emails[0].Subject, StringComparison.Ordinal);
        Assert.Equal("Tu plan venció · Local133", emails[1].Subject);
    }

    [Fact]
    public async Task La_prueba_con_un_pago_por_delante_no_avisa_que_termina()
    {
        await CreateMerchantAsync(Email);
        Freeze();
        var trialEnd = At15(3);
        await SeedPlanAsync(Email,
            (SubscriptionPeriodKind.Trial, MexicoTime.AddDays(trialEnd, -30), trialEnd),
            (SubscriptionPeriodKind.Paid, trialEnd, MexicoTime.AddMonths(trialEnd, 1)));
        await SetLastPackageAsync(Email, SubscriptionPackage.OneMonth);

        var result = await RunJobAsync();

        Assert.Equal(0, result.Notified);
        AssertNoPey();
    }

    [Fact]
    public async Task No_avisa_a_dados_de_baja_ni_en_el_onboarding()
    {
        await CreateMerchantAsync(Email);
        Freeze();
        await SeedPaidAsync(Email, SubscriptionPackage.OneMonth, endsInDays: 3);
        await UpdateMerchantAsync(Email, m => m.Status = MerchantStatus.Disabled);
        await CreateMerchantAsync(OtherEmail); // onboarding: sin suscripción

        var result = await RunJobAsync();

        Assert.Equal(new SubscriptionDailyRunResult(0, 0, false), result);
        Assert.Empty(await NotificationsAsync());
    }

    [Fact]
    public async Task La_falla_de_un_vendedor_no_detiene_a_los_demas()
    {
        await CreateMerchantAsync(Email);
        Freeze();
        await SeedPaidAsync(Email, SubscriptionPackage.OneMonth, endsInDays: 3);
        await CreateMerchantAsync(ThirdEmail);
        await SeedTrialEndingAsync(ThirdEmail, endsInDays: 3);
        var failingId = await MerchantIdAsync(Email);

        // Una falla inesperada al avisar al primero (el servicio real nunca lanza por el correo).
        var scopes = new OverridingScopeFactory(Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            inner => new FailingNotificationService(inner, failingId));
        await using var scope = Factory.Services.CreateAsyncScope();
        var job = ActivatorUtilities.CreateInstance<SubscriptionDailyJob>(scope.ServiceProvider, (IServiceScopeFactory)scopes);
        var result = await job.RunAsync(CancellationToken.None);

        Assert.Equal(2, result.Checked);
        Assert.Equal(1, result.Notified);
        var notification = Assert.Single(await NotificationsAsync());
        Assert.Equal(await MerchantIdAsync(ThirdEmail), notification.MerchantId);
        Assert.Equal(NotificationType.TrialEnding, notification.Type);
    }

    [Fact]
    public async Task El_dia_1_pide_el_resumen_del_mes_anterior_una_vez_por_corrida()
    {
        var today = MexicoTime.Today(DateTimeOffset.UtcNow);
        var firstOfNextMonth = MexicoTime.NextMonthStart(today);

        var dayOne = await RunAtAsync(firstOfNextMonth);
        var dayTwo = await RunAtAsync(firstOfNextMonth.AddDays(1));

        Assert.True(dayOne.MonthlySummarySent);
        Assert.False(dayTwo.MonthlySummarySent);
        Assert.Equal(MexicoTime.MonthStart(today), Assert.Single(Factory.MonthlySummaries.Months));
    }

    // --- Ayudas -------------------------------------------------------------------------------------------------------

    private DateTimeOffset Freeze() => _now = Factory.Clock.FreezeAtMexicoNine();

    /// <summary>15:00 de México, a <paramref name="days"/> días de hoy.</summary>
    private DateTimeOffset At15(int days) => MexicoTime.AddDays(_now, days).AddHours(6);

    /// <summary>Prueba y después un periodo pagado del paquete que vence a las 15:00 dentro de <paramref name="endsInDays"/> días.</summary>
    private async Task SeedPaidAsync(string email, SubscriptionPackage package, int endsInDays, decimal? lockedAnnualPrice = null)
    {
        var end = At15(endsInDays);
        var start = MexicoTime.AddMonths(end, -package.Months());
        await SeedPlanAsync(email,
            (SubscriptionPeriodKind.Trial, MexicoTime.AddDays(start, -30), start),
            (SubscriptionPeriodKind.Paid, start, end));
        await SetLastPackageAsync(email, package, lockedAnnualPrice);
    }

    /// <summary>Solo la prueba, que termina a las 15:00 dentro de <paramref name="endsInDays"/> días (negativo: ya terminó).</summary>
    private Task SeedTrialEndingAsync(string email, int endsInDays)
    {
        var end = At15(endsInDays);
        return SeedPlanAsync(email, (SubscriptionPeriodKind.Trial, MexicoTime.AddDays(end, -30), end));
    }

    private Task SetLastPackageAsync(string email, SubscriptionPackage package, decimal? lockedAnnualPrice = null) =>
        WithDbAsync(async db =>
        {
            var merchant = await MerchantOfAsync(db, email);
            var subscription = await db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.MerchantId == merchant.Id);
            subscription.LastPackage = package;
            subscription.LockedAnnualPrice = lockedAnnualPrice;
            await db.SaveChangesAsync();
        });

    private Task<SubscriptionDailyRunResult> RunAtAsync(DateOnly day)
    {
        Factory.Clock.FreezeAtMexicoNine(day);
        return RunJobAsync();
    }

    private async Task<SubscriptionDailyRunResult> RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SubscriptionDailyJob>().RunAsync(CancellationToken.None);
    }

    /// <summary>El job no crea ni consulta links de Pey.</summary>
    private void AssertNoPey()
    {
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        Assert.Empty(Factory.Gateway.PaymentLinkLookups);
    }

    private Task<List<Notification>> NotificationsAsync() =>
        WithDbAsync(db => db.Notifications.IgnoreQueryFilters().AsNoTracking().OrderBy(n => n.Id).ToListAsync());

    private List<SentEmail> EmailsTo(string email) =>
        Factory.Emails.Sent.Where(e => string.Equals(e.To, email, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Avisos reales, salvo para un vendedor, al que le falla.</summary>
    private sealed class FailingNotificationService(INotificationService inner, int failingMerchantId) : INotificationService
    {
        public Task<NotificationResult> NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default) =>
            request.MerchantId == failingMerchantId
                ? throw new InvalidOperationException("falla simulada")
                : inner.NotifyAsync(request, cancellationToken);
    }

    /// <summary>Scopes reales con <see cref="INotificationService"/> reemplazado.</summary>
    private sealed class OverridingScopeFactory(
        IServiceScopeFactory inner, Func<INotificationService, INotificationService> wrap) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(inner.CreateScope(), wrap);

        private sealed class Scope(IServiceScope scope, Func<INotificationService, INotificationService> wrap)
            : IServiceScope, IServiceProvider, IAsyncDisposable
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) => serviceType == typeof(INotificationService)
                ? wrap(scope.ServiceProvider.GetRequiredService<INotificationService>())
                : scope.ServiceProvider.GetService(serviceType);

            public void Dispose() => scope.Dispose();

            public ValueTask DisposeAsync() => scope is IAsyncDisposable a ? a.DisposeAsync() : ValueTask.CompletedTask;
        }
    }
}
