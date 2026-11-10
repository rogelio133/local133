using System.Net;
using System.Text.RegularExpressions;
using Local133.Application.Auth;
using Local133.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>Base de las pruebas de integración: limpia la base antes de cada prueba y ayuda con formularios.</summary>
[Collection(WebCollection.Name)]
[Trait("Category", "SqlServer")]
public abstract partial class WebTestBase(Local133WebFactory factory) : IAsyncLifetime
{
    public const string Password = "Prueba1234";

    protected Local133WebFactory Factory { get; } = factory;

    public Task InitializeAsync() => Factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Cliente con cookies y sin seguir redirecciones (para revisar cada 302).</summary>
    protected HttpClient CreateClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
    });

    /// <summary>GET del formulario (para el token antiforgery y su cookie) y POST con los campos dados.</summary>
    protected static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string pageUrl, IDictionary<string, string> fields, string? postUrl = null)
    {
        var page = await client.GetAsync(pageUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        var token = AntiforgeryRegex().Match(html);
        Assert.True(token.Success, $"No se encontró el token antiforgery en {pageUrl}.");

        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value) };
        return await client.PostAsync(postUrl ?? pageUrl, new FormUrlEncodedContent(form));
    }

    protected static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = Password) =>
        PostFormAsync(client, "/Identity/Account/Login", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        });

    protected static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password = Password) =>
        PostFormAsync(client, "/Identity/Account/Register", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        });

    /// <summary>Crea un usuario con correo ya confirmado y el rol indicado, directo con UserManager.</summary>
    protected async Task CreateConfirmedUserAsync(string email, string role)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        AssertSucceeded(await users.CreateAsync(user, Password));
        AssertSucceeded(await users.AddToRoleAsync(user, role));
    }

    protected Task CreateMerchantAsync(string email) => CreateConfirmedUserAsync(email, Roles.Merchant);

    protected static string? Location(HttpResponseMessage response) =>
        response.Headers.Location is { } uri ? (uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString) : null;

    private static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryRegex();
}
