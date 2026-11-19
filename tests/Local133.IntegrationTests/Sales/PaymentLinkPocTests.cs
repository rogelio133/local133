using System.Net;
using Local133.Application.Payments;
using Local133.Domain.Pey;
using Local133.IntegrationTests.Infrastructure;

namespace Local133.IntegrationTests.Sales;

/// <summary>Prueba de concepto del link de pago (/app/link-de-pago, solo Development; Pey simulado).</summary>
public sealed class PaymentLinkPocTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string Url = "/app/link-de-pago";

    [Fact]
    public async Task Mas_no_muestra_la_liga_y_el_formulario_avisa_que_es_prueba()
    {
        var client = await ActiveMerchantAsync(Email);

        Assert.DoesNotContain("/app/link-de-pago", await GetHtmlAsync(client, "/app/mas")); // solo por URL directa
        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("data-payment-link-poc-notice", html);
        Assert.Contains("name=\"Input.Amount\"", html);
        Assert.Contains("name=\"Input.Concept\"", html);
        Assert.DoesNotContain("data-payment-link-https-warning", html); // el cliente de pruebas usa https
    }

    [Fact]
    public async Task Generar_manda_a_la_pagina_de_Pey_con_concepto_y_urls_https()
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await PostFormAsync(client, Url, new Dictionary<string, string>
        {
            ["Input.Amount"] = "25.50",
            ["Input.Concept"] = " 2 tacos al pastor ",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var request = Assert.Single(Factory.Gateway.PaymentLinkRequests);
        var id = Location(response)!.TrimStart('/');
        Assert.Equal(FakePaymentGateway.PaymentLinkUrlFor(id), response.Headers.Location); // directo a la página de Pey
        Assert.Equal(25.50m, request.Amount);
        Assert.Equal("2 tacos al pastor", request.ClientReferenceId);
        Assert.Equal("2 tacos al pastor", request.Metadata!["concept"]);
        Assert.Equal("payment-link-poc", request.Metadata["kind"]);
        Assert.Equal((await MerchantIdAsync(Email)).ToString(), request.Metadata["merchantId"]);
        Assert.Equal("https://localhost/app/link-de-pago/regreso?resultado=pagado", request.SuccessUrl.AbsoluteUri);
        Assert.Equal("https://localhost/app/link-de-pago/regreso?resultado=cancelado", request.BackUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("0", "Tacos")]
    [InlineData("abc", "Tacos")]
    [InlineData("10", "")]
    [InlineData("10", "Tacos <script>")]
    public async Task Datos_invalidos_no_llaman_a_Pey(string amount, string concept)
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await PostFormAsync(client, Url, new Dictionary<string, string>
        {
            ["Input.Amount"] = amount,
            ["Input.Concept"] = concept,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Factory.Gateway.PaymentLinkRequests);
    }

    [Fact]
    public async Task Rechazo_de_Pey_se_muestra_en_el_formulario()
    {
        var client = await ActiveMerchantAsync(Email);
        Factory.Gateway.PaymentLinkException = new PaymentGatewayValidationException(
            "Pey: monto", PeyErrorType.InvalidAmount, 400, "req-1", "Amount too low");

        var response = await PostFormAsync(client, Url, new Dictionary<string, string>
        {
            ["Input.Amount"] = "0.01",
            ["Input.Concept"] = "Tacos",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Pey rechazó el link: Amount too low", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Regreso_consulta_el_link_creado_en_la_sesion()
    {
        var client = await ActiveMerchantAsync(Email);
        var created = await PostFormAsync(client, Url, new Dictionary<string, string>
        {
            ["Input.Amount"] = "10.00",
            ["Input.Concept"] = "Tacos",
        });
        var id = Location(created)!.Split('/').Last();
        Factory.Gateway.PaymentLinks[id] = new GatewayPaymentLink(
            id, 10m, PeyPaymentLinkStatus.Paid, null, null, false, ["CARD"], DateTimeOffset.UtcNow, "Tacos",
            new Dictionary<string, string?>(),
            [new GatewayPaymentLinkTransaction("01TXPL", "CARD", DateTimeOffset.UtcNow, 10m, null, null, new Dictionary<string, string?>())]);

        var html = await GetHtmlAsync(client, $"{Url}/regreso?resultado=pagado");

        Assert.Contains("data-payment-link-result", html);
        Assert.Contains("data-payment-link-details", html);
        Assert.Contains(id, html);
        Assert.Contains("Paid", html);
        Assert.Contains("01TXPL", html);
    }

    [Fact]
    public async Task Sin_sesion_redirige_al_login()
    {
        var response = await CreateClient().GetAsync(Url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", Location(response));
    }
}
