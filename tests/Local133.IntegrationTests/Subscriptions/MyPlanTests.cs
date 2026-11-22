using System.Globalization;
using System.Net;
using System.Text.Json;
using Local133.Application.Payments;
using Local133.Application.Subscriptions;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>5.6: «Mi plan» con los paquetes, el pago con link de Pey (simulado), la confirmación y su API.</summary>
public class MyPlanTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    // --- Paquetes -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task En_la_prueba_muestra_los_cuatro_paquetes_con_precio_de_fundador()
    {
        var client = await TrialMerchantAsync(Email);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));

        foreach (var package in Enum.GetNames<SubscriptionPackage>())
        {
            Assert.Contains($"data-plan-offer=\"{package}\"", html);
        }

        Assert.Contains("Pagar $149", html);
        Assert.Contains("Pagar $1,490", html);
        Assert.Contains("data-plan-offer-founder", html);
        Assert.Contains("quedan 100 lugares", html);
        Assert.Contains("Equivale a $124.17 al mes: 2 meses de regalo", html);
        Assert.Contains("Por $596 más llévate el año completo", html); // 1490 − 6 × 149
        Assert.Contains("Tu prueba no se acorta", html);
        Assert.Contains("IVA incluido", html);
        Assert.DoesNotContain("Muy pronto podrás pagar", html); // ya no es «Próximamente»
    }

    [Fact]
    public async Task Sin_lugares_de_fundador_cobra_el_precio_normal()
    {
        var client = await TrialMerchantAsync(Email);
        await WithSettingsAsync(s => s.FounderSlots = 1, async () =>
        {
            await TrialMerchantAsync(OtherEmail);
            await WithDbAsync(async db =>
            {
                var other = await MerchantOfAsync(db, OtherEmail);
                var subscription = await db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.MerchantId == other.Id);
                subscription.AssignFounder(1);
                await db.SaveChangesAsync();
            });

            var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));
            var charge = await CreateChargeAsync(client);

            Assert.Contains("Pagar $199", html);
            Assert.Contains("Pagar $1,990", html);
            Assert.Contains("Por $796 más llévate el año completo", html);
            Assert.Contains("Equivale a $165.83 al mes: 2 meses de regalo", html);
            Assert.DoesNotContain("data-plan-founder", html);
            Assert.Equal(199m, charge.Amount);
            Assert.False(charge.IsFounderPrice);
        });
    }

    [Fact]
    public async Task En_el_onboarding_no_hay_paquetes_ni_se_llama_a_Pey()
    {
        var client = await ActiveMerchantAsync(Email, PeyCodiStatus.Processing, MerchantStatus.BankAccountInReview);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));
        var response = await PayAsync(client, SubscriptionPackage.OneMonth);
        var after = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));

        Assert.Contains("data-plan-onboarding", html);
        Assert.DoesNotContain("data-plan-offers", html);
        Assert.Equal(PlanUrl, Location(response));
        Assert.Contains("Podrás pagar Pro cuando tu cuenta bancaria esté activa", after);
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        Assert.Empty(await ChargesAsync());
    }

    [Fact]
    public async Task Dado_de_baja_no_puede_pagar()
    {
        var client = await TrialMerchantAsync(Email);
        await UpdateMerchantAsync(Email, m => m.Status = MerchantStatus.Disabled);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));
        var response = await PayAsync(client, SubscriptionPackage.OneMonth);

        Assert.Contains("data-plan-disabled", html);
        Assert.DoesNotContain("data-plan-offers", html);
        Assert.Equal(PlanUrl, Location(response));
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
    }

    // --- Pagar --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pagar_crea_el_cargo_y_el_link_y_manda_a_Pey()
    {
        var client = await TrialMerchantAsync(Email);
        var merchantId = await MerchantIdAsync(Email);

        var response = await PayAsync(client, SubscriptionPackage.ThreeMonths);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var request = Assert.Single(Factory.Gateway.PaymentLinkRequests);
        var charge = Assert.Single(await ChargesAsync());
        Assert.Equal(FakePaymentGateway.PaymentLinkUrlFor(charge.PeyPaymentLinkId!), response.Headers.Location);
        Assert.Equal(447m, request.Amount); // 3 × 149, fundador
        Assert.False(request.HasTip);
        Assert.Equal($"https://localhost/app/plan/pago/{charge.Id}", request.SuccessUrl.AbsoluteUri);
        Assert.Equal("https://localhost/app/plan", request.BackUrl.AbsoluteUri);
        Assert.Equal($"sub-{charge.Id}", request.ClientReferenceId);
        Assert.Equal(SubscriptionChargeMetadata.SubscriptionKind, request.Metadata![SubscriptionChargeMetadata.KindKey]);
        Assert.Equal(merchantId.ToString(CultureInfo.InvariantCulture), request.Metadata[SubscriptionChargeMetadata.MerchantIdKey]);
        Assert.Equal(charge.Id.ToString(CultureInfo.InvariantCulture), request.Metadata[SubscriptionChargeMetadata.SubscriptionChargeIdKey]);

        Assert.Equal(merchantId, charge.MerchantId);
        Assert.Equal(SubscriptionPackage.ThreeMonths, charge.Package);
        Assert.Equal(3, charge.Months);
        Assert.Equal(447m, charge.ListPrice);
        Assert.Equal(447m, charge.Amount);
        Assert.True(charge.IsFounderPrice);
        Assert.Equal(SubscriptionChargeOrigin.Merchant, charge.Origin);
        Assert.Equal(SubscriptionChargeStatus.Pending, charge.Status);
    }

    [Fact]
    public async Task Reutiliza_el_link_pendiente_del_mismo_paquete()
    {
        var client = await TrialMerchantAsync(Email);

        var first = await PayAsync(client, SubscriptionPackage.OneMonth);
        var second = await PayAsync(client, SubscriptionPackage.OneMonth);
        var annual = await PayAsync(client, SubscriptionPackage.Annual);

        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.NotEqual(first.Headers.Location, annual.Headers.Location);
        Assert.Equal(2, Factory.Gateway.PaymentLinkRequests.Count);
        Assert.Equal(2, (await ChargesAsync()).Count);
    }

    [Fact]
    public async Task Un_link_pendiente_mas_viejo_que_los_dias_de_gracia_no_se_reutiliza()
    {
        var client = await TrialMerchantAsync(Email);
        var old = await CreateChargeAsync(client);
        await WithDbAsync(async db =>
        {
            var charge = await db.SubscriptionCharges.IgnoreQueryFilters().SingleAsync(c => c.Id == old.Id);
            charge.CreatedAt = DateTimeOffset.UtcNow.AddDays(-6); // GraceDays = 5
            await db.SaveChangesAsync();
        });

        var response = await PayAsync(client, SubscriptionPackage.OneMonth);

        var charges = await ChargesAsync();
        Assert.Equal(2, charges.Count);
        Assert.Equal(2, Factory.Gateway.PaymentLinkRequests.Count);
        var fresh = Assert.Single(charges, c => c.Id != old.Id);
        Assert.Equal(FakePaymentGateway.PaymentLinkUrlFor(fresh.PeyPaymentLinkId!), response.Headers.Location);
    }

    [Fact]
    public async Task Un_link_generado_y_sin_pagar_no_se_muestra_como_pago_pendiente()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));

        Assert.DoesNotContain(charge.Url!, html);
        Assert.DoesNotContain("pago pendiente", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"data-plan-charge=\"{charge.Id}\"", html);
    }

    [Fact]
    public async Task Si_cambia_el_precio_crea_otro_link()
    {
        var client = await TrialMerchantAsync(Email);
        var first = await CreateChargeAsync(client);

        await WithSettingsAsync(s => s.FounderMonthlyPrice = 159m, async () =>
        {
            var second = await CreateChargeAsync(client);

            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(159m, second.Amount);
            Assert.Equal(149m, (await LoadChargeAsync(first.Id)).Amount); // el precio guardado no cambia
        });
    }

    [Fact]
    public async Task Si_Pey_no_responde_el_cargo_queda_fallido_y_avisa()
    {
        var client = await TrialMerchantAsync(Email);
        Factory.Gateway.PaymentLinkException = new PaymentGatewayUnavailableException("timeout");

        var response = await PayAsync(client, SubscriptionPackage.OneMonth);
        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));
        Factory.Gateway.PaymentLinkException = null;
        var retry = await CreateChargeAsync(client);

        Assert.Equal(PlanUrl, Location(response));
        Assert.Contains("Pey no respondió a tiempo", html);
        var charges = await ChargesAsync();
        Assert.Equal(2, charges.Count);
        Assert.Equal(SubscriptionChargeStatus.Failed, charges[0].Status);
        Assert.Null(charges[0].PeyPaymentLinkId);
        Assert.Equal(SubscriptionChargeStatus.Pending, retry.Status);
    }

    [Fact]
    public async Task Si_Pey_rechaza_el_link_el_cargo_queda_fallido()
    {
        var client = await TrialMerchantAsync(Email);
        Factory.Gateway.PaymentLinkException = new PaymentGatewayValidationException(
            "Pey: url inválida.", PeyErrorType.InvalidUrl, 400, "req_1", "invalid url");

        var response = await PayAsync(client, SubscriptionPackage.OneMonth);
        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));

        Assert.Equal(PlanUrl, Location(response));
        Assert.Contains("Pey no aceptó generar el link de pago", html);
        Assert.Equal(SubscriptionChargeStatus.Failed, Assert.Single(await ChargesAsync()).Status);
    }

    [Fact]
    public async Task Sin_HTTPS_no_crea_cargo_ni_llama_a_Pey()
    {
        await TrialMerchantAsync(Email);
        var merchantId = await MerchantIdAsync(Email);

        using var scope = Factory.Services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<ISubscriptionCheckout>();
        var result = await checkout.StartAsync(new StartCheckoutCommand(merchantId, SubscriptionPackage.OneMonth, new Uri("http://localhost:5231")));

        Assert.IsType<CheckoutResult.HttpsRequired>(result);
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        Assert.Empty(await ChargesAsync());
    }

    [Fact]
    public async Task Un_paquete_desconocido_regresa_a_Mi_plan()
    {
        var client = await TrialMerchantAsync(Email);

        var response = await PostFormAsync(client, PlanUrl, new Dictionary<string, string> { ["package"] = "Gratis" },
            postUrl: "/app/plan/pagar");

        Assert.Equal(PlanUrl, Location(response));
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
    }

    // --- Confirmación y API -------------------------------------------------------------------------------------------

    [Fact]
    public async Task La_confirmacion_espera_y_la_API_abona_cuando_Pey_dice_pagado()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        Factory.Gateway.PaymentLinks[charge.PeyPaymentLinkId!] = FakePaymentGateway.PaymentLink(
            charge.PeyPaymentLinkId!, charge.Amount, PeyPaymentLinkStatus.Pending, charge.ClientReferenceId);

        var waiting = WebUtility.HtmlDecode(await GetHtmlAsync(client, $"/app/plan/pago/{charge.Id}"));
        Factory.Gateway.PaymentLinks[charge.PeyPaymentLinkId!] = FakePaymentGateway.PaymentLink(
            charge.PeyPaymentLinkId!, charge.Amount, PeyPaymentLinkStatus.Paid, charge.ClientReferenceId, null,
            (NewTransactionId(), charge.Amount));
        await UpdateChargeAsync(charge.Id, c => c.PaymentCheckedAt = DateTimeOffset.UtcNow.AddMinutes(-1)); // pasa el freno
        var api = await ReadStatusAsync(client, charge.Id);
        var paid = WebUtility.HtmlDecode(await GetHtmlAsync(client, $"/app/plan/pago/{charge.Id}"));

        Assert.Contains("data-status=\"Pending\"", waiting);
        Assert.Contains("Confirmando tu pago…", waiting);
        Assert.Contains("Volver a la página de pago", waiting);
        Assert.Equal("Paid", api.GetProperty("status").GetString());
        Assert.StartsWith("Tienes Pro hasta el", api.GetProperty("proUntilText").GetString());
        Assert.Contains("data-status=\"Paid\"", paid);
        Assert.Equal(SubscriptionChargeStatus.Paid, (await LoadChargeAsync(charge.Id)).Status);
    }

    [Fact]
    public async Task La_API_frena_las_consultas_a_Pey()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        Factory.Gateway.PaymentLinks[charge.PeyPaymentLinkId!] = FakePaymentGateway.PaymentLink(
            charge.PeyPaymentLinkId!, charge.Amount, PeyPaymentLinkStatus.Pending);

        await ReadStatusAsync(client, charge.Id);
        await ReadStatusAsync(client, charge.Id);
        var status = await ReadStatusAsync(client, charge.Id);

        Assert.Equal("Pending", status.GetProperty("status").GetString());
        Assert.Single(Factory.Gateway.PaymentLinkLookups);
    }

    [Fact]
    public async Task Si_Pey_falla_la_API_tampoco_reintenta_en_cada_consulta()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        Factory.Gateway.PaymentLinkLookupExceptions[charge.PeyPaymentLinkId!] = new PaymentGatewayUnavailableException("timeout");

        var first = await ReadStatusAsync(client, charge.Id);
        await ReadStatusAsync(client, charge.Id);

        Assert.Equal("Pending", first.GetProperty("status").GetString());
        Assert.Single(Factory.Gateway.PaymentLinkLookups);
        Assert.Null((await LoadChargeAsync(charge.Id)).PaymentCheckedAt); // sin sellar: el job lo reintenta
    }

    [Fact]
    public async Task El_cargo_de_otro_vendedor_da_404()
    {
        var owner = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(owner);
        var other = await TrialMerchantAsync(OtherEmail);

        var page = await other.GetAsync($"/app/plan/pago/{charge.Id}");
        var api = await other.GetAsync($"/api/subscription-charges/{charge.Id}");

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.Empty(Factory.Gateway.PaymentLinkLookups);
    }

    [Fact]
    public async Task La_API_sin_sesion_responde_401()
    {
        var response = await CreateClient().GetAsync("/api/subscription-charges/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Los_pagos_aparecen_en_Mi_plan()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(
            LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)))));

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));

        Assert.Contains($"data-plan-payment=\"{charge.Id}\"", html);
        Assert.Contains("Pro · 1 mes", html);
        Assert.Contains("CoDi", html);
        Assert.Contains("data-plan-tier=\"Pro\"", html); // ya pagó: Pro, aunque lo pagado empiece al terminar la prueba
        Assert.Contains("Lo que pagaste empieza al terminar tu prueba", html);
        Assert.Contains("Eres fundador", html);
    }

    private static async Task<JsonElement> ReadStatusAsync(HttpClient client, int chargeId)
    {
        var response = await client.GetAsync($"/api/subscription-charges/{chargeId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
