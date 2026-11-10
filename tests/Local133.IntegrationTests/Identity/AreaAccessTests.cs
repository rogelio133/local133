using System.Net;
using Local133.Application.Auth;
using Local133.IntegrationTests.Infrastructure;

namespace Local133.IntegrationTests.Identity;

/// <summary>Áreas Merchant (/app) y Admin (/admin) accesibles solo con el rol correcto.</summary>
public class AreaAccessTests(Local133WebFactory factory) : WebTestBase(factory)
{
    [Theory]
    [InlineData("/app")]
    [InlineData("/app/cobrar")]
    [InlineData("/admin")]
    [InlineData("/admin/clientes")]
    [InlineData("/Identity/Account/Manage")]
    public async Task Sin_sesion_redirige_al_login(string path)
    {
        var client = CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login?ReturnUrl=", Location(response));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Register")]
    [InlineData("/Identity/Account/ForgotPassword")]
    public async Task Paginas_publicas_responden_sin_sesion(string path)
    {
        var response = await CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Merchant_entra_a_app_pero_no_a_admin()
    {
        await CreateMerchantAsync("vendedor@prueba.mx");
        var client = CreateClient();

        var login = await LoginAsync(client, "vendedor@prueba.mx");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/app", Location(login));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app")).StatusCode);

        var admin = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.StartsWith("/Identity/Account/AccessDenied", Location(admin));
    }

    [Fact]
    public async Task Admin_entra_a_admin_pero_no_a_app()
    {
        await CreateConfirmedUserAsync("admin@prueba.mx", Roles.Admin);
        var client = CreateClient();

        var login = await LoginAsync(client, "admin@prueba.mx");
        Assert.Equal("/admin", Location(login));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);

        var app = await client.GetAsync("/app");
        Assert.StartsWith("/Identity/Account/AccessDenied", Location(app));
    }

    [Fact]
    public async Task Login_respeta_returnUrl_local()
    {
        await CreateMerchantAsync("vendedor@prueba.mx");
        var client = CreateClient();

        var response = await PostFormAsync(
            client,
            "/Identity/Account/Login?ReturnUrl=%2Fapp%2Fventas",
            new Dictionary<string, string> { ["Input.Email"] = "vendedor@prueba.mx", ["Input.Password"] = Password });

        Assert.Equal("/app/ventas", Location(response));
    }

    [Fact]
    public async Task Api_sin_sesion_responde_401_sin_redirigir()
    {
        var response = await CreateClient().GetAsync(Local133WebFactory.ProtectedApiPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Sitio_sin_sesion_con_challenge_redirige_al_login()
    {
        var response = await CreateClient().GetAsync(Local133WebFactory.ProtectedSitePath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", Location(response));
    }

    [Fact]
    public async Task Api_inexistente_sigue_en_404_problem_details()
    {
        var response = await CreateClient().GetAsync("/api/nada");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/Identity/Account/Manage/TwoFactorAuthentication")]
    [InlineData("/Identity/Account/Manage/PersonalData")]
    [InlineData("/Identity/Account/Manage/DeletePersonalData")]
    [InlineData("/Identity/Account/LoginWith2fa")]
    public async Task Paginas_no_scaffoldeadas_no_existen(string path)
    {
        await CreateMerchantAsync("vendedor@prueba.mx");
        var client = CreateClient();
        await LoginAsync(client, "vendedor@prueba.mx");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
