using System.Net;
using System.Text.RegularExpressions;
using Local133.Application.Auth;
using Local133.Domain.Audit;
using Local133.Domain.Merchants;
using Local133.Domain.Notifications;
using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Admin;

/// <summary>
/// 6.2: pestaña Plan del admin. Periodos, cargos, referidos y bitácora; regalar días, abonar a mano un cargo, anular un
/// premio y dar los premios pendientes. Cada acción queda en <c>AuditLogs</c>.
/// </summary>
public partial class CustomerPlanTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    private const string AdminEmail = "admin@prueba.mx";

    private static string PlanTab(int id) => $"/admin/clientes/{id}/plan";

    // --- Acceso ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_vendedor_no_puede_usar_las_acciones_del_plan()
    {
        var client = await TrialMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);

        foreach (var url in new[]
                 {
                     $"{PlanTab(id)}/regalar-dias", $"{PlanTab(id)}/cargos/1/abonar", $"{PlanTab(id)}/referidos/1/anular",
                     $"{PlanTab(id)}/referidos/premios-pendientes",
                 })
        {
            var response = await PostFormAsync(client, "/app", new Dictionary<string, string>
            {
                ["Grant.Days"] = "10", ["Grant.Note"] = "x", ["Confirm.Note"] = "x",
            }, postUrl: url);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Identity/Account/AccessDenied", Location(response));
        }

        Assert.Empty(await WithDbAsync(db => db.AuditLogs.ToListAsync()));
        Assert.Single((await LoadSubscriptionAsync(Email)).Periods);
    }

    [Fact]
    public async Task Cada_formulario_de_la_pestana_trae_su_token_antiforgery()
    {
        var merchant = await TrialMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);
        await SeedPartialChargeAsync(merchant);
        await CreateMerchantAsync(OtherEmail);
        await LinkQualifiedReferralAsync(Email, OtherEmail, rewarded: false);

        var html = await GetHtmlAsync(await AdminAsync(), PlanTab(id));

        var forms = FormRegex().Matches(html);
        Assert.Equal(4, forms.Count); // regalar días, dar pendientes, abonar y anular
        Assert.All(forms, f => Assert.Contains("__RequestVerificationToken", f.Value));
    }

    // --- Lectura -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Muestra_periodos_cargos_referidos_y_quien_lo_invito()
    {
        var merchant = await TrialMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);
        var charge = await SeedPartialChargeAsync(merchant);
        await CreateMerchantAsync(OtherEmail);
        await LinkQualifiedReferralAsync(OtherEmail, Email, rewarded: false);

        var html = await GetHtmlAsync(await AdminAsync(), PlanTab(id));

        Assert.Contains("data-period-kind=\"Trial\"", html);
        Assert.Contains($"data-plan-charge=\"{charge.Id}\" data-charge-status=\"Partial\"", html);
        Assert.Contains($"data-credit-charge=\"{charge.Id}\"", html);
        Assert.Contains("TX-PARCIAL", html);
        Assert.Contains("data-referred-by", html);
        Assert.Contains("Lo invitó", html);
        Assert.DoesNotContain("data-plan-coming-soon", html);
    }

    [Fact]
    public async Task En_el_onboarding_no_hay_formulario_de_regalar_dias()
    {
        await CreateMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);

        var html = await GetHtmlAsync(await AdminAsync(), PlanTab(id));

        Assert.DoesNotContain("data-grant-days-form", html);
        Assert.Contains("La prueba de Pro empieza cuando su cuenta bancaria quede activa", html);
    }

    // --- Regalar días ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Regalar_dias_encadena_un_periodo_avisa_y_queda_en_la_bitacora()
    {
        await TrialMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);
        var before = (await LoadSubscriptionAsync(Email)).ProUntil;
        var admin = await AdminAsync();

        var response = await GrantDaysAsync(admin, id, days: "10", note: "Compensación por la falla", notify: true);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(PlanTab(id), Location(response));
        var subscription = await LoadSubscriptionAsync(Email);
        var grant = Assert.Single(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.AdminGrant);
        Assert.Equal(before, grant.StartsAt);
        Assert.Equal(grant.EndsAt, subscription.ProUntil);
        Assert.Equal(TimeSpan.FromDays(10), grant.EndsAt - grant.StartsAt);

        var notification = Assert.Single(await NotificationsAsync(Email));
        Assert.Equal(NotificationType.ProDaysGranted, notification.Type);
        Assert.Equal($"grant:{grant.Id}", notification.DedupKey);
        var email = Factory.Emails.Last(Email);
        Assert.Equal("Te regalamos 10 días de Pro · Local133", email.Subject);
        Assert.DoesNotContain("Compensación", email.Html); // el motivo no le llega al vendedor

        var audit = Assert.Single(await AuditAsync());
        Assert.Equal(AuditAction.ProDaysGranted, audit.Action);
        Assert.Equal(id, audit.MerchantId);
        Assert.Equal(AdminEmail, audit.ActorEmail);
        Assert.Equal("Compensación por la falla", audit.Note);

        var html = await GetHtmlAsync(admin, PlanTab(id));
        Assert.Contains("data-plan-notice", html);
        Assert.Contains("data-period-kind=\"AdminGrant\"", html);
        Assert.Contains("data-audit-entry=\"ProDaysGranted\"", html);
    }

    [Fact]
    public async Task Sin_la_casilla_no_se_avisa_al_vendedor()
    {
        await TrialMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);

        var response = await GrantDaysAsync(await AdminAsync(), id, days: "3", note: "Prueba interna", notify: false);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Single((await LoadSubscriptionAsync(Email)).Periods, p => p.Kind == SubscriptionPeriodKind.AdminGrant);
        Assert.Empty(await NotificationsAsync(Email));
        Assert.DoesNotContain(Factory.Emails.Sent, e => e.To == Email && e.Subject.StartsWith("Te regalamos", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0", "Motivo")]
    [InlineData("366", "Motivo")]
    [InlineData("10", "")]
    public async Task Regalar_dias_valida_los_dias_y_el_motivo(string days, string note)
    {
        await TrialMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);

        var response = await GrantDaysAsync(await AdminAsync(), id, days, note, notify: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("field-validation-error", html);
        Assert.Single((await LoadSubscriptionAsync(Email)).Periods);
        Assert.Empty(await AuditAsync());
    }

    [Fact]
    public async Task Regalar_dias_trae_la_validacion_en_cliente()
    {
        await TrialMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);

        var html = await GetHtmlAsync(await AdminAsync(), PlanTab(id));

        Assert.Contains("data-val-range=\"Regala entre 1 y 365 días.\"", html);
        Assert.Contains("data-val-required=\"Escribe el motivo.\"", html);
    }

    [Fact]
    public async Task No_se_regalan_dias_sin_prueba()
    {
        await CreateMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);

        var admin = await AdminAsync();
        var response = await GrantDaysAsync(admin, id, "10", "Motivo", notify: true);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Su prueba todavía no empieza", WebUtility.HtmlDecode(await GetHtmlAsync(admin, PlanTab(id))));
        Assert.Empty(await AuditAsync());
    }

    [Fact]
    public async Task Vendedor_inexistente_responde_404()
    {
        var response = await GrantDaysAsync(await AdminAsync(), 999_999, "10", "Motivo", notify: false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Abonar a mano -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Abonar_a_mano_un_cargo_incompleto_cuenta_como_un_pago_de_Pey()
    {
        var merchant = await TrialMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);
        var charge = await SeedPartialChargeAsync(merchant);
        await CreateMerchantWithTrialAsync(OtherEmail);
        await LinkReferralAsync(OtherEmail, Email); // Email fue invitado por OtherEmail
        var referrerBefore = (await LoadSubscriptionAsync(OtherEmail)).ProUntil;
        var admin = await AdminAsync();

        var response = await PostConfirmAsync(admin, id, $"{PlanTab(id)}/cargos/{charge.Id}/abonar", "Pago visto en el portal de Pey");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var credited = await LoadChargeAsync(charge.Id);
        Assert.Equal(SubscriptionChargeStatus.Paid, credited.Status);
        Assert.NotNull(credited.CreditedManuallyAt);
        var subscription = await LoadSubscriptionAsync(Email);
        var paid = Assert.Single(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.Paid);
        Assert.Equal(charge.Id, paid.SubscriptionChargeId);
        Assert.True(subscription.IsFounder); // el cargo salió a precio de fundador
        Assert.Equal("Recibimos tu pago de Pro · Local133", Factory.Emails.Last(Email).Subject);

        // Primer pago del referido ⇒ quien lo invitó gana sus días.
        Assert.True((await LoadSubscriptionAsync(OtherEmail)).ProUntil > referrerBefore);

        var audit = Assert.Single(await AuditAsync());
        Assert.Equal(AuditAction.ChargeCreditedManually, audit.Action);
        Assert.Equal("Pago visto en el portal de Pey", audit.Note);
        Assert.Contains("data-charge-manual", await GetHtmlAsync(admin, PlanTab(id)));
    }

    [Fact]
    public async Task Un_cargo_pagado_no_se_vuelve_a_abonar()
    {
        var merchant = await TrialMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);
        var charge = await SeedPartialChargeAsync(merchant);
        var admin = await AdminAsync();
        await PostConfirmAsync(admin, id, $"{PlanTab(id)}/cargos/{charge.Id}/abonar", "Primero");

        var again = await PostConfirmAsync(admin, id, $"{PlanTab(id)}/cargos/{charge.Id}/abonar", "Otra vez", tokenFrom: "/admin/clientes");

        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Contains("ya está pagado", await GetHtmlAsync(admin, PlanTab(id)));
        Assert.Single((await LoadSubscriptionAsync(Email)).Periods, p => p.Kind == SubscriptionPeriodKind.Paid);
        Assert.Single(await AuditAsync());
    }

    [Fact]
    public async Task El_cargo_de_otro_vendedor_responde_404()
    {
        var merchant = await TrialMerchantAsync(Email);
        var charge = await SeedPartialChargeAsync(merchant);
        await CreateMerchantAsync(OtherEmail);
        var otherId = await MerchantIdAsync(OtherEmail);

        var response = await PostConfirmAsync(await AdminAsync(), otherId, $"{PlanTab(otherId)}/cargos/{charge.Id}/abonar", "Motivo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SubscriptionChargeStatus.Partial, (await LoadChargeAsync(charge.Id)).Status);
    }

    [Fact]
    public async Task Abonar_sin_motivo_no_hace_nada()
    {
        var merchant = await TrialMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);
        var charge = await SeedPartialChargeAsync(merchant);
        var admin = await AdminAsync();

        var response = await PostConfirmAsync(admin, id, $"{PlanTab(id)}/cargos/{charge.Id}/abonar", "   ");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("data-plan-error", await GetHtmlAsync(admin, PlanTab(id)));
        Assert.Equal(SubscriptionChargeStatus.Partial, (await LoadChargeAsync(charge.Id)).Status);
    }

    // --- Anular un premio --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Anular_un_premio_que_no_ha_empezado_lo_quita_y_recorre_lo_que_sigue()
    {
        await CreateMerchantWithTrialAsync(Email); // quien invita
        var id = await MerchantIdAsync(Email);
        await CreateMerchantAsync(OtherEmail);
        var referralId = await LinkQualifiedReferralAsync(Email, OtherEmail, rewarded: true);
        var trialEnd = (await LoadSubscriptionAsync(Email)).Periods.Single(p => p.Kind == SubscriptionPeriodKind.Trial).EndsAt;
        await GrantDaysAsync(await AdminAsync(), id, "5", "Regalo después del premio", notify: false);
        var admin = await AdminAsync();

        var response = await PostConfirmAsync(admin, id, $"{PlanTab(id)}/referidos/{referralId}/anular", "Se invitó a sí mismo");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var subscription = await LoadSubscriptionAsync(Email);
        Assert.DoesNotContain(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.ReferralReward);
        var grant = Assert.Single(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.AdminGrant);
        Assert.Equal(trialEnd, grant.StartsAt); // sin hueco
        Assert.Equal(grant.EndsAt, subscription.ProUntil);
        var referral = await LoadReferralAsync(OtherEmail);
        Assert.NotNull(referral.RevokedAt);

        Assert.Contains(await AuditAsync(), a => a.Action == AuditAction.ReferralRewardRevoked && a.Note == "Se invitó a sí mismo");
        var html = await GetHtmlAsync(admin, PlanTab(id));
        Assert.Contains("data-referral-state=\"Revoked\"", html);
        Assert.DoesNotContain($"data-revoke-referral=\"{referralId}\"", html);
        Assert.Contains("0 de 12", html); // un premio anulado no cuenta para el tope
    }

    [Fact]
    public async Task Un_premio_pendiente_anulado_ya_no_se_da()
    {
        await CreateMerchantWithTrialAsync(Email);
        var id = await MerchantIdAsync(Email);
        await CreateMerchantAsync(OtherEmail);
        var referralId = await LinkQualifiedReferralAsync(Email, OtherEmail, rewarded: false);
        var admin = await AdminAsync();

        await PostConfirmAsync(admin, id, $"{PlanTab(id)}/referidos/{referralId}/anular", "Fraude");
        var pending = await PostFormAsync(admin, PlanTab(id), new Dictionary<string, string>(), $"{PlanTab(id)}/referidos/premios-pendientes");

        Assert.Equal(HttpStatusCode.Redirect, pending.StatusCode);
        Assert.Contains("No tiene premios pendientes", await GetHtmlAsync(admin, PlanTab(id)));
        Assert.DoesNotContain((await LoadSubscriptionAsync(Email)).Periods, p => p.Kind == SubscriptionPeriodKind.ReferralReward);
        Assert.Null((await LoadReferralAsync(OtherEmail)).RewardedAt);
    }

    [Fact]
    public async Task Dar_premios_pendientes_arregla_el_premio_que_se_quedo_sin_dar()
    {
        await CreateMerchantWithTrialAsync(Email);
        var id = await MerchantIdAsync(Email);
        await CreateMerchantAsync(OtherEmail);
        await LinkQualifiedReferralAsync(Email, OtherEmail, rewarded: false);
        var before = (await LoadSubscriptionAsync(Email)).ProUntil;
        var admin = await AdminAsync();

        var response = await PostFormAsync(admin, PlanTab(id), new Dictionary<string, string>(), $"{PlanTab(id)}/referidos/premios-pendientes");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var subscription = await LoadSubscriptionAsync(Email);
        var reward = Assert.Single(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.ReferralReward);
        Assert.Equal(before, reward.StartsAt);
        Assert.NotNull((await LoadReferralAsync(OtherEmail)).RewardedAt);
        Assert.Contains(await AuditAsync(), a => a.Action == AuditAction.PendingReferralRewardsGranted);
    }

    [Fact]
    public async Task El_referido_de_otro_vendedor_responde_404()
    {
        await CreateMerchantWithTrialAsync(Email);
        await CreateMerchantAsync(OtherEmail);
        var otherId = await MerchantIdAsync(OtherEmail);
        var referralId = await LinkQualifiedReferralAsync(Email, OtherEmail, rewarded: true);

        // La ruta es la del referido (OtherEmail), no la de quien invitó.
        var response = await PostConfirmAsync(await AdminAsync(), otherId, $"{PlanTab(otherId)}/referidos/{referralId}/anular", "Motivo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null((await LoadReferralAsync(OtherEmail)).RevokedAt);
    }

    // --- Ayudas ------------------------------------------------------------------------------------------------------

    private async Task<HttpClient> AdminAsync()
    {
        if (!await WithDbAsync(db => db.Users.AnyAsync(u => u.Email == AdminEmail)))
        {
            await CreateConfirmedUserAsync(AdminEmail, Roles.Admin);
        }

        var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, AdminEmail)).StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> GrantDaysAsync(HttpClient admin, int id, string days, string note, bool notify)
    {
        // Como el navegador: la casilla manda «true» si está marcada y el campo oculto siempre manda «false».
        var fields = new Dictionary<string, string>
        {
            ["Grant.Days"] = days, ["Grant.Note"] = note, ["Grant.Notify"] = notify ? "true" : "false",
        };

        // El token se toma del listado: la pestaña de un vendedor que no existe responde 404.
        return PostFormAsync(admin, "/admin/clientes", fields, $"{PlanTab(id)}/regalar-dias");
    }

    private static Task<HttpResponseMessage> PostConfirmAsync(
        HttpClient admin, int id, string url, string note, string? tokenFrom = null) =>
        PostFormAsync(admin, tokenFrom ?? "/admin/clientes", new Dictionary<string, string> { ["Confirm.Note"] = note }, url);

    /// <summary>Cargo de 1 mes a precio de fundador, con un pago incompleto que reportó Pey.</summary>
    private async Task<SubscriptionCharge> SeedPartialChargeAsync(HttpClient merchant)
    {
        var charge = await CreateChargeAsync(merchant);
        await WithDbAsync(async db =>
        {
            var tracked = await db.SubscriptionCharges.IgnoreQueryFilters().SingleAsync(c => c.Id == charge.Id);
            tracked.MarkPartial();
            tracked.Payments.Add(SubscriptionPayment.Record(
                tracked, "TX-PARCIAL", "CODI", 50m, 50m, null, null,
                DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });
        return await LoadChargeAsync(charge.Id);
    }

    private async Task CreateMerchantWithTrialAsync(string email)
    {
        await CreateMerchantAsync(email);
        await UpdateMerchantAsync(email, m => m.Status = MerchantStatus.Active);
        await SeedTrialAsync(email);
    }

    private Task LinkReferralAsync(string referrerEmail, string referredEmail) => WithDbAsync(async db =>
    {
        var referrer = await MerchantOfAsync(db, referrerEmail);
        var referred = await MerchantOfAsync(db, referredEmail);
        db.Referrals.Add(Referral.Create(referrer.Id, referred.Id, referrer.ReferralCode, DateTimeOffset.UtcNow.AddDays(-10)));
        await db.SaveChangesAsync();
    });

    /// <summary>
    /// Referido que ya pagó. Con <paramref name="rewarded"/>, quien invita ya tiene su premio (periodo encadenado al
    /// final de su plan, como lo da <c>ReferralRewarder</c>); sin él, el premio quedó pendiente.
    /// </summary>
    private Task<int> LinkQualifiedReferralAsync(string referrerEmail, string referredEmail, bool rewarded) => WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        var referrer = await MerchantOfAsync(db, referrerEmail);
        var referred = await MerchantOfAsync(db, referredEmail);
        var referral = Referral.Create(referrer.Id, referred.Id, referrer.ReferralCode, now.AddDays(-10));
        referral.Qualify(now.AddDays(-1));
        db.Referrals.Add(referral);
        await db.SaveChangesAsync();
        if (rewarded)
        {
            var subscription = await db.Subscriptions.IgnoreQueryFilters().Include(s => s.Periods)
                .SingleAsync(s => s.MerchantId == referrer.Id);
            subscription.AddDaysPeriod(SubscriptionPeriodKind.ReferralReward, 10, now, graceDays: 5, referral.Id);
            referral.MarkRewarded(now.AddDays(-1));
            await db.SaveChangesAsync();
        }

        return referral.Id;
    });

    private Task<Referral> LoadReferralAsync(string referredEmail) => WithDbAsync(async db =>
    {
        var referred = await MerchantOfAsync(db, referredEmail);
        return await db.Referrals.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.ReferredMerchantId == referred.Id);
    });

    private Task<List<Notification>> NotificationsAsync(string email) => WithDbAsync(async db =>
    {
        var merchant = await MerchantOfAsync(db, email);
        return await db.Notifications.IgnoreQueryFilters().AsNoTracking().Where(n => n.MerchantId == merchant.Id).ToListAsync();
    });

    private Task<List<AuditLog>> AuditAsync() => WithDbAsync(db => db.AuditLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync());

    [GeneratedRegex("<form\\b[^>]*action=\"[^\"]*/plan/[^\"]*\"[^>]*>.*?</form>", RegexOptions.Singleline)]
    private static partial Regex FormRegex();
}
