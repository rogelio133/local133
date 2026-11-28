using System.Net;
using System.Net.Http.Json;
using Local133.Application.Merchants;
using Local133.Application.Storage;
using Local133.Domain.Sales;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Products;
using Local133.Web.PublicPages;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Sales;

/// <summary>
/// 5.10: recibo con marca. Según el plan del vendedor al abrir la liga: en Pro (y prueba, gracia, onboarding) lleva el
/// logo del negocio (servido por el portal); en Básico y vencido, el código de invitación para «Cobrado con Local133».
/// Desde el 2026-11-27 la portada dibuja el recibo con <c>GET /api/public/recibos/{token}</c>.
/// </summary>
public class BrandedReceiptTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    [Fact]
    public async Task En_Pro_con_logo_el_recibo_lleva_el_logo_y_no_el_codigo()
    {
        var sale = await PaidSaleAsync();
        await SeedTrialAsync(Email);
        var logoKey = await StoreLogoAsync(Email);

        var client = LandingClient();
        var receipt = await client.GetFromJsonAsync<PublicReceipt>(PublicPagesApi.Receipt(sale.ReceiptToken));

        var logoPath = $"/recibo/{sale.ReceiptToken}/logo?v={MerchantImageRules.Version(logoKey)}";
        Assert.NotNull(receipt);
        Assert.Equal(new Uri(client.BaseAddress!, logoPath).AbsoluteUri, receipt.LogoUrl);
        Assert.Null(receipt.ReferralCode);

        var logo = await client.GetAsync(logoPath);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.True(logo.Headers.CacheControl?.Private);
        Assert.Equal("noindex, nofollow", string.Join(",", logo.Headers.GetValues("X-Robots-Tag")));
    }

    [Fact]
    public async Task En_Pro_sin_logo_no_hay_logo_ni_codigo()
    {
        var sale = await PaidSaleAsync();
        await SeedTrialAsync(Email);

        var client = LandingClient();
        var receipt = await client.GetFromJsonAsync<PublicReceipt>(PublicPagesApi.Receipt(sale.ReceiptToken));

        Assert.NotNull(receipt);
        Assert.Null(receipt.LogoUrl);
        Assert.Null(receipt.ReferralCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/recibo/{sale.ReceiptToken}/logo")).StatusCode);
    }

    [Fact]
    public async Task Vencido_lleva_el_codigo_para_Cobrado_con_Local133_y_no_sirve_el_logo()
    {
        var sale = await PaidSaleAsync();
        await SeedExpiredPlanAsync(Email);
        await StoreLogoAsync(Email);
        var code = (await WithDbAsync(db => MerchantOfAsync(db, Email))).ReferralCode;

        var client = LandingClient();
        var receipt = await client.GetFromJsonAsync<PublicReceipt>(PublicPagesApi.Receipt(sale.ReceiptToken));

        Assert.NotNull(receipt);
        Assert.Equal(code, receipt.ReferralCode);
        Assert.Null(receipt.LogoUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/recibo/{sale.ReceiptToken}/logo")).StatusCode);
    }

    [Fact]
    public async Task El_logo_de_una_venta_sin_pagar_o_con_token_invalido_es_404()
    {
        var merchant = await ActiveMerchantAsync(Email);
        await StoreLogoAsync(Email);
        var pending = await ChargeAsync(merchant);

        var client = LandingClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/recibo/{pending.ReceiptToken}/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/recibo/no-es-token/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/recibo/{new string('a', 32)}/logo")).StatusCode);
    }

    /// <summary>Venta pagada por webhook del vendedor <see cref="SaleFlowTestBase.Email"/>.</summary>
    private async Task<Sale> PaidSaleAsync()
    {
        var merchant = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(merchant);
        await PostWebhookAsync(QrPaymentBodyFor(sale));
        return sale;
    }

    /// <summary>Guarda un logo en el almacenamiento y lo asigna al vendedor; devuelve la llave.</summary>
    private async Task<string> StoreLogoAsync(string email)
    {
        var merchantId = await MerchantIdAsync(email);
        var key = MerchantImageRules.NewKey(merchantId, MerchantImageKind.Logo);
        using (var scope = Factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            await using var content = new MemoryStream(TestImages.Solid(64, 64, SkiaSharp.SKEncodedImageFormat.Png));
            await storage.SaveAsync(MerchantImageRules.FileKey(MerchantImageKind.Logo, key), content, MerchantImageRules.LogoContentType);
        }

        await UpdateMerchantAsync(email, m => m.ReplaceLogo(key));
        return key;
    }
}
