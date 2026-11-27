using System.Net;
using Local133.Application.Merchants;
using Local133.Application.Storage;
using Local133.Domain.Sales;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Products;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Sales;

/// <summary>
/// 5.10: recibo con marca. Según el plan del vendedor al abrir la liga: en Pro (y prueba, gracia, onboarding) lleva el
/// logo del negocio; en el gratuito, «Cobrado con Local133» con liga a la portada y el código de invitación.
/// </summary>
public class BrandedReceiptTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    [Fact]
    public async Task En_Pro_con_logo_el_recibo_lleva_el_logo_y_no_el_pie_de_Local133()
    {
        var sale = await PaidSaleAsync();
        await SeedTrialAsync(Email);
        var logoKey = await StoreLogoAsync(Email);

        var client = CreateClient();
        var html = await GetHtmlAsync(client, $"/recibo/{sale.ReceiptToken}");

        var logoPath = $"/recibo/{sale.ReceiptToken}/logo?v={MerchantImageRules.Version(logoKey)}";
        Assert.Contains($"src=\"{logoPath}\"", html);
        Assert.Contains("data-receipt-logo", html);
        Assert.DoesNotContain("data-receipt-powered-by", html);

        var logo = await client.GetAsync(logoPath);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.True(logo.Headers.CacheControl?.Private);
        Assert.Equal("noindex, nofollow", string.Join(",", logo.Headers.GetValues("X-Robots-Tag")));
    }

    [Fact]
    public async Task En_Pro_sin_logo_no_hay_logo_ni_pie()
    {
        var sale = await PaidSaleAsync();
        await SeedTrialAsync(Email);

        var client = CreateClient();
        var html = await GetHtmlAsync(client, $"/recibo/{sale.ReceiptToken}");

        Assert.DoesNotContain("data-receipt-logo", html);
        Assert.DoesNotContain("data-receipt-powered-by", html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/recibo/{sale.ReceiptToken}/logo")).StatusCode);
    }

    [Fact]
    public async Task En_el_gratuito_dice_Cobrado_con_Local133_con_el_codigo_y_no_sirve_el_logo()
    {
        var sale = await PaidSaleAsync();
        await SeedExpiredPlanAsync(Email);
        await StoreLogoAsync(Email);
        var code = (await WithDbAsync(db => MerchantOfAsync(db, Email))).ReferralCode;

        var client = CreateClient();
        var html = await GetHtmlAsync(client, $"/recibo/{sale.ReceiptToken}");

        Assert.Contains("data-receipt-powered-by", html);
        Assert.Contains($"?codigo={code}\" target=\"_blank\" rel=\"noopener\"", html);
        Assert.Contains("Cobrado con <strong>Local133</strong>", html);
        Assert.DoesNotContain("data-receipt-logo", html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/recibo/{sale.ReceiptToken}/logo")).StatusCode);
    }

    [Fact]
    public async Task El_logo_de_una_venta_sin_pagar_o_con_token_invalido_es_404()
    {
        var merchant = await ActiveMerchantAsync(Email);
        await StoreLogoAsync(Email);
        var pending = await ChargeAsync(merchant);

        var client = CreateClient();
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
