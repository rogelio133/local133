using System.Text.Json;
using Local133.Application.BankAccounts;
using Local133.Application.Subscriptions;
using Local133.Application.Tenancy;
using Local133.Domain.BankAccounts;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.3: la prueba de Pro empieza cuando la cuenta bancaria queda activa (alta, webhook, sondeo o botón), una sola vez
/// por vendedor y en el mismo guardado que el cambio de estatus.
/// </summary>
public class TrialTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string PeyId = "01JBAVWE4NS5TGXHWGMX3KH23Q";

    [Fact]
    public async Task El_webhook_que_activa_la_cuenta_empieza_la_prueba_una_sola_vez()
    {
        var merchantId = await InReviewAsync(Email, PeyId);
        var before = DateTimeOffset.UtcNow;

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(BankAccountUpdatedBody(PeyId))));

        var subscription = await AssertTrialAsync(merchantId, before);
        // Otro aviso de la misma activación (con otro id) no da otra prueba.
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(BankAccountUpdatedBody(PeyId))));
        var again = await SubscriptionOfAsync(merchantId);
        Assert.Equal(subscription.TrialStartedAt, again!.TrialStartedAt);
        Assert.Single(again.Periods);
    }

    [Fact]
    public async Task La_consulta_del_sondeo_o_del_boton_que_la_ve_activa_empieza_la_prueba()
    {
        var merchantId = await InReviewAsync(Email, PeyId);
        var accountId = await WithDbAsync(db => db.BankAccounts.Where(b => b.MerchantId == merchantId).Select(b => b.Id).SingleAsync());
        Factory.Gateway.LookupStatus = PeyCodiStatus.Active;
        var before = DateTimeOffset.UtcNow;

        await SyncAsync(accountId);

        await AssertTrialAsync(merchantId, before);
    }

    [Theory]
    [InlineData(PeyCodiStatus.Pending)]
    [InlineData(PeyCodiStatus.Processing)]
    [InlineData(PeyCodiStatus.Error)]
    public async Task Sin_activacion_no_hay_prueba(PeyCodiStatus lookup)
    {
        var merchantId = await InReviewAsync(Email, PeyId);
        var accountId = await WithDbAsync(db => db.BankAccounts.Where(b => b.MerchantId == merchantId).Select(b => b.Id).SingleAsync());
        Factory.Gateway.LookupStatus = lookup;

        await SyncAsync(accountId);

        Assert.Null(await SubscriptionOfAsync(merchantId));
    }

    [Fact]
    public async Task El_alta_que_Pey_activa_de_inmediato_empieza_la_prueba_y_el_Inicio_la_muestra()
    {
        Factory.Gateway.RegisterStatus = PeyCodiStatus.Active;
        await CreateMerchantAsync(Email);
        var client = CreateClient();
        await LoginAsync(client, Email);
        var before = DateTimeOffset.UtcNow;

        await PostFormAsync(client, "/app/cuenta-bancaria", new Dictionary<string, string>
        {
            ["Input.Entity"] = nameof(PeyEntity.Person),
            ["Input.HolderName"] = "Ramón Orraca Lascurain",
            ["Input.Clabe"] = TestClabe,
            ["Input.Alias"] = "Tacos Don Ramón",
        });

        await AssertTrialAsync(await MerchantIdAsync(Email), before);
        var html = await GetHtmlAsync(client, "/app");
        Assert.Contains("data-plan-tier=\"Trial\"", html);
        Assert.Contains("Prueba: te quedan 15 días", html);
    }

    [Fact]
    public async Task Un_vendedor_que_ya_tuvo_su_prueba_no_recibe_otra()
    {
        var merchantId = await InReviewAsync(Email, PeyId);
        var oldTrial = DateTimeOffset.UtcNow.AddDays(-90);
        await WithDbAsync(async db =>
        {
            db.Subscriptions.Add(Subscription.StartTrial(merchantId, oldTrial, 30));
            await db.SaveChangesAsync();
        });

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(BankAccountUpdatedBody(PeyId))));

        var subscription = await SubscriptionOfAsync(merchantId);
        Assert.Equal(oldTrial, subscription!.TrialStartedAt);
        Assert.Single(subscription.Periods);
        Assert.Equal(MerchantStatus.Active, (await WithDbAsync(db => db.Merchants.AsNoTracking().SingleAsync(m => m.Id == merchantId))).Status);
    }

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

    private async Task<Subscription> AssertTrialAsync(int merchantId, DateTimeOffset before)
    {
        var subscription = await SubscriptionOfAsync(merchantId);
        Assert.NotNull(subscription);
        var trial = Assert.Single(subscription.Periods);
        Assert.Equal(SubscriptionPeriodKind.Trial, trial.Kind);
        Assert.InRange(trial.StartsAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
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
