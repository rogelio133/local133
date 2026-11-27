using System.Text.Json;
using Local133.Application.BankAccounts;
using Local133.Application.Payments;
using Local133.Application.Subscriptions;
using Local133.Application.Tenancy;
using Local133.Domain.BankAccounts;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Sales;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Jobs;
using Local133.Infrastructure.Persistence;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.13: la prueba de Pro empieza con el <b>primer cobro pagado</b> (webhook QR, push o conciliación), en el
/// <c>PaidAt</c> del pago, una sola vez por vendedor y sin tumbar nunca el webhook. Activar la cuenta bancaria ya no la
/// empieza. Respaldo: el job <c>suscripciones-diario</c>.
/// </summary>
public class TrialTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string PeyId = "01JBAVWE4NS5TGXHWGMX3KH23Q";

    // --- Activar la cuenta ya no empieza la prueba ------------------------------------------------------------------

    [Fact]
    public async Task El_webhook_que_activa_la_cuenta_ya_no_empieza_la_prueba()
    {
        var merchantId = await InReviewAsync(Email, PeyId);

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(BankAccountUpdatedBody(PeyId))));

        Assert.Equal(MerchantStatus.Active, (await WithDbAsync(db => db.Merchants.AsNoTracking().SingleAsync(m => m.Id == merchantId))).Status);
        Assert.Null(await SubscriptionOfAsync(merchantId));
    }

    [Fact]
    public async Task La_consulta_del_sondeo_o_del_boton_que_la_ve_activa_ya_no_empieza_la_prueba()
    {
        var merchantId = await InReviewAsync(Email, PeyId);
        var accountId = await WithDbAsync(db => db.BankAccounts.Where(b => b.MerchantId == merchantId).Select(b => b.Id).SingleAsync());
        Factory.Gateway.LookupStatus = PeyCodiStatus.Active;

        await SyncAsync(accountId);

        Assert.Null(await SubscriptionOfAsync(merchantId));
    }

    [Fact]
    public async Task El_alta_que_Pey_activa_de_inmediato_no_empieza_la_prueba_y_el_Inicio_dice_que_empieza_con_el_primer_cobro()
    {
        Factory.Gateway.RegisterStatus = PeyCodiStatus.Active;
        await CreateMerchantAsync(Email);
        var client = CreateClient();
        await LoginAsync(client, Email);

        await PostFormAsync(client, "/app/cuenta-bancaria", new Dictionary<string, string>
        {
            ["Input.Entity"] = nameof(PeyEntity.Person),
            ["Input.HolderName"] = "Ramón Orraca Lascurain",
            ["Input.Clabe"] = TestClabe,
            ["Input.Alias"] = "Tacos Don Ramón",
        });

        Assert.Null(await SubscriptionOfAsync(await MerchantIdAsync(Email)));
        var html = await GetHtmlAsync(client, "/app");
        Assert.Contains("data-merchant-status=\"Active\"", html);
        Assert.Contains("data-trial-first-charge", html);
        Assert.Contains("Tu prueba de 15 días de Pro empieza con tu primer cobro.", html);
        Assert.DoesNotContain("data-plan-tier", html);
        Assert.Contains("empieza con tu primer cobro. Después podrás elegir tu plan.", await GetHtmlAsync(client, "/app/plan"));
    }

    // --- Primer cobro -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_primer_pago_de_QR_por_webhook_empieza_la_prueba_con_su_PaidAt()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        var paidAt = DateTimeOffset.UtcNow.AddMinutes(-7);

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(sale, paidAt: paidAt))));

        var subscription = await AssertTrialAsync(sale.MerchantId, paidAt);
        var html = await GetHtmlAsync(client, "/app");
        Assert.Contains("data-plan-tier=\"Trial\"", html);
        Assert.Contains("Prueba: te quedan 15 días", html);
        Assert.DoesNotContain("data-trial-first-charge", html);

        // Otro cobro pagado no cambia la prueba.
        var second = await ChargeAsync(client, "80.00");
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(second))));
        var again = await SubscriptionOfAsync(sale.MerchantId);
        Assert.Equal(subscription.TrialStartedAt, again!.TrialStartedAt);
        Assert.Single(again.Periods);
    }

    [Fact]
    public async Task El_primer_pago_de_un_cobro_por_celular_empieza_la_prueba()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        var paidAt = DateTimeOffset.UtcNow.AddMinutes(-2);

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(PushPaymentBodyFor(sale, paidAt: paidAt))));

        await AssertTrialAsync(sale.MerchantId, paidAt);
    }

    [Fact]
    public async Task El_primer_pago_que_encuentra_la_conciliacion_empieza_la_prueba_con_su_PaidAt()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        var paidAt = DateTimeOffset.UtcNow.AddMinutes(-40);
        var external = SaleMetadataFor(sale).ToDictionary(p => p.Key, p => (string?)p.Value);
        Factory.Gateway.CodiQrTransactions[sale.PeyQrId!] = [FakePaymentGateway.Transaction(NewTransactionId(), sale.Amount, external, paidAt: paidAt)];

        using (var scope = Factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IPaymentReconciliation>().ReconcileSaleAsync(sale.Id);
            Assert.Equal(1, Assert.IsType<PaymentReconciliationResult.Checked>(result).Recorded);
        }

        // Una conciliación tardía no le quita minutos: empieza cuando le pagaron, no cuando se registró.
        await AssertTrialAsync(sale.MerchantId, paidAt);
    }

    [Fact]
    public async Task Dos_pagos_simultaneos_crean_una_sola_suscripcion()
    {
        var client = await ActiveMerchantAsync(Email);
        var uno = await ChargeAsync(client, "100.00");
        var dos = await ChargeAsync(client, "200.00");

        var responses = await Task.WhenAll(
            PostWebhookAsync(QrPaymentBodyFor(uno)),
            PostWebhookAsync(QrPaymentBodyFor(dos)));

        foreach (var response in responses)
        {
            Assert.Equal("Processed", await WebhookStatusAsync(response));
        }

        var subscriptions = await WithDbAsync(db => db.Subscriptions.AsNoTracking().Include(s => s.Periods)
            .Where(s => s.MerchantId == uno.MerchantId).ToListAsync());
        var trial = Assert.Single(Assert.Single(subscriptions).Periods);
        Assert.Equal(SubscriptionPeriodKind.Trial, trial.Kind);
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(uno.Id)).Status);
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(dos.Id)).Status);
    }

    [Fact]
    public async Task Un_vendedor_que_ya_tuvo_su_prueba_no_recibe_otra_al_cobrar()
    {
        var client = await ActiveMerchantAsync(Email);
        var merchantId = await MerchantIdAsync(Email);
        var oldTrial = DateTimeOffset.UtcNow.AddDays(-90);
        await WithDbAsync(async db =>
        {
            db.Subscriptions.Add(Subscription.StartTrial(merchantId, oldTrial, 30));
            await db.SaveChangesAsync();
        });
        var sale = await ChargeAsync(client);

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(sale))));

        var subscription = await SubscriptionOfAsync(merchantId);
        Assert.Equal(oldTrial, subscription!.TrialStartedAt);
        Assert.Single(subscription.Periods);
    }

    // --- Falla y respaldo -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Una_falla_del_disparador_no_tumba_el_webhook_y_el_job_diario_empieza_la_prueba()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        var paidAt = DateTimeOffset.UtcNow.AddHours(-3);
        Factory.TrialFaults.FailWith = new InvalidOperationException("Falla simulada de la prueba.");

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(sale, paidAt: paidAt))));

        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
        Assert.Equal((sale.MerchantId, sale.Id), Assert.Single(Factory.Notifier.Notifications));
        Assert.Null(await SubscriptionOfAsync(sale.MerchantId));

        Factory.TrialFaults.Reset();
        Assert.Equal(1, (await RunDailyJobAsync()).TrialsStarted);
        await AssertTrialAsync(sale.MerchantId, paidAt);

        Assert.Equal(0, (await RunDailyJobAsync()).TrialsStarted);
        Assert.Single((await SubscriptionOfAsync(sale.MerchantId))!.Periods);
    }

    [Fact]
    public async Task El_respaldo_usa_el_primer_pago_y_solo_toma_vendedores_activos_que_cobraron_sin_suscripcion()
    {
        await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        await ActiveMerchantAsync("suspendido@prueba.mx", status: MerchantStatus.Suspended);
        await ActiveMerchantAsync("sin-cobros@prueba.mx");
        await ActiveMerchantAsync("con-prueba@prueba.mx");
        var first = DateTimeOffset.UtcNow.AddDays(-2);

        // Pagos insertados a mano: el disparador de PaymentRecorder no corrió.
        var uno = await InsertSaleAsync(Email, 100m, first.AddMinutes(-5));
        await AddPaymentAsync(uno, uno.Amount, first.AddHours(5));
        var otra = await InsertSaleAsync(Email, 100m, first.AddMinutes(-5));
        await AddPaymentAsync(otra, otra.Amount, first);
        var suspendido = await InsertSaleAsync("suspendido@prueba.mx", 100m, first.AddMinutes(-5));
        await AddPaymentAsync(suspendido, suspendido.Amount, first);
        var conPrueba = await InsertSaleAsync("con-prueba@prueba.mx", 100m, first.AddMinutes(-5));
        await AddPaymentAsync(conPrueba, conPrueba.Amount, first);
        var conPruebaId = await MerchantIdAsync("con-prueba@prueba.mx");
        var oldTrial = DateTimeOffset.UtcNow.AddDays(-30);
        await WithDbAsync(async db =>
        {
            db.Subscriptions.Add(Subscription.StartTrial(conPruebaId, oldTrial, 15));
            await db.SaveChangesAsync();
        });

        Assert.Equal(1, (await RunDailyJobAsync()).TrialsStarted);

        await AssertTrialAsync(await MerchantIdAsync(Email), first);
        Assert.Null(await SubscriptionOfAsync(await MerchantIdAsync(OtherEmail)));
        Assert.Null(await SubscriptionOfAsync(await MerchantIdAsync("suspendido@prueba.mx")));
        Assert.Null(await SubscriptionOfAsync(await MerchantIdAsync("sin-cobros@prueba.mx")));
        Assert.Equal(oldTrial, (await SubscriptionOfAsync(conPruebaId))!.TrialStartedAt);
    }

    // --- Aislamiento ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cada_vendedor_ve_solo_su_suscripcion_y_su_plan()
    {
        await CreateMerchantAsync(Email);
        await CreateMerchantAsync(OtherEmail);
        var uno = await MerchantIdAsync(Email);
        var dos = await MerchantIdAsync(OtherEmail);
        var now = DateTimeOffset.UtcNow;
        await WithDbAsync(async db =>
        {
            db.Subscriptions.Add(Subscription.StartTrial(uno, now, 30));
            db.Subscriptions.Add(Subscription.StartTrial(dos, now.AddDays(-40), 30)); // ya terminó
            await db.SaveChangesAsync();
        });

        using var scope = Factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        await using (var asUno = new AppDbContext(options, new FixedTenant(uno)))
        {
            Assert.Equal(uno, Assert.Single(await asUno.Subscriptions.ToListAsync()).MerchantId);
            Assert.Equal(uno, Assert.Single(await asUno.SubscriptionPeriods.ToListAsync()).MerchantId);
        }

        var plans = scope.ServiceProvider.GetRequiredService<IPlanAccess>();
        Assert.Equal(PlanTier.Trial, (await plans.GetForMerchantAsync(uno)).Tier);
        Assert.Equal(PlanTier.Expired, (await plans.GetForMerchantAsync(dos)).Tier);
        Assert.False((await plans.GetForMerchantAsync(dos)).Entitlements.CanChargeWithCart);
        Assert.Equal(PlanTier.Onboarding, (await plans.GetForMerchantAsync(-1)).Tier);
        // Sin vendedor en sesión (aquí, el contexto del sistema) no hay plan "actual".
        await Assert.ThrowsAsync<InvalidOperationException>(() => plans.GetCurrentAsync());
    }

    // --- Ayudas -----------------------------------------------------------------------------------------------------

    /// <summary>Vendedor con su cuenta en revisión en Pey.</summary>
    private async Task<int> InReviewAsync(string email, string peyId)
    {
        await CreateMerchantAsync(email);
        return await WithDbAsync(async db =>
        {
            var merchant = await MerchantOfAsync(db, email);
            merchant.Status = MerchantStatus.BankAccountInReview;
            db.BankAccounts.Add(BankAccount.Submit(merchant.Id, "Ramón Orraca Lascurain", TestClabe, "Puesto", PeyEntity.Person,
                peyId, PeyCodiStatus.Processing, DateTimeOffset.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
            return merchant.Id;
        });
    }

    private async Task SyncAsync(int bankAccountId)
    {
        using var scope = Factory.Services.CreateScope();
        Assert.IsType<BankAccountStatusSyncResult.Synced>(
            await scope.ServiceProvider.GetRequiredService<IBankAccountStatusSync>().SyncAsync(bankAccountId));
    }

    private async Task<SubscriptionDailyRunResult> RunDailyJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SubscriptionDailyJob>().RunAsync(CancellationToken.None);
    }

    /// <summary>Prueba de 15 días que empieza en el <c>PaidAt</c> del pago (Pey lo manda en segundos).</summary>
    private async Task<Subscription> AssertTrialAsync(int merchantId, DateTimeOffset paidAt)
    {
        var subscription = await SubscriptionOfAsync(merchantId);
        Assert.NotNull(subscription);
        var trial = Assert.Single(subscription.Periods);
        Assert.Equal(SubscriptionPeriodKind.Trial, trial.Kind);
        Assert.Equal(SubscriptionPlan.Pro, trial.Plan);
        Assert.Equal(paidAt.ToUnixTimeSeconds(), trial.StartsAt.ToUnixTimeSeconds());
        Assert.Equal(MexicoTime.AddDays(trial.StartsAt, SystemSettings.DefaultTrialDays), trial.EndsAt);
        Assert.Equal(trial.StartsAt, subscription.TrialStartedAt);
        Assert.Equal(trial.EndsAt, subscription.ProUntil);
        Assert.Equal(SubscriptionPeriodKind.Trial, subscription.LastPeriodKind);
        return subscription;
    }

    private Task<Subscription?> SubscriptionOfAsync(int merchantId) => WithDbAsync(db =>
        db.Subscriptions.AsNoTracking().Include(s => s.Periods).SingleOrDefaultAsync(s => s.MerchantId == merchantId));

    private static string BankAccountUpdatedBody(string peyBankAccountId) => JsonSerializer.Serialize(new
    {
        type = PeyWebhookEventTypes.BankAccountUpdated,
        bankAccountId = peyBankAccountId,
        name = "Ramón Orraca Lascurain",
        ownerType = "API_KEY",
        ownerId = "01M3QMMR6X9CBVDY1SF3G8QWNW",
        codiStatus = "ACTIVE",
        clabe = TestClabe,
        entity = "PERSON",
    });

    private sealed class FixedTenant(int merchantId) : ITenantContext
    {
        public bool IsMerchantScoped => true;

        public int? MerchantId => merchantId;
    }
}
