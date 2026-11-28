using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Local133.Domain.Sales;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Hosting;
using Local133.Web.Models;
using Local133.Web.PublicPages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Local133.IntegrationTests.Sales;

/// <summary>
/// Paso 2.8 (+ 3.3): recibo público por token, sin sesión y sin datos internos. Desde el 2026-11-27 lo dibuja la portada
/// con los datos de <c>GET /api/public/recibos/{token}</c>; la liga vieja <c>/recibo/{token}</c> del portal redirige ahí.
/// </summary>
public class ReceiptTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private static string ApiUrl(Sale sale) => PublicPagesApi.Receipt(sale.ReceiptToken);

    [Fact]
    public async Task Sin_sesion_la_API_da_el_recibo_de_una_venta_pagada()
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(merchant, "150.00", "2 tacos al pastor");
        var paidAt = new DateTimeOffset(2026, 11, 12, 21, 45, 0, TimeSpan.Zero); // 3:45 p. m. en CDMX
        await PostWebhookAsync(QrPaymentBodyFor(sale, paidAt: paidAt));

        var response = await LandingClient().GetAsync(ApiUrl(sale));
        var json = await response.Content.ReadAsStringAsync();
        var receipt = await response.Content.ReadFromJsonAsync<PublicReceipt>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(receipt);
        Assert.Equal("Tacos Don Ramón", receipt.BusinessName);
        Assert.Equal("$150.00", receipt.AmountText);
        Assert.Equal("2 tacos al pastor", receipt.ConceptText);
        Assert.StartsWith("12 de noviembre", receipt.PaidAtText);
        Assert.Equal("0192191042", Assert.Single(receipt.Payments).TrackingKey);
        Assert.False(receipt.IsCart);
        // Nada interno: comisión, CLABE, ids de Pey, folio.
        Assert.DoesNotContain("Comisión", json);
        Assert.DoesNotContain(TestClabe, json);
        Assert.DoesNotContain(sale.PeyQrId!, json);
        Assert.DoesNotContain("folio", json, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("noindex, nofollow", string.Join(",", response.Headers.GetValues("X-Robots-Tag")));
        Assert.Equal("no-referrer", string.Join(",", response.Headers.GetValues("Referrer-Policy")));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task El_recibo_de_un_carrito_lista_sus_productos()
    {
        var merchant = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        var sodaId = await CreateProductAsync(Email, "Refresco", 20m);
        var response = await PostFormAsync(merchant, "/app/cobrar/productos", new Dictionary<string, string>
        {
            ["Input.Lines[0].ProductId"] = tacoId.ToString(CultureInfo.InvariantCulture),
            ["Input.Lines[0].Quantity"] = "2",
            ["Input.Lines[1].ProductId"] = sodaId.ToString(CultureInfo.InvariantCulture),
            ["Input.Lines[1].Quantity"] = "1",
            ["Input.ExpectedTotal"] = "70.00",
            ["Input.Concept"] = "2 Taco al pastor, Refresco",
        });
        var sale = await LoadSaleAsync(int.Parse(Location(response)!.Split('/').Last(), CultureInfo.InvariantCulture));
        await PostWebhookAsync(QrPaymentBodyFor(sale));

        var receipt = await LandingClient().GetFromJsonAsync<PublicReceipt>(ApiUrl(sale));

        Assert.NotNull(receipt);
        Assert.True(receipt.IsCart);
        Assert.Equal(
            [new PublicReceiptItem("Taco al pastor", 2, "$25.00", "$50.00"), new PublicReceiptItem("Refresco", 1, "$20.00", "$20.00")],
            receipt.Items);
        Assert.Equal("$70.00", receipt.AmountText);
    }

    [Fact]
    public async Task El_recibo_de_un_cobro_por_celular_no_trae_el_celular()
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(merchant, "55 1234 5678");
        await PostWebhookAsync(PushPaymentBodyFor(sale));

        var json = await LandingClient().GetStringAsync(ApiUrl(sale));

        Assert.Contains("amountText", json);
        Assert.DoesNotContain("5512345678", json);
        Assert.DoesNotContain("5678", json);
    }

    [Fact]
    public async Task Otro_vendedor_con_sesion_tambien_lo_ve_sin_el_folio()
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(merchant);
        await PostWebhookAsync(QrPaymentBodyFor(sale));
        var other = await ActiveMerchantAsync(OtherEmail);

        var response = await LandingClient(other).GetAsync(ApiUrl(sale));
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Tacos Don Ramón", json);
        Assert.DoesNotContain("folio", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(SaleStatus.Pending)]
    [InlineData(SaleStatus.Expired)]
    [InlineData(SaleStatus.Cancelled)]
    public async Task Sin_pago_no_hay_recibo(SaleStatus status)
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(merchant);
        await UpdateSaleAsync(sale.Id, s => s.Status = status);

        var response = await LandingClient().GetAsync(ApiUrl(sale));
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Tacos Don Ramón", json);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0123456789abcdef0123456789abcdef")] // bien formado pero inexistente
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    public async Task Un_token_invalido_o_inexistente_da_404(string token)
    {
        var response = await LandingClient().GetAsync(PublicPagesApi.Receipt(token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task El_token_en_mayusculas_no_abre_el_recibo()
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(merchant);
        await PostWebhookAsync(QrPaymentBodyFor(sale));

        var response = await LandingClient().GetAsync(PublicPagesApi.Receipt(sale.ReceiptToken.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("otra-llave")]
    [InlineData(Local133WebFactory.PublicApiKey + "x")]
    public async Task Sin_la_llave_de_la_portada_la_API_responde_401_sin_datos(string? key)
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(merchant);
        await PostWebhookAsync(QrPaymentBodyFor(sale));
        var client = CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add(PublicPagesApi.KeyHeader, key);
        }

        var receipt = await client.GetAsync(ApiUrl(sale));
        var storefront = await client.GetAsync(PublicPagesApi.Storefront("tacos-don-ramon"));

        Assert.Equal(HttpStatusCode.Unauthorized, receipt.StatusCode);
        Assert.Equal("application/problem+json", receipt.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Tacos Don Ramón", await receipt.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, storefront.StatusCode);
    }

    [Fact]
    public async Task Si_el_portal_no_tiene_la_llave_la_API_queda_cerrada()
    {
        // Fuente agregada después de la de la fábrica: gana sobre su llave.
        using var site = Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { [PublicPagesApi.KeyConfigKey] = "" })));
        var client = site.CreateClient();
        client.DefaultRequestHeaders.Add(PublicPagesApi.KeyHeader, "");

        var response = await client.GetAsync(PublicPagesApi.Receipt("0123456789abcdef0123456789abcdef"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task La_liga_vieja_del_portal_redirige_al_recibo_de_la_portada()
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(merchant);
        await PostWebhookAsync(QrPaymentBodyFor(sale));
        var site = Factory.Services.GetRequiredService<IOptions<PublicSiteOptions>>().Value;

        var response = await LandingClient().GetAsync($"/recibo/{sale.ReceiptToken}");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"{site.LandingUrl.TrimEnd('/')}/recibo/{sale.ReceiptToken}", response.Headers.Location?.OriginalString);
        Assert.Equal("noindex, nofollow", string.Join(",", response.Headers.GetValues("X-Robots-Tag")));
    }
}

