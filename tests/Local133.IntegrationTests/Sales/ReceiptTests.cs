using System.Globalization;
using System.Net;
using Local133.Domain.Sales;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Hosting;
using Local133.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Local133.IntegrationTests.Sales;

/// <summary>Paso 2.8 (+ 3.3): recibo público por token (<c>/recibo/{token}</c>), sin sesión y sin datos internos.</summary>
public class ReceiptTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private static string ReceiptUrl(Sale sale) => $"/recibo/{sale.ReceiptToken}";

    [Fact]
    public async Task Sin_sesion_se_ve_el_recibo_de_una_venta_pagada()
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(merchant, "150.00", "2 tacos al pastor");
        var paidAt = new DateTimeOffset(2026, 11, 12, 21, 45, 0, TimeSpan.Zero); // 3:45 p. m. en CDMX
        await PostWebhookAsync(QrPaymentBodyFor(sale, paidAt: paidAt));

        var response = await CreateClient().GetAsync(ReceiptUrl(sale));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-receipt-business>Tacos Don Ramón<", html);
        Assert.Contains("data-receipt-amount>$150.00<", html);
        Assert.Contains("data-receipt-concept>2 tacos al pastor<", html);
        Assert.Contains($"data-receipt-folio>{sale.Folio}<", html);
        Assert.Contains("data-receipt-paid-at>12 de noviembre", html);
        Assert.Contains("data-receipt-tracking>0192191042<", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\" />", html);
        Assert.Contains("/js/receipt-share", html);
        // Nada interno: comisión, CLABE, ids de Pey.
        Assert.DoesNotContain("Comisión", html);
        Assert.DoesNotContain(TestClabe, html);
        Assert.DoesNotContain(sale.PeyQrId!, html);
        Assert.DoesNotContain("data-sale-qr", html);

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

        var html = await GetHtmlAsync(CreateClient(), ReceiptUrl(sale));

        Assert.Contains("data-receipt-items", html);
        Assert.Equal(2, html.Split("data-receipt-item>").Length - 1);
        Assert.Contains("2 × $25.00", html);
        Assert.Contains("1 × $20.00", html);
        Assert.Contains("data-receipt-amount>$70.00<", html);
    }

    [Fact]
    public async Task El_recibo_de_un_cobro_por_celular_no_muestra_el_celular()
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(merchant, "55 1234 5678");
        await PostWebhookAsync(PushPaymentBodyFor(sale));

        var html = await GetHtmlAsync(CreateClient(), ReceiptUrl(sale));

        Assert.DoesNotContain("5512345678", html);
        Assert.DoesNotContain("••••", html);
    }

    [Fact]
    public async Task Otro_vendedor_con_sesion_tambien_puede_abrir_la_liga()
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(merchant);
        await PostWebhookAsync(QrPaymentBodyFor(sale));
        var other = await ActiveMerchantAsync(OtherEmail);

        var html = await GetHtmlAsync(other, ReceiptUrl(sale));

        Assert.Contains("data-receipt-business>Tacos Don Ramón<", html);
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

        var response = await CreateClient().GetAsync(ReceiptUrl(sale));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Tacos Don Ramón", html);
    }

    [Theory]
    [InlineData("/recibo/abc")]
    [InlineData("/recibo/0123456789abcdef0123456789abcdef")] // bien formado pero inexistente
    [InlineData("/recibo/0123456789ABCDEF0123456789ABCDEF")]
    public async Task Un_token_invalido_o_inexistente_da_404(string url)
    {
        var response = await CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task El_token_en_mayusculas_no_abre_el_recibo()
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(merchant);
        await PostWebhookAsync(QrPaymentBodyFor(sale));

        var response = await CreateClient().GetAsync($"/recibo/{sale.ReceiptToken.ToUpperInvariant()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>Ligas del recibo: URL absoluta (dominio configurado o el de la petición) y mensaje de WhatsApp.</summary>
public class ReceiptLinksTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    private static HttpRequest Request(string scheme = "https", string host = "localhost:7180", string pathBase = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.PathBase = pathBase;
        return context.Request;
    }

    private static ReceiptLinks Links(string? publicBaseUrl = null) =>
        new(Options.Create(new PublicSiteOptions { PublicBaseUrl = publicBaseUrl }));

    [Fact]
    public void Sin_dominio_configurado_usa_el_de_la_peticion() =>
        Assert.Equal($"https://localhost:7180/sitio/recibo/{Token}", Links().UrlFor(Request(pathBase: "/sitio"), Token));

    [Theory]
    [InlineData("https://local133.mx")]
    [InlineData("https://local133.mx/")]
    [InlineData(" https://local133.mx/ ")]
    public void Con_dominio_configurado_lo_usa(string baseUrl) =>
        Assert.Equal($"https://local133.mx/recibo/{Token}", Links(baseUrl).UrlFor(Request(host: "interno:8080"), Token));

    [Fact]
    public void El_mensaje_lleva_negocio_folio_monto_y_liga()
    {
        var share = Links("https://local133.mx").ShareFor(Request(), Token, "Tacos Don Ramón", 12, "$150.00", null);

        Assert.Equal($"Recibo de pago de Tacos Don Ramón\nVenta 12 por $150.00\nhttps://local133.mx/recibo/{Token}", share.Message);
        Assert.Equal($"https://wa.me/?text={Uri.EscapeDataString(share.Message)}", share.WhatsAppUrl);
        Assert.Equal($"/recibo/{Token}", share.Path);
        Assert.Null(share.CustomerMaskedPhone);
    }

    [Fact]
    public void Sin_nombre_de_negocio_el_mensaje_es_generico() =>
        Assert.StartsWith("Recibo de pago\nVenta 3 por $10.00\n",
            Links().ShareFor(Request(), Token, "  ", 3, "$10.00", null).Message);

    [Fact]
    public void Con_el_celular_del_cliente_ofrece_su_chat_sin_escribir_el_numero()
    {
        var share = Links().ShareFor(Request(), Token, "Tacos", 1, "$1.00", "5512345678", "/app/ventas/7/whatsapp");

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
        var share = Links().ShareFor(Request(), Token, "Tacos", 1, "$1.00", phone, path);

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
        var share = Links().ShareFor(Request(), Token, "Tacos", 1, "$1.00");

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
