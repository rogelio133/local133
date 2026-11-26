using System.Net;
using System.Text.RegularExpressions;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;

namespace Local133.IntegrationTests.Merchants;

/// <summary>
/// «Más» (/app/mas) con tarjetas (ajustes 2026-11-26, punto 11): cada tarjeta liga a su pantalla, «Contraseña y acceso»
/// lleva a «Mi cuenta» y el QR del puesto sigue apagado.
/// </summary>
public class MoreTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string Url = "/app/mas";

    public static TheoryData<string, string> Cards => new()
    {
        { "data-more-profile", "/app/mi-informacion" },
        { "data-more-plan", "/app/plan" },
        { "data-more-products", "/app/productos" },
        { "data-more-storefront", "/app/tienda" },
        { "data-more-bank-account", "/app/cuenta-bancaria" },
        { "data-more-invite", "/app/invita" },
        { "data-more-account", "/Identity/Account/Manage/ChangePassword" },
    };

    [Theory]
    [MemberData(nameof(Cards))]
    public async Task Cada_tarjeta_liga_a_su_pantalla(string attribute, string href)
    {
        var client = await MerchantClientAsync(Email);

        var html = await GetHtmlAsync(client, Url);

        var card = Regex.Match(html, $"<a class=\"more-card [^\"]*\" href=\"([^\"]*)\" {attribute}>");
        Assert.True(card.Success, $"No hay tarjeta con {attribute}.");
        Assert.Equal(href, card.Groups[1].Value);
    }

    [Fact]
    public async Task Las_tarjetas_van_en_dos_columnas_y_tres_desde_md()
    {
        var client = await MerchantClientAsync(Email);

        var html = await GetHtmlAsync(client, Url);

        Assert.Matches("<ul class=\"row row-cols-2 row-cols-md-3 [^\"]*\" data-more-cards>", html);
        Assert.DoesNotContain("list-group", html);
    }

    [Fact]
    public async Task El_QR_del_puesto_sigue_apagado()
    {
        var client = await MerchantClientAsync(Email);

        var html = await GetHtmlAsync(client, Url);

        var card = Regex.Match(html, "<(\\w+) class=\"more-card is-disabled\" data-more-static-qr>(.*?)</div>", RegexOptions.Singleline);
        Assert.True(card.Success);
        Assert.Equal("div", card.Groups[1].Value);
        Assert.Contains("data-coming-soon", card.Groups[2].Value);
    }

    [Fact]
    public async Task Contrasena_y_acceso_abre_cambiar_contrasena()
    {
        var client = await MerchantClientAsync(Email);

        var response = await client.GetAsync("/Identity/Account/Manage/ChangePassword");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"change-password\"", await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> MerchantClientAsync(string email)
    {
        await CreateMerchantAsync(email);
        var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, email)).StatusCode);
        return client;
    }
}
