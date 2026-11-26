using System.Net;
using Local133.Domain.Legal;
using Local133.Domain.Merchants;
using Local133.IntegrationTests.Infrastructure;
using Local133.Infrastructure.Persistence;
using Local133.Web.Hosting;
using Local133.Web.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Local133.IntegrationTests.Identity.ExternalLoginTests;

namespace Local133.IntegrationTests.Identity;

/// <summary>Paso 1.4: el alta crea el Merchant en PendingBankAccount y exige aceptar Términos y Aviso de privacidad.</summary>
public class MerchantRegistrationTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private const string Email = "vendedor@prueba.mx";
    private const string ProviderEmail = "externo@prueba.mx"; // el que manda FakeExternalUser por defecto

    [Fact]
    public async Task Registro_sin_aceptar_terminos_muestra_error_y_no_crea_cuenta()
    {
        var response = await RegisterAsync(CreateClient(), Email, acceptLegal: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(LegalAcceptanceInput.NotAcceptedMessage, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await AssertNoAccountAsync(Email);
    }

    [Fact]
    public async Task Registro_crea_merchant_pendiente_de_cuenta_bancaria_con_terminos_aceptados()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await RegisterAsync(CreateClient(), Email);

        Assert.StartsWith("/Identity/Account/RegisterConfirmation", Location(response));
        await AssertMerchantCreatedAsync(Email, before);
    }

    [Fact]
    public async Task Alta_externa_sin_aceptar_terminos_muestra_error_y_no_crea_cuenta()
    {
        var client = CreateClient();
        var callback = await StartExternalLoginAsync(client);

        var response = await ConfirmExternalAsync(client, callback, ProviderEmail, acceptLegal: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(LegalAcceptanceInput.NotAcceptedMessage, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await AssertNoAccountAsync(ProviderEmail);
    }

    [Fact]
    public async Task Alta_externa_crea_merchant_pendiente_de_cuenta_bancaria_con_terminos_aceptados()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var client = CreateClient();
        var callback = await StartExternalLoginAsync(client);

        Assert.Equal("/app", Location(await ConfirmExternalAsync(client, callback, ProviderEmail)));
        await AssertMerchantCreatedAsync(ProviderEmail, before);
    }

    [Fact]
    public async Task Formularios_de_alta_muestran_la_casilla_con_ligas_a_los_documentos()
    {
        var client = CreateClient();
        var register = await HtmlAsync(client, "/Identity/Account/Register");
        var external = await HtmlAsync(client, await StartExternalLoginAsync(client));
        // Los textos legales viven en la portada (App:LandingUrl).
        var site = Factory.Services.GetRequiredService<IOptions<PublicSiteOptions>>().Value;

        foreach (var html in new[] { register, external })
        {
            Assert.Contains("name=\"Input.AcceptLegal\"", html);
            Assert.Contains("data-val-mustbetrue=", html);
            Assert.Contains($"href=\"{site.TermsUrl}\" target=\"_blank\"", html);
            Assert.Contains($"href=\"{site.PrivacyNoticeUrl}\" target=\"_blank\"", html);
        }
    }

    // ---- Nombre en el registro (4.0) ----

    [Fact]
    public async Task Registro_guarda_el_nombre_normalizado_y_saluda_en_el_correo_de_confirmacion()
    {
        var response = await RegisterAsync(CreateClient(), Email, ownerName: "  María   O’Brien  ");

        Assert.StartsWith("/Identity/Account/RegisterConfirmation", Location(response));
        Assert.Equal("María O'Brien", await OwnerNameAsync(Email));
        var confirm = WebUtility.HtmlDecode(Factory.Emails.Last(Email).Html);
        Assert.Contains("¡Hola, María!", confirm);
        Assert.Contains("Te damos la bienvenida a Local133", confirm);
    }

    [Fact]
    public async Task Alta_externa_guarda_el_nombre()
    {
        var client = CreateClient();
        var callback = await StartExternalLoginAsync(client);

        Assert.Equal("/app", Location(await ConfirmExternalAsync(client, callback, ProviderEmail, ownerName: "José Ángel Ruiz")));
        Assert.Equal("José Ángel Ruiz", await OwnerNameAsync(ProviderEmail));
        Assert.Contains("Hola, José</h1>", await HtmlAsync(client, "/app"));
    }

    [Fact]
    public async Task Alta_externa_propone_el_nombre_del_proveedor()
    {
        var client = CreateClient();
        var html = await HtmlAsync(client, await StartExternalLoginAsync(client));

        Assert.Contains("value=\"Usuario Externo\"", html); // ClaimTypes.Name de FakeExternalAuthHandler
    }

    [Theory]
    [InlineData("", OwnerNameMessages.Required)]
    [InlineData("   ", OwnerNameMessages.Required)]
    [InlineData("María 123", OwnerNameMessages.Invalid)]
    [InlineData("...", OwnerNameMessages.Invalid)]
    [InlineData("<script>", OwnerNameMessages.Invalid)]
    public async Task Registro_con_nombre_vacio_o_invalido_muestra_error_y_no_crea_cuenta(string ownerName, string message)
    {
        var response = await RegisterAsync(CreateClient(), Email, ownerName: ownerName);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await AssertNoAccountAsync(Email);
    }

    [Fact]
    public async Task Registro_con_nombre_de_mas_de_100_caracteres_muestra_error()
    {
        var response = await RegisterAsync(CreateClient(), Email, ownerName: new string('a', PersonName.MaxLength + 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Tu nombre puede tener hasta 100 caracteres.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await AssertNoAccountAsync(Email);
    }

    [Fact]
    public async Task Alta_externa_sin_nombre_muestra_error_y_no_crea_cuenta()
    {
        var client = CreateClient();
        var callback = await StartExternalLoginAsync(client);

        var response = await ConfirmExternalAsync(client, callback, ProviderEmail, ownerName: "");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(OwnerNameMessages.Required, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await AssertNoAccountAsync(ProviderEmail);
    }

    [Fact]
    public async Task Formularios_de_alta_piden_el_nombre_con_validacion_en_cliente()
    {
        var client = CreateClient();
        var register = await HtmlAsync(client, "/Identity/Account/Register");
        var external = await HtmlAsync(client, await StartExternalLoginAsync(client));

        foreach (var html in new[] { register, external })
        {
            Assert.Contains("name=\"Input.OwnerName\"", html);
            Assert.Contains("autocomplete=\"name\"", html);
            Assert.Contains($"data-val-required=\"{OwnerNameMessages.Required}\"", html);
            Assert.Contains($"data-val-regex-pattern=\"{PersonName.Pattern}\"", html);
            Assert.Contains("data-val-length-max=\"100\"", html);
            Assert.Contains("data-val-trim", html);
            // Es el primer campo del formulario.
            Assert.True(html.IndexOf("name=\"Input.OwnerName\"", StringComparison.Ordinal)
                < html.IndexOf("name=\"Input.Email\"", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AccountRegistration_rechaza_un_nombre_invalido_sin_crear_nada()
    {
        using var scope = Factory.Services.CreateScope();
        var user = new Local133.Infrastructure.Identity.ApplicationUser { UserName = Email, Email = Email };

        var result = await scope.ServiceProvider.GetRequiredService<AccountRegistration>().CreateMerchantAsync(user, "123", Password);

        Assert.False(result.Succeeded);
        Assert.Equal(OwnerNameMessages.Invalid, Assert.Single(result.Errors).Description);
        await AssertNoAccountAsync(Email);
    }

    private async Task<string?> OwnerNameAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return await db.Merchants.Where(m => m.UserId == userId).Select(m => m.OwnerName).SingleAsync();
    }

    private async Task AssertMerchantCreatedAsync(string email, DateTimeOffset notBefore)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        var merchant = await db.Merchants.SingleAsync(m => m.UserId == user.Id);

        Assert.Equal(MerchantStatus.PendingBankAccount, merchant.Status);
        Assert.True(merchant.CreatedAt >= notBefore);

        Assert.NotNull(user.TermsAcceptedAt);
        Assert.True(user.TermsAcceptedAt >= notBefore);
        Assert.Equal(user.TermsAcceptedAt, user.PrivacyNoticeAcceptedAt);
        Assert.Equal(LegalDocuments.CurrentTermsVersion, user.TermsVersion);
        Assert.Equal(LegalDocuments.CurrentPrivacyNoticeVersion, user.PrivacyNoticeVersion);
    }

    private async Task AssertNoAccountAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        Assert.False(await db.Merchants.AnyAsync());
    }
}
