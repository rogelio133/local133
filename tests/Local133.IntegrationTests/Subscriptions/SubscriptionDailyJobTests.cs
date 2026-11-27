using Local133.Application.Notifications;
using Local133.Domain.Audit;
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
/// 5.7 y 5.16: job <c>suscripciones-diario</c> con el reloj congelado a las 9:00 de México: recordatorios idempotentes
/// por plan, fin de la prueba, vencido y su seguimiento del día 7, anuncio del cambio de planes y resumen del día 1. El
/// job <b>nunca</b> crea links de Pey: los botones van al checkout y el link nace solo al pulsar «Pagar».
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
        Assert.Equal("/app/plan/checkout/pro/anual", notification.Url); // el resumen de su último pedido
        Assert.StartsWith("sub:AnnualUpcoming:", notification.DedupKey, StringComparison.Ordinal);
        Assert.NotNull(notification.EmailSentAt);
        var email = Assert.Single(EmailsTo(Email));
        Assert.Contains("Pro Anual", email.Subject, StringComparison.Ordinal);
        Assert.Equal("http://localhost:5231/app/plan/checkout/pro/anual", email.Link); // al checkout, nunca a Pey
        Assert.Contains("Renovar mi plan Pro", email.Html, StringComparison.Ordinal);

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
    public async Task La_prueba_avisa_3_dias_antes_y_al_vencer()
    {
        await CreateMerchantAsync(Email);
        var today = MexicoTime.Today(Freeze());
        await SeedTrialEndingAsync(Email, endsInDays: 3);

        await RunJobAsync();
        await RunAtAsync(today.AddDays(4)); // terminó ayer a las 15:00: sin gracia

        var notifications = await NotificationsAsync();
        Assert.Equal([NotificationType.TrialEnding, NotificationType.PlanExpired], notifications.Select(n => n.Type));
        Assert.All(notifications, n => Assert.Equal("/app/plan/checkout", n.Url)); // nunca pagó: el paso 1
        AssertNoPey();
        Assert.Empty(await ChargesAsync());
        var emails = EmailsTo(Email);
        Assert.Contains("prueba", emails[0].Subject, StringComparison.Ordinal);
        Assert.Equal("Tu plan venció · Local133", emails[1].Subject);
        Assert.Equal("http://localhost:5231/app/plan/checkout", emails[1].Link);
    }

    [Fact]
    public async Task El_vencido_recibe_el_seguimiento_del_dia_7_una_sola_vez()
    {
        await CreateMerchantAsync(Email);
        var today = MexicoTime.Today(Freeze());
        await SeedTrialEndingAsync(Email, endsInDays: -1); // terminó ayer a las 15:00: día 1 desde el vencimiento

        await RunJobAsync();                // día 1: vencido
        await RunAtAsync(today.AddDays(5)); // día 6: nada
        await RunAtAsync(today.AddDays(6)); // día 7: seguimiento
        await RunAtAsync(today.AddDays(7)); // día 8: el mismo seguimiento, no se repite
        await RunAtAsync(today.AddDays(9)); // día 10: nada más

        var notifications = await NotificationsAsync();
        Assert.Equal([NotificationType.PlanExpired, NotificationType.PlanExpiredFollowUp], notifications.Select(n => n.Type));
        Assert.StartsWith("sub:ExpiredFollowUp:", notifications[1].DedupKey, StringComparison.Ordinal);
        var followUp = EmailsTo(Email)[1];
        Assert.Equal("Tus clientes te siguen buscando · Local133", followUp.Subject);
        Assert.Contains("Básico cuesta $99 al mes y Pro $199 al mes.", followUp.Html, StringComparison.Ordinal);
        Assert.Equal("http://localhost:5231/app/plan/checkout", followUp.Link);
        AssertNoPey();
        Assert.Empty(await ChargesAsync());
    }

    [Fact]
    public async Task El_seguimiento_sale_al_dia_siguiente_si_el_job_no_corrio_el_dia_7()
    {
        await CreateMerchantAsync(Email);
        var today = MexicoTime.Today(Freeze());
        await SeedTrialEndingAsync(Email, endsInDays: -1);

        await RunAtAsync(today.AddDays(7)); // día 8

        Assert.Equal([NotificationType.PlanExpiredFollowUp], (await NotificationsAsync()).Select(n => n.Type));
    }

    [Fact]
    public async Task Basico_avisa_con_su_plan_y_lleva_al_resumen_de_Basico()
    {
        await ActiveMerchantAsync(Email);
        Freeze();
        var end = At15(3);
        var start = MexicoTime.AddMonths(end, -1);
        await SeedPlanAsync(Email,
            (SubscriptionPeriodKind.Trial, SubscriptionPlan.Pro, MexicoTime.AddDays(start, -15), start),
            (SubscriptionPeriodKind.Paid, SubscriptionPlan.Basic, start, end));
        await SetLastPackageAsync(Email, SubscriptionPackage.OneMonth);

        await RunJobAsync();

        var notification = Assert.Single(await NotificationsAsync());
        Assert.Equal(NotificationType.SubscriptionUpcoming, notification.Type);
        Assert.Equal("Tu plan Básico vence en 3 días", notification.Title);
        Assert.Equal("/app/plan/checkout/basico/1-mes", notification.Url);
        var email = Assert.Single(EmailsTo(Email));
        Assert.Equal("Tu plan Básico vence en 3 días · Local133", email.Subject);
        Assert.Equal("http://localhost:5231/app/plan/checkout/basico/1-mes", email.Link);
        Assert.DoesNotContain("Pro", email.Subject, StringComparison.Ordinal);
        AssertNoPey();
    }

    [Fact]
    public async Task Los_dias_de_transicion_avisan_que_terminan_con_su_nombre()
    {
        await CreateMerchantAsync(Email);
        Freeze();
        var end = At15(2);
        await SeedPlanAsync(Email,
            (SubscriptionPeriodKind.Trial, SubscriptionPlan.Pro, MexicoTime.AddDays(end, -40), MexicoTime.AddDays(end, -15)),
            (SubscriptionPeriodKind.Transition, SubscriptionPlan.Basic, MexicoTime.AddDays(end, -15), end));

        await RunJobAsync();

        var notification = Assert.Single(await NotificationsAsync());
        Assert.Equal(NotificationType.PlanDaysEnding, notification.Type);
        Assert.Equal("Tus días de Básico de transición terminan en 2 días", notification.Title);
        Assert.Equal("/app/plan/checkout", notification.Url);
    }

    // --- Anuncio del cambio de planes (5.16) ---------------------------------------------------------------------------

    [Fact]
    public async Task El_anuncio_sale_una_sola_vez_a_los_registrados_antes_del_corte()
    {
        await CreateMerchantAsync(Email);
        Freeze();
        var transitionEnd = At15(12);
        await SeedPlanAsync(Email,
            (SubscriptionPeriodKind.Trial, SubscriptionPlan.Pro, MexicoTime.AddDays(_now, -40), MexicoTime.AddDays(_now, -10)),
            (SubscriptionPeriodKind.Transition, SubscriptionPlan.Basic, _now.AddHours(-1), transitionEnd));
        await CreateMerchantAsync(OtherEmail);             // onboarding
        await CreateMerchantAsync(ThirdEmail);
        await UpdateMerchantAsync(ThirdEmail, m => m.Status = MerchantStatus.Disabled);
        foreach (var before in new[] { Email, OtherEmail, ThirdEmail })
        {
            await UpdateMerchantAsync(before, m => m.CreatedAt = _now.AddDays(-60));
        }

        await SeedPlanChangeAsync(_now.AddMinutes(-30));
        await CreateMerchantAsync("nuevo@prueba.mx");      // registrado después del corte
        await UpdateMerchantAsync("nuevo@prueba.mx", m => m.CreatedAt = _now.AddMinutes(-10));

        var first = await RunJobAsync();
        var second = await RunJobAsync();

        Assert.Equal(2, first.AnnouncementsSent);
        Assert.Equal(0, second.AnnouncementsSent);
        var announcements = (await NotificationsAsync()).Where(n => n.Type == NotificationType.PlanChangeAnnouncement).ToList();
        Assert.Equal(
            new[] { await MerchantIdAsync(Email), await MerchantIdAsync(OtherEmail) }.Order(),
            announcements.Select(n => n.MerchantId).Order());
        Assert.All(announcements, n =>
        {
            Assert.Equal("planes-2026-12", n.DedupKey);
            Assert.Equal("/app/plan/checkout", n.Url);
        });

        var email = Assert.Single(EmailsTo(Email));
        Assert.Equal("Nuevos planes: Básico y Pro · Local133", email.Subject);
        Assert.Contains("ya no hay plan gratuito", email.Html, StringComparison.Ordinal);
        Assert.Contains(
            $"te regalamos días de Básico hasta el {MexicoTime.DateOf(transitionEnd).ToString("d 'de' MMMM 'de' yyyy", System.Globalization.CultureInfo.GetCultureInfo("es-MX"))}",
            email.Html, StringComparison.Ordinal);
        Assert.Contains("empieza con tu primer cobro", Assert.Single(EmailsTo(OtherEmail)).Html, StringComparison.Ordinal);
        Assert.Empty(EmailsTo(ThirdEmail));
        Assert.Empty(EmailsTo("nuevo@prueba.mx"));
        AssertNoPey();
    }

    [Fact]
    public async Task Sin_el_corte_o_pasados_30_dias_no_hay_anuncio()
    {
        await CreateMerchantAsync(Email);
        Freeze();

        Assert.Equal(0, (await RunJobAsync()).AnnouncementsSent); // sin el renglón PlanChange

        await SeedPlanChangeAsync(_now.AddDays(-30));
        Assert.Equal(0, (await RunJobAsync()).AnnouncementsSent);
        Assert.Empty(await NotificationsAsync());
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

    /// <summary>El renglón global que deja la migración <c>PlanChangeAnnouncement</c>: el corte del anuncio.</summary>
    private Task SeedPlanChangeAsync(DateTimeOffset at) => WithDbAsync(async db =>
    {
        db.AuditLogs.Add(AuditLog.Create("sistema", "sistema", AuditAction.PlanChange, null, "Cambio de planes", "Cambio de planes 2026-12", null, at));
        await db.SaveChangesAsync();
    });

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
