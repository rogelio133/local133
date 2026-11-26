using System.Net;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Local133.IntegrationTests.Identity.ExternalLoginTests;

namespace Local133.IntegrationTests.Legal;

/// <summary>
/// Términos y Aviso de privacidad viven en la portada (Local133.LandingPage) desde el 2026-11-25; su contenido se prueba
/// en <c>Local133.LandingPage.Tests/LegalPagesTests</c>. El portal solo enlaza a ellos y redirige sus rutas viejas.
/// </summary>
public class LegalLinksTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private PublicSiteOptions Site => Factory.Services.GetRequiredService<IOptions<PublicSiteOptions>>().Value;

    [Theory]
    [InlineData("/terminos", PublicSiteOptions.TermsPath)]
    [InlineData("/aviso-de-privacidad", PublicSiteOptions.PrivacyNoticePath)]
    public async Task Las_rutas_viejas_del_portal_redirigen_a_la_portada(string url, string landingPath)
    {
        var response = await CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        // Absoluta: Location() del WebTestBase recorta el host.
        Assert.Equal(Site.LandingUrl.TrimEnd('/') + landingPath, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task El_pie_del_portal_enlaza_a_los_textos_de_la_portada()
    {
        var html = await HtmlAsync(CreateClient(), "/Identity/Account/Login");

        Assert.Contains($"href=\"{Site.TermsUrl}\">Términos y condiciones</a>", html);
        Assert.Contains($"href=\"{Site.PrivacyNoticeUrl}\">Aviso de privacidad</a>", html);
        Assert.DoesNotContain("href=\"/terminos\"", html);
        Assert.DoesNotContain("href=\"/aviso-de-privacidad\"", html);
    }
}
