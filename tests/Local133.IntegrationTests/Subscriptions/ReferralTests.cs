using System.Net;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Notifications;
using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.8: descuento de bienvenida del referido en su primer pago y premio de quien lo invitó (10 días, hasta 12 en 12
/// meses), con el webhook firmado de los links. También «Invita y gana» (/app/invita).
/// </summary>
public class ReferralTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    // Email = el referido (paga); OtherEmail = quien invita.
    private const string InviteUrl = "/app/invita";

    // --- Descuento ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_referido_tiene_descuento_en_su_primer_pago_y_despues_ya_no()
    {
        await CreateMerchantAsync(OtherEmail);
        var client = await TrialMerchantAsync(Email);
        await LinkReferralAsync(OtherEmail, Email);

        var plan = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));
        Assert.Contains("data-welcome-discount", plan);
        Assert.Contains("$44.70 de descuento", plan); // 30 % de $149 (precio de fundador)
        Assert.Contains("data-plan-offer-list-price", plan);

        var first = await CreateChargeAsync(client);
        Assert.Equal(149m, first.ListPrice);
        Assert.Equal(44.70m, first.DiscountAmount);
        Assert.Equal(SubscriptionDiscountReason.ReferralWelcome, first.DiscountReason);
        Assert.Equal(104.30m, first.Amount);
        Assert.Equal(104.30m, Factory.Gateway.PaymentLinkRequests.Last().Amount);

        await PostWebhookAsync(LinkWebhookBody(first, "paid", (NewTransactionId(), first.Amount)));

        var second = await CreateChargeAsync(client);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(0m, second.DiscountAmount);
        Assert.Null(second.DiscountReason);
        Assert.Equal(149m, second.Amount);
        Assert.DoesNotContain("data-welcome-discount", await GetHtmlAsync(client, PlanUrl));
    }

    [Fact]
    public async Task Sin_codigo_no_hay_descuento()
    {
        var client = await TrialMerchantAsync(Email);

        var charge = await CreateChargeAsync(client);

        Assert.Equal(0m, charge.DiscountAmount);
        Assert.DoesNotContain("data-welcome-discount", await GetHtmlAsync(client, PlanUrl));
    }

    [Fact]
    public async Task Un_pago_incompleto_no_califica_y_conserva_el_descuento()
    {
        var referrer = await CreateMerchantWithTrialAsync(OtherEmail);
        var client = await TrialMerchantAsync(Email);
        await LinkReferralAsync(OtherEmail, Email);
        var charge = await CreateChargeAsync(client);

        await PostWebhookAsync(LinkWebhookBody(charge, "partial", (NewTransactionId(), 50m)));

        var referral = await LoadReferralAsync();
        Assert.Null(referral.QualifiedAt);
        Assert.DoesNotContain((await LoadSubscriptionAsync(OtherEmail)).Periods, p => p.Kind == SubscriptionPeriodKind.ReferralReward);
        Assert.Equal(referrer, (await LoadSubscriptionAsync(OtherEmail)).ProUntil);
        Assert.Contains("data-welcome-discount", await GetHtmlAsync(client, PlanUrl));
    }

    // --- Premio ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_primer_pago_del_referido_da_10_dias_a_quien_invito_una_sola_vez()
    {
        var referrerTrialEnd = await CreateMerchantWithTrialAsync(OtherEmail);
        var client = await TrialMerchantAsync(Email);
        await LinkReferralAsync(OtherEmail, Email);
        var charge = await CreateChargeAsync(client);
        var body = LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount));

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(body)));
        await PostWebhookAsync(body); // otra entrega del mismo pago

        var referral = await LoadReferralAsync();
        Assert.NotNull(referral.QualifiedAt);
        Assert.NotNull(referral.RewardedAt);
        Assert.Null(referral.SkippedReason);

        var subscription = await LoadSubscriptionAsync(OtherEmail);
        var reward = Assert.Single(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.ReferralReward);
        Assert.Equal(referral.Id, reward.ReferralId);
        Assert.Equal(referrerTrialEnd, reward.StartsAt); // se encadena al final de su prueba
        Assert.Equal(MexicoTime.AddDays(referrerTrialEnd, 10), reward.EndsAt);
        Assert.Equal(reward.EndsAt, subscription.ProUntil);

        var notification = Assert.Single(await NotificationsAsync(OtherEmail));
        Assert.Equal(NotificationType.ReferralRewardGranted, notification.Type);
        Assert.Equal("/app/invita", notification.Url);
        var email = Factory.Emails.Last(OtherEmail);
        Assert.Equal("Ganaste 10 días de Pro · Local133", email.Subject);
        Assert.Contains("María pagó su plan con tu código", WebUtility.HtmlDecode(email.Html));

        // Un segundo pago del referido ya no premia.
        var second = await CreateChargeAsync(client);
        await PostWebhookAsync(LinkWebhookBody(second, "paid", (NewTransactionId(), second.Amount)));
        Assert.Single((await LoadSubscriptionAsync(OtherEmail)).Periods, p => p.Kind == SubscriptionPeriodKind.ReferralReward);
        Assert.Single(await NotificationsAsync(OtherEmail));
    }

    [Fact]
    public async Task Quien_invita_desde_el_gratuito_gana_Pro_desde_ese_momento()
    {
        await CreateMerchantAsync(OtherEmail);
        await SeedFreePlanAsync(OtherEmail);
        var client = await TrialMerchantAsync(Email);
        await LinkReferralAsync(OtherEmail, Email);
        var charge = await CreateChargeAsync(client);
        var before = DateTimeOffset.UtcNow;

        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));

        var subscription = await LoadSubscriptionAsync(OtherEmail);
        var reward = Assert.Single(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.ReferralReward);
        Assert.InRange(reward.StartsAt, before.AddSeconds(-1), DateTimeOffset.UtcNow);
        Assert.Equal(PlanTier.Pro, subscription.StatusAt(DateTimeOffset.UtcNow, 5).Tier);
    }

    [Fact]
    public async Task Con_12_premios_en_12_meses_no_hay_premio_y_se_avisa()
    {
        var trialEnd = await CreateMerchantWithTrialAsync(OtherEmail);
        await SeedRewardedReferralsAsync(OtherEmail, Enumerable.Repeat(DateTimeOffset.UtcNow.AddDays(-30), 12));
        var client = await TrialMerchantAsync(Email);
        await LinkReferralAsync(OtherEmail, Email);
        var charge = await CreateChargeAsync(client);

        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));

        var referral = await LoadReferralAsync(Email);
        Assert.NotNull(referral.QualifiedAt);
        Assert.Null(referral.RewardedAt);
        Assert.Equal(ReferralSkipReason.RewardCap, referral.SkippedReason);
        Assert.Equal(trialEnd, (await LoadSubscriptionAsync(OtherEmail)).ProUntil);
        Assert.Equal(NotificationType.ReferralRewardSkipped, Assert.Single(await NotificationsAsync(OtherEmail)).Type);
        Assert.Contains("12 premios en los últimos 12 meses", WebUtility.HtmlDecode(Factory.Emails.Last(OtherEmail).Html));
    }

    [Fact]
    public async Task Un_premio_de_hace_mas_de_12_meses_no_cuenta_para_el_tope()
    {
        await CreateMerchantWithTrialAsync(OtherEmail);
        var now = DateTimeOffset.UtcNow;
        await SeedRewardedReferralsAsync(OtherEmail,
            Enumerable.Repeat(now.AddDays(-30), 11).Append(MexicoTime.AddMonths(now, -13)));
        var client = await TrialMerchantAsync(Email);
        await LinkReferralAsync(OtherEmail, Email);
        var charge = await CreateChargeAsync(client);

        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));

        Assert.NotNull((await LoadReferralAsync(Email)).RewardedAt);
        Assert.Single((await LoadSubscriptionAsync(OtherEmail)).Periods, p => p.Kind == SubscriptionPeriodKind.ReferralReward);
    }

    [Fact]
    public async Task Quien_invita_dado_de_baja_no_gana()
    {
        await CreateMerchantWithTrialAsync(OtherEmail);
        await UpdateMerchantAsync(OtherEmail, m => m.Status = MerchantStatus.Disabled);
        var client = await TrialMerchantAsync(Email);
        await LinkReferralAsync(OtherEmail, Email);
        var charge = await CreateChargeAsync(client);

        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));

        Assert.Equal(ReferralSkipReason.ReferrerDisabled, (await LoadReferralAsync()).SkippedReason);
        Assert.DoesNotContain((await LoadSubscriptionAsync(OtherEmail)).Periods, p => p.Kind == SubscriptionPeriodKind.ReferralReward);
    }

    [Fact]
    public async Task Quien_invita_sin_prueba_recibe_sus_dias_con_su_primer_cobro_despues_de_la_prueba()
    {
        // Quien invita ya tiene su cuenta activa, pero no ha cobrado: no tiene suscripción (5.13).
        var referrerClient = await ActiveMerchantAsync(OtherEmail);
        var client = await TrialMerchantAsync(Email);
        await LinkReferralAsync(OtherEmail, Email);
        var charge = await CreateChargeAsync(client);

        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));

        var pending = await LoadReferralAsync();
        Assert.True(pending.IsPendingReward);
        Assert.Equal(NotificationType.ReferralRewardPending, Assert.Single(await NotificationsAsync(OtherEmail)).Type);
        Assert.Contains("data-invite-onboarding", await GetHtmlAsync(referrerClient, InviteUrl));

        // Su primer cobro pagado empieza su prueba y le da el premio, encadenado después.
        var sale = await ChargeAsync(referrerClient);
        await PostWebhookAsync(QrPaymentBodyFor(sale));

        var referral = await LoadReferralAsync();
        Assert.NotNull(referral.RewardedAt);
        var subscription = await LoadSubscriptionAsync(OtherEmail);
        var trial = Assert.Single(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.Trial);
        var reward = Assert.Single(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.ReferralReward);
        Assert.Equal(trial.EndsAt, reward.StartsAt);
        Assert.Equal(SubscriptionPlan.Pro, reward.Plan);
        Assert.Equal(referral.Id, reward.ReferralId);
        Assert.Equal(reward.EndsAt, subscription.ProUntil);
        Assert.DoesNotContain("data-invite-onboarding", await GetHtmlAsync(referrerClient, InviteUrl));
    }

    // --- Invita y gana --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Invita_y_gana_muestra_el_codigo_la_liga_y_los_invitados()
    {
        await CreateMerchantWithTrialAsync(OtherEmail);
        var referrerClient = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(referrerClient, OtherEmail)).StatusCode);
        var code = await WithDbAsync(async db => (await MerchantOfAsync(db, OtherEmail)).ReferralCode);

        var empty = WebUtility.HtmlDecode(await GetHtmlAsync(referrerClient, InviteUrl));
        Assert.Contains($"data-invite-code>{code}<", empty);
        Assert.Contains($"value=\"https://localhost/r/{code}\"", empty);
        Assert.Contains("https://wa.me/?text=", empty);
        Assert.Contains("data-invite-empty", empty);

        var client = await TrialMerchantAsync(Email);
        await LinkReferralAsync(OtherEmail, Email);
        var notPaid = WebUtility.HtmlDecode(await GetHtmlAsync(referrerClient, InviteUrl));
        Assert.Contains("data-invite-referral=\"NotPaid\"", notPaid);
        Assert.Contains("María", notPaid);
        Assert.DoesNotContain(Email, notPaid); // nunca el correo del invitado

        var charge = await CreateChargeAsync(client);
        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));
        var rewarded = WebUtility.HtmlDecode(await GetHtmlAsync(referrerClient, InviteUrl));
        Assert.Contains("data-invite-referral=\"Rewarded\"", rewarded);
        Assert.Contains("Pagó · ganaste 10 días", rewarded);
        Assert.Matches(@"data-invite-days>\s*<span class=""fs-3 fw-bold"">10</span>", rewarded);
    }

    [Fact]
    public async Task Invita_y_gana_solo_muestra_los_invitados_propios()
    {
        await CreateMerchantAsync(OtherEmail);
        await TrialMerchantAsync(Email);
        await LinkReferralAsync(OtherEmail, Email);
        var stranger = await ActiveMerchantAsync("tercero@prueba.mx");

        var html = await GetHtmlAsync(stranger, InviteUrl);

        Assert.Contains("data-invite-empty", html);
        Assert.DoesNotContain("data-invite-referral=\"", html);
    }

    [Fact]
    public async Task Mas_lleva_a_Invita_y_gana()
    {
        var client = await TrialMerchantAsync(Email);

        Assert.Contains("data-more-invite", await GetHtmlAsync(client, "/app/mas"));
        Assert.Contains("data-plan-invite", await GetHtmlAsync(client, PlanUrl));
    }

    [Fact]
    public async Task Invita_y_gana_pide_sesion_de_vendedor()
    {
        var response = await CreateClient().GetAsync(InviteUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", Location(response));
    }

    // --- Ayudas ---------------------------------------------------------------------------------------------------------

    /// <summary>Vendedor (sin sesión) en su prueba; devuelve el fin de la prueba.</summary>
    private async Task<DateTimeOffset> CreateMerchantWithTrialAsync(string email)
    {
        await CreateMerchantAsync(email);
        await UpdateMerchantAsync(email, m => m.Status = MerchantStatus.Active);
        await SeedTrialAsync(email);
        return (await LoadSubscriptionAsync(email)).ProUntil;
    }

    private Task LinkReferralAsync(string referrerEmail, string referredEmail) => WithDbAsync(async db =>
    {
        var referrer = await MerchantOfAsync(db, referrerEmail);
        var referred = await MerchantOfAsync(db, referredEmail);
        db.Referrals.Add(Referral.Create(referrer.Id, referred.Id, referrer.ReferralCode, DateTimeOffset.UtcNow.AddDays(-10)));
        await db.SaveChangesAsync();
    });

    /// <summary>Referidos de <paramref name="referrerEmail"/> ya premiados en esas fechas (vendedores nuevos).</summary>
    private async Task SeedRewardedReferralsAsync(string referrerEmail, IEnumerable<DateTimeOffset> rewardedAt)
    {
        var i = 0;
        foreach (var at in rewardedAt)
        {
            var email = $"premio{i++}@prueba.mx";
            await CreateMerchantAsync(email);
            await WithDbAsync(async db =>
            {
                var referrer = await MerchantOfAsync(db, referrerEmail);
                var referred = await MerchantOfAsync(db, email);
                var referral = Referral.Create(referrer.Id, referred.Id, referrer.ReferralCode, at.AddDays(-5));
                referral.Qualify(at);
                referral.MarkRewarded(at);
                db.Referrals.Add(referral);
                await db.SaveChangesAsync();
            });
        }
    }

    private Task<Referral> LoadReferralAsync(string referredEmail = Email) => WithDbAsync(async db =>
    {
        var referred = await MerchantOfAsync(db, referredEmail);
        return await db.Referrals.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.ReferredMerchantId == referred.Id);
    });

    private Task<List<Notification>> NotificationsAsync(string email) => WithDbAsync(async db =>
    {
        var merchant = await MerchantOfAsync(db, email);
        return await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.MerchantId == merchant.Id).OrderBy(n => n.Id).ToListAsync();
    });
}