/// <summary>
/// Los contratos de <c>/api/public</c> se enlazan en la portada, que no puede referenciar Domain ni Web: sus constantes
/// copian las del portal y aquí se comparan.
/// </summary>
public class PublicPageContractsTests
{
    [Fact]
    public void Las_constantes_coinciden_con_las_reglas_del_portal()
    {
        Assert.Equal(Sale.ReceiptTokenLength, PublicPagesApi.ReceiptTokenLength);
        Assert.Equal(Domain.Merchants.PublicSlug.PathPrefix, PublicPagesApi.StorefrontPathPrefix);
        Assert.Equal(PageInfo.QueryKey, PublicPagesApi.PageQueryKey);
        Assert.Equal(StorefrontViewModel.PageSize, PublicStorefront.DefaultPageSize);
        Assert.Equal(StorefrontViewModel.QueryMaxLength, PublicStorefront.QueryMaxLength);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("abc")]
    [InlineData("")]
    public void El_token_se_valida_igual_que_en_el_portal(string token) =>
        Assert.Equal(Sale.IsValidReceiptToken(token), PublicPagesApi.IsReceiptToken(token));

    [Fact]
    public void Un_token_nuevo_tiene_la_forma_de_token() =>
        Assert.True(PublicPagesApi.IsReceiptToken(Sale.NewReceiptToken()));
}

