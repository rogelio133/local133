using System.Net;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Home;

/// <summary>Portada: la última palabra del título rota entre las de <c>Home:RotatingWords</c> (ajustes UI 2026-11-17).</summary>
public class HomePageTests(Local133WebFactory factory) : WebTestBase(factory)
{
    [Fact]
    public async Task El_titulo_trae_la_primera_palabra_y_la_lista_para_rotar()
    {
        // Por índice, como en user-secrets o App Settings; los índices sobrantes en "" acortan la lista de appsettings.
        var settings = new Dictionary<string, string?>
        {
            ["Home:RotatingWords:0"] = "tienda",
            ["Home:RotatingWords:1"] = "kiosko",
        };
        for (var i = 2; i < 20; i++)
        {
            settings[$"Home:RotatingWords:{i}"] = "";
        }
        using var site = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings)));

        var html = await GetHomeAsync(site);

        Assert.Contains("desde&nbsp;tu <span class=\"rotating-word\" data-rotating-words=\"[&quot;tienda&quot;,&quot;kiosko&quot;]\">tienda</span>", html);
        Assert.Contains("/js/rotating-word", html);
    }

    [Fact]
    public async Task Sin_palabras_configuradas_dice_local_y_no_rota()
    {
        using var site = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<HomeHeroOptions>(options => options.RotatingWords = [])));

        var html = await GetHomeAsync(site);

        Assert.Contains("desde&nbsp;tu <span class=\"rotating-word\">local</span>", html);
        Assert.DoesNotContain("data-rotating-words", html);
        Assert.DoesNotContain("/js/rotating-word", html);
    }

    private static async Task<string> GetHomeAsync(WebApplicationFactory<Program> site)
    {
        var client = site.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
