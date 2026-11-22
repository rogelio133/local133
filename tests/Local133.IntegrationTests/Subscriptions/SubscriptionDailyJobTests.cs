using Local133.Application.Payments;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Notifications;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Email;
using Local133.Infrastructure.Jobs;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.7: job <c>suscripciones-diario</c> con el reloj congelado a las 9:00 de México: links de renovación, recordatorios
/// idempotentes, fin de la prueba, baja al gratuito y resumen del día 1.
/// </summary>
public class SubscriptionDailyJobTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    /// <summary>Base HTTPS para los links de renovación (en pruebas, App:PublicBaseUrl está vacío).</summary>
    private const string PublicBase = "https://app.local133.test";

    private const string ThirdEmail = "tercero@prueba.mx";

    private DateTimeOffset _now;

    [Fact]
    public async Task El_anual_15_dias_antes_crea_el_link_de_renovacion_y_avisa_una_sola_vez()
    {
        await ActiveMerchantAsync(Email);
        Freeze();
        await SeedPaidAsync(Email, SubscriptionPackage.Annual, endsInDays: 15, lockedAnnualPrice: 1200m);

        var first = await RunJobAsync();

        Assert.Equal(new SubscriptionDailyRunResult(1, 1, 1, false), first);
        var charge = Assert.Single(await ChargesAsync());
        Assert.Equal(SubscriptionChargeOrigin.Renewal, charge.Origin);
        Assert.Equal(SubscriptionPackage.Annual, charge.Package);
        Assert.Equal(1200m, charge.Amount); // precio congelado del anual
        Assert.Equal(SubscriptionChargeStatus.Pending, charge.Status);
        var request = Assert.Single(Factory.Gateway.PaymentLinkRequests);
        Assert.StartsWith(PublicBase + "/app/plan/pago/", request.SuccessUrl.AbsoluteUri, StringComparison.Ordinal);

        var notification = Assert.Single(await NotificationsAsync());
        Assert.Equal(NotificationType.SubscriptionUpcoming, notification.Type);
        Assert.Equal("/app/plan", notification.Url);
        Assert.StartsWith("sub:AnnualUpcoming:", notification.DedupKey, StringComparison.Ordinal);
        Assert.NotNull(notification.EmailSentAt);
        var email = Assert.Single(EmailsTo(Email));
        Assert.Contains("anual", email.Subject, StringComparison.Ordinal);
        Assert.Equal("http://localhost:5231/app/plan", email.Link); // el correo lleva a «Mi plan», nunca a Pey
        Assert.Contains("$1,200.00", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("pay.example.test", email.Html, StringComparison.Ordinal);

        var second = await RunJobAsync();

        Assert.Equal(new SubscriptionDailyRunResult(1, 0, 1, false), second); // el link se reutiliza
        Assert.Single(Factory.Gateway.PaymentLinkRequests);
        Assert.Single(await ChargesAsync());
        Assert.Single(await NotificationsAsync());
        Assert.Single(EmailsTo(Email));
    }

    [Fact]
    public async Task El_mensual_avisa_3_dias_antes_el_dia_del_vencimiento_y_en_la_gracia_con_un_solo_link()
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
        Assert.Single(Factory.Gateway.PaymentLinkRequests);
        var charge = Assert.Single(await ChargesAsync());
        Assert.Equal(SubscriptionChargeOrigin.Renewal, charge.Origin);
        Assert.Equal(SubscriptionPackage.OneMonth, charge.Package);
    }

    [Fact]
    public async Task Fuera_de_las_ventanas_no_avisa_ni_crea_links()
    {
        await ActiveMerchantAsync(Email);
        Freeze();
        await SeedPaidAsync(Email, SubscriptionPackage.OneMonth, endsInDays: 10); // el mensual no avisa 15 días antes
        await CreateMerchantAsync(OtherEmail);
        await SeedTrialEndingAsync(OtherEmail, endsInDays: -5);                    // bajó hace 5 días: ya no se avisa

        var result = await RunJobAsync();

        Assert.Equal(new SubscriptionDailyRunResult(2, 0, 0, false), result);
        Assert.Empty(await NotificationsAsync());
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
    }

    [Fact]
    public async Task La_prueba_avisa_3_dias_antes_sin_link_y_al_bajar_al_gratuito()
    {
        await CreateMerchantAsync(Email);
        var today = MexicoTime.Today(Freeze());
        await SeedTrialEndingAsync(Email, endsInDays: 3);

        await RunJobAsync();
        await RunAtAsync(today.AddDays(4)); // terminó ayer a las 15:00: sin gracia

        Assert.Equal([NotificationType.TrialEnding, NotificationType.DowngradedToFree], (await NotificationsAsync()).Select(n => n.Type));
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        Assert.Empty(await ChargesAsync());
        var emails = EmailsTo(Email);
        Assert.Contains("prueba", emails[0].Subject, StringComparison.Ordinal);
        Assert.Equal("Pasaste al plan gratuito · Local133", emails[1].Subject);
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
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
    }

    [Fact]
    public async Task Si_Pey_falla_el_aviso_sale_y_el_link_se_reintenta_al_dia_siguiente()
    {
        await ActiveMerchantAsync(Email);
        var today = MexicoTime.Today(Freeze());
        await SeedPaidAsync(Email, SubscriptionPackage.OneMonth, endsInDays: 3);
        Factory.Gateway.PaymentLinkException = new PaymentGatewayUnavailableException("timeout");

        var first = await RunJobAsync();

        Assert.Equal(new SubscriptionDailyRunResult(1, 1, 0, false), first);
        Assert.Equal(SubscriptionChargeStatus.Failed, Assert.Single(await ChargesAsync()).Status);
        Assert.DoesNotContain("link de renovación", Assert.Single(EmailsTo(Email)).Html, StringComparison.Ordinal);

        Factory.Gateway.PaymentLinkException = null;
        var second = await RunAtAsync(today.AddDays(1));

        Assert.Equal(new SubscriptionDailyRunResult(1, 0, 1, false), second); // mismo aviso: no se repite
        Assert.Equal(
            [SubscriptionChargeStatus.Failed, SubscriptionChargeStatus.Pending],
            (await ChargesAsync()).Select(c => c.Status));
        Assert.Single(await NotificationsAsync());
    }

    [Fact]
    public async Task Sin_HTTPS_avisa_sin_crear_el_link()
    {
        await ActiveMerchantAsync(Email);
        Freeze();
        await SeedPaidAsync(Email, SubscriptionPackage.OneMonth, endsInDays: 3);

        var result = await RunJobAsync(https: false);

        Assert.Equal(new SubscriptionDailyRunResult(1, 1, 0, false), result);
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        Assert.Empty(await ChargesAsync());
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

        Assert.Equal(new SubscriptionDailyRunResult(0, 0, 0, false), result);
        Assert.Empty(await NotificationsAsync());
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
    }

    [Fact]
    public async Task La_falla_de_un_vendedor_no_detiene_a_los_demas()
    {
        await CreateMerchantAsync(Email);
        Freeze();
        await SeedPaidAsync(Email, SubscriptionPackage.OneMonth, endsInDays: 3);
        await CreateMerchantAsync(ThirdEmail);
        await SeedTrialEndingAsync(ThirdEmail, endsInDays: 3);
        // Una falla inesperada (no es de Pey) al crear el link del primero.
        Factory.Gateway.PaymentLinkException = new HttpRequestException("conexión rota");

        var result = await RunJobAsync();

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

    /// <param name="https">Con la base HTTPS que exige Pey; sin ella, la del respaldo de desarrollo (http).</param>
    private async Task<SubscriptionDailyRunResult> RunJobAsync(bool https = true)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var job = https
            ? ActivatorUtilities.CreateInstance<SubscriptionDailyJob>(
                scope.ServiceProvider, Options.Create(new SiteUrlOptions { PublicBaseUrl = PublicBase }))
            : scope.ServiceProvider.GetRequiredService<SubscriptionDailyJob>();
        return await job.RunAsync(CancellationToken.None);
    }

    private Task<List<Notification>> NotificationsAsync() =>
        WithDbAsync(db => db.Notifications.IgnoreQueryFilters().AsNoTracking().OrderBy(n => n.Id).ToListAsync());

    private List<SentEmail> EmailsTo(string email) =>
        Factory.Emails.Sent.Where(e => string.Equals(e.To, email, StringComparison.OrdinalIgnoreCase)).ToList();
}
