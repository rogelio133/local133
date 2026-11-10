using System.Net;
using Local133.Domain.Legal;
using Local133.Domain.Merchants;
using Local133.IntegrationTests.Infrastructure;
using Local133.Infrastructure.Persistence;
using Local133.Web.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

        foreach (var html in new[] { register, external })
        {
            Assert.Contains("name=\"Input.AcceptLegal\"", html);
            Assert.Contains("data-val-mustbetrue=", html);
            Assert.Contains("href=\"/terminos\"", html);
            Assert.Contains("href=\"/aviso-de-privacidad\"", html);
        }
    }

    [Theory]
    [InlineData("/terminos", "Términos y condiciones")]
    [InlineData("/aviso-de-privacidad", "Aviso de privacidad")]
    public async Task Paginas_legales_son_publicas(string url, string title)
    {
        var html = await HtmlAsync(CreateClient(), url);
        Assert.Contains($"<h1 class=\"h3\">{title}</h1>", html);
    }

    private async Task AssertMerchantCreatedAsync(string email, DateTimeOffset notBefore)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        var merchant = await db.Merchants.SingleAsync(m => m.UserId == user.Id);

        Assert.Equal(MerchantStatus.PendingBankAccount, merchant.Status);
        Assert.Null(merchant.CommissionRateOverride);
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
