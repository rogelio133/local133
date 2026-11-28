using System.Net;
using System.Text.RegularExpressions;
using Local133.Application.Auth;
using Local133.Infrastructure.Identity;
using Local133.Web.Identity;
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

    /// <summary>Nombre de la persona con el que se dan de alta los vendedores de prueba (4.0).</summary>
    public const string OwnerName = "María López";

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

    /// <summary>
    /// Cliente como el servidor de la portada: con la llave de <c>/api/public</c> (2026-11-27). Sin
    /// <paramref name="client"/> crea uno nuevo (<see cref="CreateClient"/>).
    /// </summary>
    protected HttpClient LandingClient(HttpClient? client = null)
    {
        client ??= CreateClient();
        client.DefaultRequestHeaders.Remove(Local133.Web.PublicPages.PublicPagesApi.KeyHeader);
        client.DefaultRequestHeaders.Add(Local133.Web.PublicPages.PublicPagesApi.KeyHeader, Local133WebFactory.PublicApiKey);
        return client;
    }

    /// <summary>GET del formulario (para el token antiforgery y su cookie) y POST con los campos dados.</summary>
    protected static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string pageUrl, IDictionary<string, string> fields, string? postUrl = null)
    {
        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = await GetAntiforgeryTokenAsync(client, pageUrl) };
        return await client.PostAsync(postUrl ?? pageUrl, new FormUrlEncodedContent(form));
    }

    /// <summary>Archivo de un formulario multipart (<c>enctype="multipart/form-data"</c>).</summary>
    protected sealed record FormFile(string FieldName, string FileName, byte[] Content, string ContentType = "image/jpeg");

    /// <summary>Como <see cref="PostFormAsync"/>, pero multipart y con archivos.</summary>
    protected static async Task<HttpResponseMessage> PostMultipartAsync(
        HttpClient client, string pageUrl, IDictionary<string, string> fields, IEnumerable<FormFile>? files = null,
        string? postUrl = null)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(await GetAntiforgeryTokenAsync(client, pageUrl)), "__RequestVerificationToken" },
        };
        foreach (var (name, value) in fields)
        {
            content.Add(new StringContent(value), name);
        }

        foreach (var file in files ?? [])
        {
            var part = new ByteArrayContent(file.Content);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(part, file.FieldName, file.FileName);
        }

        return await client.PostAsync(postUrl ?? pageUrl, content);
    }

    /// <summary>GET de la página (para el token antiforgery y su cookie); devuelve el primer token de la página.</summary>
    protected static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string pageUrl)
    {
        var page = await client.GetAsync(pageUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        var token = AntiforgeryRegex().Match(html);
        Assert.True(token.Success, $"No se encontró el token antiforgery en {pageUrl}.");
        return WebUtility.HtmlDecode(token.Groups[1].Value);
    }

    protected static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = Password) =>
        PostFormAsync(client, "/Identity/Account/Login", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        });

    protected static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string email, string password = Password, bool acceptLegal = true, string ownerName = OwnerName) =>
        PostFormAsync(client, "/Identity/Account/Register", new Dictionary<string, string>
        {
            ["Input.OwnerName"] = ownerName,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
            ["Input.AcceptLegal"] = acceptLegal ? "true" : "false",
        });

    /// <summary>
    /// Crea un usuario con correo ya confirmado y el rol indicado. Los vendedores se crean con
    /// <see cref="AccountRegistration"/> (igual que el registro real: con su Merchant y términos aceptados).
    /// </summary>
    protected async Task CreateConfirmedUserAsync(string email, string role)
    {
        using var scope = Factory.Services.CreateScope();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        if (role == Roles.Merchant)
        {
            user.AcceptLegalDocuments(DateTimeOffset.UtcNow);
            AssertSucceeded(await scope.ServiceProvider.GetRequiredService<AccountRegistration>().CreateMerchantAsync(user, OwnerName, Password));
            return;
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        AssertSucceeded(await users.CreateAsync(user, Password));
        AssertSucceeded(await users.AddToRoleAsync(user, role));
    }

    protected Task CreateMerchantAsync(string email) => CreateConfirmedUserAsync(email, Roles.Merchant);

    /// <summary>Correos «Tu cuenta ya está activa» enviados (capturados en memoria).</summary>
    protected IReadOnlyList<SentEmail> ActivationEmails() =>
        Factory.Emails.Sent.Where(e => e.Subject == Local133.Infrastructure.Email.MerchantEmails.BankAccountActiveSubject).ToList();

    /// <summary>Liga del botón del correo de cuenta activa (base de respaldo de desarrollo: las pruebas vacían App:PublicBaseUrl).</summary>
    protected const string ActivationEmailLink = "http://localhost:5231/app/cobrar";

    protected static string? Location(HttpResponseMessage response) =>
        response.Headers.Location is { } uri ? (uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString) : null;

    private static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryRegex();
}
