using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Local133.LandingPage.Tests;

public class LandingPageTests
{
    private const string AppUrl = "https://app.example.test";

    private static WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            // Sin watcher de Sass: cada factory levantaría su propio `sass --watch`.
            builder.UseSetting("Sass:Watch", "false");
            builder.UseSetting("Landing:AppUrl", AppUrl);
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });

    [Fact]
    public async Task Portada_muestra_logo_textos_y_boton_al_portal_en_pestana_nueva()
    {
        using var factory = CreateFactory();
        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("src=\"/local133.svg", html);
        Assert.Contains("Cobra con CoDi desde&nbsp;tu", html);
        Assert.Contains("Genera un QR, tu cliente paga desde su app bancaria y el dinero llega directo a tu cuenta.", html);
        Assert.Contains($"href=\"{AppUrl}\" target=\"_blank\" rel=\"noopener\">Empieza a cobrar</a>", html);
    }

    [Theory]
    [InlineData("?codigo=ABC234", AppUrl + "/r/ABC234")]
    [InlineData("?codigo=abc-234", AppUrl + "/r/ABC234")]
    [InlineData("?codigo=ABC10I", AppUrl)]
    [InlineData("?codigo=%3Cscript%3E", AppUrl)]
    [InlineData("?codigo=", AppUrl)]
    public async Task Con_codigo_de_invitacion_el_boton_va_a_la_liga_de_invitacion(string query, string expectedHref)
    {
        using var factory = CreateFactory();
        var html = await factory.CreateClient().GetStringAsync("/" + query);

        Assert.Contains($"href=\"{expectedHref}\" target=\"_blank\" rel=\"noopener\">Empieza a cobrar</a>", html);
    }

    [Fact]
    public async Task Con_varias_palabras_pinta_la_primera_y_carga_la_animacion()
    {
        using var factory = CreateFactory();
        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("data-rotating-words=\"[&quot;local&quot;", html);
        Assert.Contains(">local</span></h1>", html);
        Assert.Contains("src=\"/js/rotating-word.js", html);
    }

    [Fact]
    public async Task Con_una_sola_palabra_no_carga_la_animacion()
    {
        using var factory = CreateFactory(
            ("Home:RotatingWords:0", "puesto"),
            ("Home:RotatingWords:1", ""),
            ("Home:RotatingWords:2", ""),
            ("Home:RotatingWords:3", ""),
            ("Home:RotatingWords:4", ""),
            ("Home:RotatingWords:5", ""));
        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("<span class=\"rotating-word\">puesto</span>", html);
        Assert.DoesNotContain("data-rotating-words", html);
        Assert.DoesNotContain("rotating-word.js", html);
    }

    [Theory]
    [InlineData("/local133.svg")]
    [InlineData("/favicon.ico")]
    [InlineData("/apple-touch-icon.png")]
    [InlineData("/js/rotating-word.js")]
    [InlineData("/css/landing.css")]
    public async Task Sirve_los_archivos_compartidos_con_Local133_Web(string path)
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
