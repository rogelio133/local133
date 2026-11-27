using Local133.Domain.Common;
using Local133.Domain.Subscriptions;
using Local133.Domain.Webhooks;
using Local133.Infrastructure.Email;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.6: webhook <c>payment_link.status_updated</c> firmado. Abona el periodo solo con <c>paid</c> y un monto que cubre
/// el cargo; idempotente por <c>webhook-id</c> y por <c>transactionId</c>; todo link pagado se abona.
/// </summary>
public class SubscriptionWebhookTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    [Fact]
    public async Task Pagado_abona_el_periodo_despues_de_la_prueba_y_avisa_por_correo()
    {
        var client = await TrialMerchantAsync(Email);
        var trialEnd = (await LoadSubscriptionAsync(Email)).ProUntil;
        var charge = await CreateChargeAsync(client);
        var transactionId = NewTransactionId();

        var status = await WebhookStatusAsync(await PostWebhookAsync(LinkWebhookBody(charge, "paid", (transactionId, 149m))));

        Assert.Equal("Processed", status);
        var paid = await LoadChargeAsync(charge.Id);
        Assert.Equal(SubscriptionChargeStatus.Paid, paid.Status);
        Assert.NotNull(paid.PaidAt);
        var payment = Assert.Single(paid.Payments);
        Assert.Equal(transactionId, payment.PeyTransactionId);
        Assert.Equal("CODI", payment.PaymentMethod);
        Assert.Equal(149m, payment.Base);

        var subscription = await LoadSubscriptionAsync(Email);
        var period = Assert.Single(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.Paid);
        Assert.Equal(trialEnd, period.StartsAt); // pagar en la prueba no la acorta
        Assert.Equal(MexicoTime.AddMonths(trialEnd, 1), period.EndsAt);
        Assert.Equal(charge.Id, period.SubscriptionChargeId);
        Assert.Equal(period.EndsAt, subscription.ProUntil);
        Assert.Equal(SubscriptionPackage.OneMonth, subscription.LastPackage);
        Assert.Equal(SubscriptionPeriodKind.Paid, subscription.LastPeriodKind);
        Assert.True(subscription.IsFounder);
        Assert.Equal(1, subscription.FounderNumber);

        var email = Factory.Emails.Last(Email);
        Assert.Equal(MerchantEmails.SubscriptionPaidSubject, email.Subject);
        Assert.Contains("fundador número 1", email.Html);
        Assert.Contains($"pedido #{charge.Id}: Pro · 1 mes", email.Html);
        Assert.Contains("Ver comprobante", email.Html);
        // El botón sigue siendo la primera liga del correo, ahora al comprobante.
        var firstLink = System.Text.RegularExpressions.Regex.Match(email.Html, "href=\"([^\"]+)\"").Groups[1].Value;
        Assert.EndsWith($"/app/plan/pedidos/{charge.Id}/comprobante", firstLink);
    }

    [Fact]
    public async Task Pagado_durante_la_prueba_las_pantallas_ya_muestran_Pro()
    {
        var client = await TrialMerchantAsync(Email);
        var trialEnd = (await LoadSubscriptionAsync(Email)).ProUntil;
        var charge = await CreateChargeAsync(client);

        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), 149m)));

        var lastTrialDay = MexicoTime.DateOf(trialEnd.AddTicks(-1));
        var trialNote = $"Lo que pagaste empieza al terminar tu prueba, el {Local133.Web.Areas.Merchant.Models.SaleFormats.Date(lastTrialDay, MexicoTime.Today(DateTimeOffset.UtcNow))}.";
        foreach (var url in new[] { "/app", "/app/plan" })
        {
            var html = System.Net.WebUtility.HtmlDecode(await GetHtmlAsync(client, url));
            Assert.Contains("data-plan-tier=\"Pro\"", html);
            Assert.Contains("Plan Pro", html);
            Assert.Contains(trialNote, html);
            Assert.DoesNotContain("Prueba: te quedan", html);
        }

        // Su Pro sigue sin interrupción: todavía no toca renovar.
        var plan = System.Net.WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/plan"));
        Assert.Contains("data-plan-action=\"extend\"", plan);
        Assert.Contains("Adquirir más días de Pro", plan);
    }

    [Fact]
    public async Task Procesando_no_abona_y_despues_pagado_si()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        var transactionId = NewTransactionId();

        var processing = await WebhookStatusAsync(await PostWebhookAsync(LinkWebhookBody(charge, "processing", (transactionId, 149m))));
        var afterProcessing = await LoadChargeAsync(charge.Id);
        var paid = await WebhookStatusAsync(await PostWebhookAsync(LinkWebhookBody(charge, "paid", (transactionId, 149m))));

        Assert.Equal("Processed", processing);
        Assert.Equal(SubscriptionChargeStatus.Processing, afterProcessing.Status);
        Assert.Single(afterProcessing.Payments);
        Assert.Equal("Processed", paid);
        var final = await LoadChargeAsync(charge.Id);
        Assert.Equal(SubscriptionChargeStatus.Paid, final.Status);
        Assert.Single(final.Payments);
        Assert.Single((await LoadSubscriptionAsync(Email)).Periods, p => p.Kind == SubscriptionPeriodKind.Paid);
    }

    [Theory]
    [InlineData("partial", 149)]
    [InlineData("paid", 100)] // monto menor
    public async Task Incompleto_o_por_menos_queda_en_revision_sin_abonar(string status, int paidBase)
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);

        var result = await WebhookStatusAsync(await PostWebhookAsync(LinkWebhookBody(charge, status, (NewTransactionId(), paidBase))));

        Assert.Equal("Processed", result);
        Assert.Equal(SubscriptionChargeStatus.Partial, (await LoadChargeAsync(charge.Id)).Status);
        Assert.DoesNotContain((await LoadSubscriptionAsync(Email)).Periods, p => p.Kind == SubscriptionPeriodKind.Paid);
        Assert.DoesNotContain(Factory.Emails.Sent, e => e.Subject == MerchantEmails.SubscriptionPaidSubject);
    }

    [Fact]
    public async Task Repetido_no_abona_dos_veces()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        var transactionId = NewTransactionId();
        var body = LinkWebhookBody(charge, "paid", (transactionId, 149m));
        var webhookId = NewWebhookId();

        var first = await WebhookStatusAsync(await PostWebhookAsync(body, webhookId));
        var sameId = await WebhookStatusAsync(await PostWebhookAsync(body, webhookId));
        var otherId = await WebhookStatusAsync(await PostWebhookAsync(body));

        Assert.Equal("Processed", first);
        Assert.Equal("Duplicate", sameId);
        Assert.Equal("Processed", otherId); // «ya abonado»
        Assert.Single((await LoadChargeAsync(charge.Id)).Payments);
        Assert.Single((await LoadSubscriptionAsync(Email)).Periods, p => p.Kind == SubscriptionPeriodKind.Paid);
        Assert.Single(Factory.Emails.Sent, e => e.Subject == MerchantEmails.SubscriptionPaidSubject);
    }

    [Fact]
    public async Task Un_link_viejo_tambien_se_abona()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        await UpdateChargeAsync(charge.Id, c => c.CreatedAt = DateTimeOffset.UtcNow.AddDays(-120));

        var status = await WebhookStatusAsync(await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), 149m))));

        Assert.Equal("Processed", status);
        Assert.Equal(SubscriptionChargeStatus.Paid, (await LoadChargeAsync(charge.Id)).Status);
    }

    [Fact]
    public async Task Dos_links_pagados_se_abonan_encadenados()
    {
        var client = await TrialMerchantAsync(Email);
        var trialEnd = (await LoadSubscriptionAsync(Email)).ProUntil;
        var monthly = await CreateChargeAsync(client);
        var annual = await CreateChargeAsync(client, SubscriptionPackage.Annual);

        await PostWebhookAsync(LinkWebhookBody(monthly, "paid", (NewTransactionId(), monthly.Amount)));
        await PostWebhookAsync(LinkWebhookBody(annual, "paid", (NewTransactionId(), annual.Amount)));

        var periods = (await LoadSubscriptionAsync(Email)).Periods
            .Where(p => p.Kind == SubscriptionPeriodKind.Paid).OrderBy(p => p.StartsAt).ToList();
        Assert.Equal(2, periods.Count);
        Assert.Equal(trialEnd, periods[0].StartsAt);
        Assert.Equal(periods[0].EndsAt, periods[1].StartsAt);
        Assert.Equal(MexicoTime.AddMonths(periods[0].EndsAt, 12), periods[1].EndsAt);
        Assert.Contains("Empieza cuando termina lo que ya tenías", Factory.Emails.Last(Email).Html);
    }

    [Fact]
    public async Task En_la_gracia_cuenta_desde_el_vencimiento()
    {
        var client = await ActiveMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        var expired = now.AddDays(-2);
        await SeedPlanAsync(Email, (SubscriptionPeriodKind.Trial, now.AddDays(-70), now.AddDays(-40)),
            (SubscriptionPeriodKind.Paid, now.AddDays(-40), expired));
        var charge = await CreateChargeAsync(client);

        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));

        var period = (await LoadSubscriptionAsync(Email)).Periods.Single(p => p.SubscriptionChargeId == charge.Id);
        Assert.Equal(expired, period.StartsAt);
    }

    [Fact]
    public async Task En_el_gratuito_empieza_al_pagar()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedExpiredPlanAsync(Email);
        var charge = await CreateChargeAsync(client);
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));

        var period = (await LoadSubscriptionAsync(Email)).Periods.Single(p => p.SubscriptionChargeId == charge.Id);
        Assert.InRange(period.StartsAt, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task El_anual_congela_su_precio_para_la_renovacion()
    {
        var client = await TrialMerchantAsync(Email);
        var annual = await CreateChargeAsync(client, SubscriptionPackage.Annual);
        await PostWebhookAsync(LinkWebhookBody(annual, "paid", (NewTransactionId(), annual.Amount)));

        await WithSettingsAsync(s => s.ProFounderAnnualPrice = 1690m, async () =>
        {
            var renewal = await CreateChargeAsync(client, SubscriptionPackage.Annual);

            Assert.Equal(1490m, (await LoadSubscriptionAsync(Email)).LockedAnnualPrice);
            Assert.Equal(1490m, renewal.Amount);
        });
    }

    [Fact]
    public async Task Cotizado_como_fundador_sin_lugar_al_pagar_se_abona_sin_numero()
    {
        var client = await TrialMerchantAsync(Email);
        await WithSettingsAsync(s => s.FounderSlots = 1, async () =>
        {
            var charge = await CreateChargeAsync(client);
            Assert.True(charge.IsFounderPrice);
            await TrialMerchantAsync(OtherEmail);
            await WithDbAsync(async db =>
            {
                var other = await MerchantOfAsync(db, OtherEmail);
                (await db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.MerchantId == other.Id)).AssignFounder(1);
                await db.SaveChangesAsync();
            });

            await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));

            var subscription = await LoadSubscriptionAsync(Email);
            Assert.Equal(SubscriptionChargeStatus.Paid, (await LoadChargeAsync(charge.Id)).Status);
            Assert.False(subscription.IsFounder);
            Assert.Null(subscription.FounderNumber);
        });
    }

    [Fact]
    public async Task Sin_id_de_link_guardado_lo_encuentra_por_la_referencia()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        await UpdateChargeAsync(charge.Id, c => c.PeyPaymentLinkId = null);

        var status = await WebhookStatusAsync(await PostWebhookAsync(
            LinkWebhookBody("01PLPERDIDO", "paid", charge.ClientReferenceId, MetadataOf(charge), (NewTransactionId(), charge.Amount))));

        Assert.Equal("Processed", status);
        var paid = await LoadChargeAsync(charge.Id);
        Assert.Equal(SubscriptionChargeStatus.Paid, paid.Status);
        Assert.Equal("01PLPERDIDO", paid.PeyPaymentLinkId);
    }

    [Fact]
    public async Task Metadata_de_otro_vendedor_no_se_abona()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        var metadata = MetadataOf(charge);
        metadata["merchantId"] = "999999";

        var status = await WebhookStatusAsync(await PostWebhookAsync(
            LinkWebhookBody(charge.PeyPaymentLinkId!, "paid", charge.ClientReferenceId, metadata, (NewTransactionId(), charge.Amount))));

        Assert.Equal("Failed", status);
        Assert.Equal(SubscriptionChargeStatus.Pending, (await LoadChargeAsync(charge.Id)).Status);
        Assert.Equal(WebhookEventStatus.Failed,
            (await WithDbAsync(db => db.WebhookEvents.AsNoTracking().SingleAsync())).Status);
    }

    // --- Abono por plan (5.15) ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pro_pagado_con_Basico_por_delante_convierte_los_dias_y_el_anual_va_despues()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedBasicPlanAsync(Email); // Básico pagado hasta dentro de 25 días
        var basicEnd = (await LoadSubscriptionAsync(Email)).ProUntil;
        var charge = await CreateChargeAsync(client, SubscriptionPackage.Annual, SubscriptionPlan.Pro);

        var status = await WebhookStatusAsync(await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount))));

        Assert.Equal("Processed", status);
        Assert.True(charge.IsFounderPrice);
        var subscription = await LoadSubscriptionAsync(Email);
        var basic = Assert.Single(subscription.Periods, p => p.Plan == SubscriptionPlan.Basic);
        var conversion = Assert.Single(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.Conversion);
        var annual = Assert.Single(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.Paid && p.Plan == SubscriptionPlan.Pro);

        // Lo usado de Básico se queda; lo que quedaba (≈ 25 días) se vuelve Pro a 69/149 (fundador).
        Assert.Equal(conversion.StartsAt, basic.EndsAt);
        Assert.Equal(SubscriptionPlan.Pro, conversion.Plan);
        var remaining = basicEnd - conversion.StartsAt;
        var expected = TimeSpan.FromTicks((long)(remaining.Ticks * (69m / 149m)));
        Assert.InRange((conversion.EndsAt - conversion.StartsAt - expected).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.Equal(conversion.EndsAt, annual.StartsAt);
        Assert.Equal(MexicoTime.AddMonths(annual.StartsAt, 12), annual.EndsAt);
        Assert.Equal(charge.Id, annual.SubscriptionChargeId);
        Assert.Equal(SubscriptionPlan.Pro, subscription.LastPlan);
        Assert.Equal(SubscriptionPlan.Pro, subscription.LockedAnnualPlan);
        Assert.Equal(1490m, subscription.LockedAnnualPrice);
        Assert.Equal(PlanTier.Pro, subscription.StatusAt(DateTimeOffset.UtcNow, 5).Tier);

        var email = Factory.Emails.Last(Email);
        Assert.Contains("Subiste a Pro", email.Html);
        Assert.Contains("de Básico que quedaban se volvieron 11 días de Pro", email.Html); // 25 × 69/149 = 11.58
    }

    [Fact]
    public async Task Basico_pagado_durante_Pro_se_encadena_al_final()
    {
        var client = await ActiveMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        await SeedPlanAsync(Email,
            (SubscriptionPeriodKind.Trial, SubscriptionPlan.Pro, now.AddDays(-40), now.AddDays(-25)),
            (SubscriptionPeriodKind.Paid, SubscriptionPlan.Pro, now.AddDays(-20), now.AddDays(10)));
        var proEnd = (await LoadSubscriptionAsync(Email)).ProUntil;
        var charge = await CreateChargeAsync(client, SubscriptionPackage.OneMonth, SubscriptionPlan.Basic);

        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));

        Assert.Equal(69m, charge.Amount); // Básico de fundador
        var subscription = await LoadSubscriptionAsync(Email);
        var basic = Assert.Single(subscription.Periods, p => p.Plan == SubscriptionPlan.Basic);
        Assert.Equal(proEnd, basic.StartsAt);
        Assert.Equal(SubscriptionPeriodKind.Paid, basic.Kind);
        Assert.DoesNotContain(subscription.Periods, p => p.Kind == SubscriptionPeriodKind.Conversion);
        var status = subscription.StatusAt(DateTimeOffset.UtcNow, 5);
        Assert.Equal(PlanTier.Pro, status.Tier);
        Assert.Equal(SubscriptionPlan.Basic, status.NextPlan);
        Assert.Equal(SubscriptionPlan.Basic, subscription.LastPlan);
        Assert.Contains("Empieza cuando termina lo que ya tenías", Factory.Emails.Last(Email).Html);
    }

    [Fact]
    public async Task Basico_pagado_durante_la_prueba_sigue_en_la_prueba()
    {
        var client = await TrialMerchantAsync(Email);
        var trialEnd = (await LoadSubscriptionAsync(Email)).ProUntil;
        var charge = await CreateChargeAsync(client, SubscriptionPackage.Annual, SubscriptionPlan.Basic);

        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));

        var subscription = await LoadSubscriptionAsync(Email);
        var basic = Assert.Single(subscription.Periods, p => p.Plan == SubscriptionPlan.Basic);
        Assert.Equal(trialEnd, basic.StartsAt);
        Assert.Equal(MexicoTime.AddMonths(trialEnd, 12), basic.EndsAt);
        var status = subscription.StatusAt(DateTimeOffset.UtcNow, 5);
        Assert.Equal(PlanTier.Trial, status.Tier);
        Assert.Equal(SubscriptionPlan.Basic, status.NextPlan);
    }

    [Fact]
    public async Task Un_cargo_de_antes_de_la_migracion_sin_version_de_Terminos_se_abona_igual()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        await UpdateChargeAsync(charge.Id, c =>
        {
            c.TermsVersion = null;
            c.TermsAcceptedAt = null;
        });

        var status = await WebhookStatusAsync(await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount))));

        Assert.Equal("Processed", status);
        var paid = await LoadChargeAsync(charge.Id);
        Assert.Equal(SubscriptionChargeStatus.Paid, paid.Status);
        Assert.Equal(SubscriptionPlan.Pro, paid.Plan);
        Assert.Single((await LoadSubscriptionAsync(Email)).Periods, p => p.Kind == SubscriptionPeriodKind.Paid);
    }
}