/// <summary>Ligas del recibo: URL absoluta en la portada (App:LandingUrl, 2026-11-27) y mensaje de WhatsApp.</summary>
public class ReceiptLinksTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    private static ReceiptLinks Links(string? landingUrl = null) =>
        new(Options.Create(landingUrl is null ? new PublicSiteOptions() : new PublicSiteOptions { LandingUrl = landingUrl }));

    [Fact]
    public void Sin_portada_configurada_usa_la_de_produccion() =>
        Assert.Equal($"https://local133.com.mx/recibo/{Token}", Links().UrlFor(Token));

    [Theory]
    [InlineData("https://local133.mx")]
    [InlineData("https://local133.mx/")]
    [InlineData(" https://local133.mx/ ")]
    public void Con_portada_configurada_la_usa(string landingUrl) =>
        Assert.Equal($"https://local133.mx/recibo/{Token}", Links(landingUrl).UrlFor(Token));

    [Fact]
    public void El_mensaje_lleva_negocio_folio_monto_y_liga()
    {
        var share = Links("https://local133.mx").ShareFor(Token, "Tacos Don Ramón", 12, "$150.00", null);

        Assert.Equal($"Recibo de pago de Tacos Don Ramón\nVenta 12 por $150.00\nhttps://local133.mx/recibo/{Token}", share.Message);
        Assert.Equal($"https://wa.me/?text={Uri.EscapeDataString(share.Message)}", share.WhatsAppUrl);
        Assert.Equal($"https://local133.mx/recibo/{Token}", share.Url);
        Assert.Null(share.CustomerMaskedPhone);
    }

    [Fact]
    public void Sin_nombre_de_negocio_el_mensaje_es_generico() =>
        Assert.StartsWith("Recibo de pago\nVenta 3 por $10.00\n",
            Links().ShareFor(Token, "  ", 3, "$10.00", null).Message);

    [Fact]
    public void Con_el_celular_del_cliente_ofrece_su_chat_sin_escribir_el_numero()
    {
        var share = Links().ShareFor(Token, "Tacos", 1, "$1.00", "5512345678", "/app/ventas/7/whatsapp");

        Assert.Equal("/app/ventas/7/whatsapp", share.CustomerWhatsAppPath);
        Assert.Equal("•••• 5678", share.CustomerMaskedPhone);
        Assert.StartsWith("https://wa.me/?text=", share.WhatsAppUrl);
        Assert.DoesNotContain("5512345678", share.ToString());
    }

    [Theory]
    [InlineData("123", "/app/ventas/7/whatsapp")]
    [InlineData(null, "/app/ventas/7/whatsapp")]
    [InlineData("5512345678", null)]
    public void Sin_celular_valido_o_sin_ruta_no_ofrece_el_chat_del_cliente(string? phone, string? path)
    {
        var share = Links().ShareFor(Token, "Tacos", 1, "$1.00", phone, path);

        Assert.Null(share.CustomerWhatsAppPath);
        Assert.Null(share.CustomerMaskedPhone);
    }

    [Fact]
    public void WhatsAppUrl_con_celular_abre_el_chat_con_lada_de_Mexico()
    {
        Assert.Equal("https://wa.me/525512345678?text=Hola%0Aliga", ReceiptLinks.WhatsAppUrl("Hola\nliga", "5512345678"));
        Assert.Equal("https://wa.me/?text=Hola", ReceiptLinks.WhatsAppUrl("Hola", "123"));
    }

    [Fact]
    public void En_escritorio_hay_liga_a_la_app_y_a_WhatsApp_Web()
    {
        var share = Links().ShareFor(Token, "Tacos", 1, "$1.00");

        Assert.Equal($"whatsapp://send?text={Uri.EscapeDataString(share.Message)}", share.WhatsAppAppUrl);
        Assert.Equal($"https://web.whatsapp.com/send?text={Uri.EscapeDataString(share.Message)}", share.WhatsAppWebUrl);
    }

    [Theory]
    [InlineData("app", "whatsapp://send?phone=525512345678&text=Hola")]
    [InlineData("web", "https://web.whatsapp.com/send?phone=525512345678&text=Hola")]
    [InlineData(null, "https://wa.me/525512345678?text=Hola")]
    [InlineData("otro", "https://wa.me/525512345678?text=Hola")]
    public void WhatsAppUrlFor_elige_app_web_o_wa_me(string? destination, string expected) =>
        Assert.Equal(expected, ReceiptLinks.WhatsAppUrlFor(destination, "Hola", "5512345678"));
}
