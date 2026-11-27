using System.Net;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Areas.Merchant.Models;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.15: checkout por pasos (Plan → Resumen → Pey → Listo; desde el 2026-11-27 el plan y la duración van en la misma
/// página, con solo 1 mes y Anual). Ningún GET llama a Pey ni crea cargos; el POST
/// del resumen exige la casilla de los Términos y el total que vio el vendedor. Comprobante de los pedidos pagados.
/// </summary>
public class CheckoutTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    // --- Pasos sin Pey ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Ningun_paso_del_checkout_llama_a_Pey_ni_crea_cargos()
    {
        var client = await TrialMerchantAsync(Email);

        var urls = new List<string> { PlanUrl, CheckoutUrl };
        foreach (var plan in Enum.GetValues<SubscriptionPlan>())
        {
            urls.AddRange(SubscriptionPackages.Offered.Select(p => SummaryUrl(plan, p)));
        }

        foreach (var url in urls)
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        Assert.Empty(Factory.Gateway.PaymentLinkLookups);
        Assert.Empty(await ChargesAsync());
    }

    [Fact]
    public async Task El_paso_1_muestra_las_secciones_de_los_dos_planes_con_Pro_recomendado()
    {
        var client = await TrialMerchantAsync(Email);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, CheckoutUrl));

        Assert.Contains("data-checkout-steps=\"1\"", html);
        Assert.Contains("aria-current=\"step\"", html);
        Assert.Contains("Paso 1 de 4 · Plan", html);
        Assert.DoesNotContain("Duración", html);
        Assert.Contains("id=\"basico\" aria-labelledby=\"plan-basico-title\"", html);
        Assert.Contains("id=\"pro\" aria-labelledby=\"plan-pro-title\"", html);
        Assert.Contains("data-checkout-plan=\"basico\"", html);
        Assert.Contains("data-checkout-plan=\"pro\"", html);
        Assert.Contains("Equivale a $57.50 al mes", html); // Básico anual de fundador: 690 / 12
        Assert.Contains("Equivale a $124.17 al mes", html); // Pro anual de fundador: 1490 / 12
        Assert.Contains("data-checkout-plan-recommended", html);
        Assert.Contains("data-checkout-plan-current", html); // la prueba es de Pro
        Assert.Contains("quedan 100 lugares", html);
        Assert.Contains("data-plan-features", html);
    }

    [Fact]
    public async Task El_resumen_muestra_el_pedido_con_fundador_IVA_y_la_casilla()
    {
        var client = await TrialMerchantAsync(Email);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, SummaryUrl(SubscriptionPlan.Pro, SubscriptionPackage.Annual)));

        Assert.Contains("data-order-title>Pro Anual<", html);
        Assert.Contains("data-order-annual-deal", html);
        Assert.Contains("Estás obteniendo 12 meses por el precio de 10", html);
        Assert.Contains("data-order-row=\"list\"", html);
        Assert.Contains("$1,990.00", html);
        Assert.Contains("data-order-row=\"founder\"", html);
        Assert.Contains("−$500.00", html);
        Assert.Contains("data-order-total>$1,490.00<", html);
        Assert.Contains("IVA incluido: $205.52", html);
        Assert.Contains("cuando termina tu prueba", html);
        Assert.Contains("name=\"Input.ExpectedTotal\" value=\"1490.00\"", html);
        Assert.Contains("data-val-mustbetrue", html); // la casilla se valida también en el cliente
        Assert.Contains("Términos de la suscripción", html);
        Assert.Contains("/terminos\" target=\"_blank\"", html);
        Assert.Contains("Pagar $1,490.00", html);
        Assert.Contains($"href=\"{PlanUrls.CheckoutFor(SubscriptionPlan.Pro)}\" data-order-change", html);
        Assert.DoesNotContain("Cambiar duración", html);
        Assert.Contains("Paso 2 de 4 · Resumen", html);
    }

    [Fact]
    public async Task El_resumen_de_1_mes_no_dice_12_por_10()
    {
        var client = await TrialMerchantAsync(Email);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, SummaryUrl(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth)));

        Assert.Contains("data-order-title>Básico (1 mes)<", html);
        Assert.DoesNotContain("data-order-annual-deal", html);
        Assert.Contains("data-order-total>$69.00<", html);
    }

    [Theory]
    [InlineData("/app/plan/checkout/pro", "/app/plan/checkout#pro")]
    [InlineData("/app/plan/checkout/basico", "/app/plan/checkout#basico")]
    [InlineData("/app/plan/checkout/pro/3-meses", "/app/plan/checkout#pro")]
    [InlineData("/app/plan/checkout/basico/6-meses", "/app/plan/checkout#basico")]
    public async Task El_antiguo_paso_de_duracion_y_los_paquetes_retirados_llevan_a_la_seccion_del_plan(string url, string expected)
    {
        var client = await TrialMerchantAsync(Email);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, Location(response));
    }

    [Fact]
    public async Task Pagar_3_meses_ya_no_crea_cargo_ni_llama_a_Pey()
    {
        var client = await TrialMerchantAsync(Email);

        // Un POST a mano (el resumen de 3 meses ya no existe): el token sale del resumen de 1 mes.
        var response = await PostFormAsync(client, SummaryUrl(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth),
            new Dictionary<string, string>
            {
                ["Input.Plan"] = "pro",
                ["Input.Package"] = "3-meses",
                ["Input.ExpectedTotal"] = "447.00",
                ["Input.AcceptTerms"] = "true",
            },
            postUrl: CheckoutUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/app/plan/checkout#pro", Location(response));
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        Assert.Empty(await ChargesAsync());
    }

    [Fact]
    public async Task Subir_de_Basico_a_Pro_dice_cuantos_dias_se_convierten()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedBasicPlanAsync(Email); // 25 días de Básico por delante

        var summary = WebUtility.HtmlDecode(await GetHtmlAsync(client, SummaryUrl(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth)));
        var plan = WebUtility.HtmlDecode(await GetHtmlAsync(client, PlanUrl));

        // 24.99 días × 69/149 (fundador) = 11.57: «unos 11 días».
        Assert.Contains("de Básico se vuelven unos 11 días de Pro desde hoy", summary);
        // Básico a media vigencia: «Adquirir más días de Básico» y «Cambiar a Pro», cada uno a la sección de su plan.
        Assert.Contains($"href=\"{PlanUrls.CheckoutFor(SubscriptionPlan.Basic)}\" data-plan-action=\"extend\"", plan);
        Assert.Contains("Adquirir más días de Básico", plan);
        Assert.Contains($"href=\"{PlanUrls.CheckoutFor(SubscriptionPlan.Pro)}\" data-plan-action=\"upgrade\"", plan);
        Assert.DoesNotContain("data-plan-action=\"choose\"", plan);
    }

    [Theory]
    [InlineData("/app/plan/checkout/gratis")]
    [InlineData("/app/plan/checkout/Pro")]
    [InlineData("/app/plan/checkout/pro/12-meses")]
    [InlineData("/app/plan/checkout/basico/Annual")]
    public async Task Un_plan_o_paquete_que_no_existe_da_404(string url)
    {
        var client = await TrialMerchantAsync(Email);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Onboarding_y_dado_de_baja_ven_el_aviso_sin_el_boton()
    {
        var onboarding = await ActiveMerchantAsync(Email, PeyCodiStatus.Processing, MerchantStatus.BankAccountInReview);
        var disabled = await TrialMerchantAsync(OtherEmail);
        await UpdateMerchantAsync(OtherEmail, m => m.Status = MerchantStatus.Disabled);
        var url = SummaryUrl(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth);

        var first = WebUtility.HtmlDecode(await GetHtmlAsync(onboarding, url));
        var second = WebUtility.HtmlDecode(await GetHtmlAsync(disabled, url));

        Assert.Contains("data-checkout-not-allowed", first);
        Assert.Contains("Tu prueba de Pro empieza con tu primer cobro", first);
        Assert.DoesNotContain("data-checkout-form", first);
        Assert.Contains("data-checkout-not-allowed", second);
        Assert.Contains("dada de baja", second);
        Assert.DoesNotContain("data-checkout-form", second);
    }

    // --- POST del resumen -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Sin_la_casilla_de_los_Terminos_no_llama_a_Pey()
    {
        var client = await TrialMerchantAsync(Email);

        var response = await PayAsync(client, SubscriptionPackage.OneMonth, acceptTerms: false);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // vuelve a pintar el resumen
        Assert.Contains(CheckoutMessages.TermsNotAccepted, html);
        Assert.Contains("data-checkout-form", html);
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        Assert.Empty(await ChargesAsync());
    }

    [Fact]
    public async Task Con_el_precio_cambiado_pide_revisar_el_total_y_no_llama_a_Pey()
    {
        var client = await TrialMerchantAsync(Email);

        var response = await PayAsync(client, SubscriptionPackage.OneMonth, expectedTotal: "199.00"); // vio el precio normal
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var invalid = await PayAsync(client, SubscriptionPackage.OneMonth, expectedTotal: "abc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(CheckoutMessages.PriceChanged, html);
        Assert.Contains("name=\"Input.ExpectedTotal\" value=\"149.00\"", html); // el total nuevo, para revisarlo
        Assert.Contains(CheckoutMessages.PriceChanged, WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        Assert.Empty(await ChargesAsync());
    }

    [Fact]
    public async Task Si_se_acaban_los_lugares_de_fundador_entre_el_resumen_y_el_pago_no_cobra_el_precio_viejo()
    {
        var client = await TrialMerchantAsync(Email);
        var summary = SummaryUrl(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth);
        Assert.Contains("value=\"149.00\"", await GetHtmlAsync(client, summary));

        await WithSettingsAsync(s => s.FounderSlots = 0, async () =>
        {
            var response = await PayAsync(client, SubscriptionPackage.OneMonth, expectedTotal: "149.00");

            Assert.Contains(CheckoutMessages.PriceChanged, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
            Assert.Empty(Factory.Gateway.PaymentLinkRequests);
        });
    }

    [Fact]
    public async Task Se_reutiliza_el_link_del_mismo_plan_y_con_otro_plan_se_crea_otro()
    {
        var client = await TrialMerchantAsync(Email);

        var pro = await CreateChargeAsync(client, SubscriptionPackage.OneMonth, SubscriptionPlan.Pro);
        var again = await CreateChargeAsync(client, SubscriptionPackage.OneMonth, SubscriptionPlan.Pro);
        var basic = await CreateChargeAsync(client, SubscriptionPackage.OneMonth, SubscriptionPlan.Basic);

        Assert.Equal(pro.Id, again.Id);
        Assert.NotEqual(pro.Id, basic.Id);
        Assert.Equal(SubscriptionPlan.Basic, basic.Plan);
        Assert.Equal(69m, basic.Amount);
        Assert.Equal(2, Factory.Gateway.PaymentLinkRequests.Count);
        Assert.Equal("https://localhost/app/plan/checkout/basico/1-mes",
            Factory.Gateway.PaymentLinkRequests.Last().BackUrl.AbsoluteUri);
    }

    // --- Comprobante ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_comprobante_de_un_pedido_pagado_trae_sus_datos()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client, SubscriptionPackage.Annual);
        await PostWebhookAsync(LinkWebhookBody(charge, "paid", (NewTransactionId(), charge.Amount)));

        var response = await client.GetAsync(PlanUrls.Receipt(charge.Id));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Pedido #{charge.Id}", html);
        Assert.Contains("data-receipt-plan>Pro · Anual<", html);
        Assert.Contains("data-receipt-period>Del ", html);
        Assert.Contains("Precio de fundador", html);
        Assert.Contains("$1,490.00", html);
        Assert.Contains("$1,284.48", html); // servicio
        Assert.Contains("$205.52", html); // IVA
        Assert.Contains("data-receipt-method>CoDi<", html);
        Assert.Contains(Email, html);
        Assert.Contains("Este comprobante no es una factura", html);
        Assert.Contains("data-receipt-print", html);
        Assert.Contains("/js/receipt-share.", html);
    }

    [Fact]
    public async Task El_comprobante_sin_pagar_o_de_otro_vendedor_da_404()
    {
        var owner = await TrialMerchantAsync(Email);
        var pending = await CreateChargeAsync(owner);
        var paid = await CreateChargeAsync(owner, SubscriptionPackage.Annual);
        await PostWebhookAsync(LinkWebhookBody(paid, "paid", (NewTransactionId(), paid.Amount)));
        var other = await TrialMerchantAsync(OtherEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(PlanUrls.Receipt(pending.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(PlanUrls.Receipt(paid.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(PlanUrls.Receipt(paid.Id))).StatusCode);
    }
}
