using System.Globalization;
using System.Net;
using System.Text.Json;
using Local133.Application.Payments;
using Local133.Application.Subscriptions;
using Local133.Domain.Legal;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.6 y 5.15: los paquetes del checkout (1 mes y Anual de cada plan, en una página), el pago con link de Pey (simulado) desde el resumen del pedido, la
/// confirmación y su API, y «Tus pedidos» en «Mi plan».
/// </summary>
public class MyPlanTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    // --- Paquetes (paso 1) --------------------------------------------------------------------------------------------

    [Fact]
    public async Task En_la_prueba_muestra_1_mes_y_anual_de_cada_plan_con_precio_de_fundador()
    {
        var client = await TrialMerchantAsync(Email);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, CheckoutUrl));

        foreach (var plan in Enum.GetValues<SubscriptionPlan>())
        {
            foreach (var package in SubscriptionPackages.Offered)
            {
                Assert.Contains($"href=\"{SummaryUrl(plan, package)}\" data-plan-offer=\"{package}\"", html);
            }
        }

        // Solo 1 mes y Anual (2026-11-27).
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Count(html, "data-plan-offer=\""));
        Assert.DoesNotContain("3-meses", html);
        Assert.DoesNotContain("6-meses", html);
        Assert.Contains("id=\"basico\"", html);
        Assert.Contains("id=\"pro\"", html);
        foreach (var title in new[] { "Básico (1 mes)", "Básico Anual", "Pro (1 mes)", "Pro Anual" })
        {
            Assert.Contains($"data-plan-offer-title>{title}<", html);
            Assert.Contains($"Elegir {title}", html);
        }

        Assert.Contains("data-plan-offer-price>$149<", html);
        Assert.Contains("data-plan-offer-price>$1,490<", html);
        Assert.Contains("data-plan-offer-price>$69<", html);
        Assert.Contains("data-plan-offer-price>$690<", html);
        Assert.Contains("data-plan-offer-founder", html);
        Assert.Contains("quedan 100 lugares", html);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(html, "<span>12 meses por el precio de 10</span>"));
        Assert.Contains("Equivale a $124.17 al mes", html);
        Assert.DoesNotContain("año completo", html);
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

            var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, CheckoutUrl));
            var charge = await CreateChargeAsync(client);

            Assert.Contains("data-plan-offer-price>$199<", html);
            Assert.Contains("data-plan-offer-price>$1,990<", html);
            Assert.Contains("12 meses por el precio de 10", html);
            Assert.Contains("Equivale a $165.83 al mes", html);
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
        var durations = WebUtility.HtmlDecode(await GetHtmlAsync(client, CheckoutUrl));
        var response = await PayAsync(client, SubscriptionPackage.OneMonth);
        var after = WebUtility.HtmlDecode(await GetHtmlAsync(client, SummaryUrl(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth)));

        Assert.Contains("data-plan-onboarding", html);
        Assert.DoesNotContain("data-plan-actions", html);
        Assert.DoesNotContain("data-plan-offers", durations);
        Assert.Contains("data-checkout-not-allowed", durations);
        Assert.Equal(SummaryUrl(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth), Location(response));
        Assert.Contains("Tu prueba de Pro empieza con tu primer cobro", after);
        Assert.DoesNotContain("data-checkout-form", after);
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
        Assert.DoesNotContain("data-plan-actions", html);
        Assert.Equal(SummaryUrl(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth), Location(response));
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
    }

    // --- Pagar --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pagar_crea_el_cargo_y_el_link_y_manda_a_Pey()
    {
        var client = await TrialMerchantAsync(Email);
        var merchantId = await MerchantIdAsync(Email);

        var response = await PayAsync(client, SubscriptionPackage.Annual);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var request = Assert.Single(Factory.Gateway.PaymentLinkRequests);
        var charge = Assert.Single(await ChargesAsync());
        Assert.Equal(FakePaymentGateway.PaymentLinkUrlFor(charge.PeyPaymentLinkId!), response.Headers.Location);
        Assert.Equal(1490m, request.Amount); // anual de fundador
        Assert.False(request.HasTip);
        Assert.Equal($"https://localhost/app/plan/pago/{charge.Id}", request.SuccessUrl.AbsoluteUri);
        Assert.Equal("https://localhost/app/plan/checkout/pro/anual", request.BackUrl.AbsoluteUri); // «Regresar» vuelve al resumen
        Assert.Equal($"sub-{charge.Id}", request.ClientReferenceId);
        Assert.Equal(SubscriptionChargeMetadata.SubscriptionKind, request.Metadata![SubscriptionChargeMetadata.KindKey]);
        Assert.Equal(merchantId.ToString(CultureInfo.InvariantCulture), request.Metadata[SubscriptionChargeMetadata.MerchantIdKey]);
        Assert.Equal(charge.Id.ToString(CultureInfo.InvariantCulture), request.Metadata[SubscriptionChargeMetadata.SubscriptionChargeIdKey]);

        Assert.Equal(merchantId, charge.MerchantId);
        Assert.Equal(SubscriptionPackage.Annual, charge.Package);
        Assert.Equal(12, charge.Months);
        Assert.Equal(1490m, charge.ListPrice);
        Assert.Equal(1490m, charge.Amount);
        Assert.True(charge.IsFounderPrice);
        Assert.Equal(SubscriptionChargeOrigin.Merchant, charge.Origin);
        Assert.Equal(SubscriptionChargeStatus.Pending, charge.Status);
        Assert.Equal(SubscriptionPlan.Pro, charge.Plan);
        Assert.Equal(LegalDocuments.CurrentTermsVersion, charge.TermsVersion);
        Assert.NotNull(charge.TermsAcceptedAt);
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
        Assert.DoesNotContain($"data-plan-order=\"{charge.Id}\"", html);
    }

    [Fact]
    public async Task Si_cambia_el_precio_crea_otro_link()
    {
        var client = await TrialMerchantAsync(Email);
        var first = await CreateChargeAsync(client);

        await WithSettingsAsync(s => s.ProFounderMonthlyPrice = 159m, async () =>
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
        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, Location(response)!));
        Factory.Gateway.PaymentLinkException = null;
        var retry = await CreateChargeAsync(client);

        Assert.Equal(SummaryUrl(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth), Location(response));
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
        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, Location(response)!));

        Assert.Equal(SummaryUrl(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth), Location(response));
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
        var result = await checkout.StartAsync(new StartCheckoutCommand(merchantId, SubscriptionPlan.Pro, SubscriptionPackage.OneMonth,
            new Uri("http://localhost:5231"), LegalDocuments.CurrentTermsVersion, 149m));

        Assert.IsType<CheckoutResult.HttpsRequired>(result);
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        Assert.Empty(await ChargesAsync());
    }

    [Fact]
    public async Task Un_paquete_o_un_plan_desconocido_da_404_sin_llamar_a_Pey()
    {
        var client = await TrialMerchantAsync(Email);

        var package = await PostFormAsync(client, PlanUrl, new Dictionary<string, string>
        {
            ["Input.Plan"] = "pro", ["Input.Package"] = "Gratis", ["Input.AcceptTerms"] = "true", ["Input.ExpectedTotal"] = "149.00",
        }, postUrl: CheckoutUrl);
        var plan = await PostFormAsync(client, PlanUrl, new Dictionary<string, string>
        {
            ["Input.Plan"] = "gratis", ["Input.Package"] = "1-mes", ["Input.AcceptTerms"] = "true", ["Input.ExpectedTotal"] = "149.00",
        }, postUrl: CheckoutUrl);

        Assert.Equal(HttpStatusCode.NotFound, package.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, plan.StatusCode);
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        Assert.Empty(await ChargesAsync());
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
        Assert.StartsWith("Tu plan está vigente hasta el", api.GetProperty("proUntilText").GetString());
        Assert.Contains("data-status=\"Paid\"", paid);
        Assert.Contains($"¡Gracias! Tu pedido #{charge.Id} está pagado", paid);
        Assert.Contains($"href=\"/app/plan/pedidos/{charge.Id}/comprobante\"", paid);
        Assert.Contains("Pro · 1 mes · Del ", paid);
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
    public async Task Los_pedidos_aparecen_en_Mi_plan_con_su_comprobante()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(
            LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)))));

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));

        Assert.Contains($"data-plan-order=\"{charge.Id}\"", html);
        Assert.Contains($"Pedido #{charge.Id}", html);
        Assert.Contains("Pro · 1 mes", html);
        Assert.Contains("CoDi", html);
        Assert.Contains($"href=\"/app/plan/pedidos/{charge.Id}/comprobante\" data-plan-order-link>Comprobante", html);
        // Prueba con Pro pagado después: todavía no toca renovar; el botón lleva a la sección de Pro.
        Assert.Contains($"href=\"{PlanUrls.CheckoutFor(SubscriptionPlan.Pro)}\" data-plan-action=\"extend\"", html);
        Assert.Contains("Adquirir más días de Pro", html);
        Assert.DoesNotContain("Renovar igual que la última vez", html);
        Assert.DoesNotContain("data-plan-action=\"choose\"", html);
        Assert.Contains("data-plan-tier=\"Pro\"", html); // ya pagó: Pro, aunque lo pagado empiece al terminar la prueba
        Assert.Contains("Lo que pagaste empieza al terminar tu prueba", html);
        Assert.Contains("Eres fundador", WebUtility.HtmlDecode(await GetHtmlAsync(client, CheckoutUrl)));
    }

    private static async Task<JsonElement> ReadStatusAsync(HttpClient client, int chargeId)
    {
        var response = await client.GetAsync($"/api/subscription-charges/{chargeId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
