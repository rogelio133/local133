using Local133.Web.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Local133.IntegrationTests.Identity;

/// <summary>Google y Facebook solo se registran con credenciales. No usa base de datos: corre en CI.</summary>
public class ExternalProvidersRegistrationTests
{
    [Fact]
    public async Task Sin_credenciales_no_hay_proveedores_externos()
    {
        Assert.Empty(await ExternalSchemesAsync(new Dictionary<string, string?>()));
    }

    [Fact]
    public async Task Credenciales_incompletas_no_registran_el_proveedor()
    {
        Assert.Empty(await ExternalSchemesAsync(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "id",
            ["Authentication:Facebook:AppSecret"] = "secreto",
        }));
    }

    [Fact]
    public async Task Con_credenciales_se_registran_Google_y_Facebook()
    {
        var schemes = await ExternalSchemesAsync(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "id",
            ["Authentication:Google:ClientSecret"] = "secreto",
            ["Authentication:Facebook:AppId"] = "id",
            ["Authentication:Facebook:AppSecret"] = "secreto",
        });
        Assert.Equal(["Facebook", "Google"], schemes.Select(s => s.DisplayName).Order());
    }

    [Fact]
    public async Task Google_siempre_muestra_el_selector_de_cuentas()
    {
        await using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "id",
            ["Authentication:Google:ClientSecret"] = "secreto",
        });
        var options = provider.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(GoogleDefaults.AuthenticationScheme);
        var scheme = (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(GoogleDefaults.AuthenticationScheme))!;
        var http = new DefaultHttpContext();

        await options.Events.RedirectToAuthorizationEndpoint(new RedirectContext<OAuthOptions>(
            http, scheme, options, new AuthenticationProperties(), "https://accounts.google.com/o/oauth2/v2/auth?client_id=id"));

        Assert.Equal(StatusCodes.Status302Found, http.Response.StatusCode);
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth?client_id=id&prompt=select_account", http.Response.Headers.Location.ToString());
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppIdentity(configuration, new FakeEnvironment());
        return services.BuildServiceProvider();
    }

    private static async Task<IReadOnlyList<AuthenticationScheme>> ExternalSchemesAsync(Dictionary<string, string?> settings)
    {
        await using var provider = BuildProvider(settings);

        // Los externos son los que tienen nombre para mostrar (los de Identity no).
        var all = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        return all.Where(s => s.DisplayName is not null).ToList();
    }

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Local133.Web";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Development";
    }
}
