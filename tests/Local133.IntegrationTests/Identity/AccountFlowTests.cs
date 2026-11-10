using System.Net;
using Local133.IntegrationTests.Infrastructure;

namespace Local133.IntegrationTests.Identity;

/// <summary>Registro con verificación de correo, login, logout y recuperación de contraseña.</summary>
public class AccountFlowTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private const string Email = "nuevo@prueba.mx";

    [Fact]
    public async Task Registro_exige_confirmar_correo_y_luego_entra_como_merchant()
    {
        var client = CreateClient();

        var register = await RegisterAsync(client, Email);
        Assert.Equal(HttpStatusCode.Redirect, register.StatusCode);
        Assert.StartsWith("/Identity/Account/RegisterConfirmation", Location(register));

        // Sin confirmar: la contraseña es correcta pero no se permite entrar.
        var early = await LoginAsync(client, Email);
        Assert.Equal(HttpStatusCode.OK, early.StatusCode);
        Assert.Contains("Confirma tu correo", WebUtility.HtmlDecode(await early.Content.ReadAsStringAsync()));

        var email = Factory.Emails.Last(Email);
        Assert.Contains("Confirma tu correo", email.Subject);
        var confirm = await client.GetAsync(email.Link);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Contains("Tu correo quedó confirmado", WebUtility.HtmlDecode(await confirm.Content.ReadAsStringAsync()));

        var login = await LoginAsync(client, Email);
        Assert.Equal("/app", Location(login));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app")).StatusCode);
    }

    [Fact]
    public async Task Registro_con_correo_duplicado_muestra_error_en_espanol()
    {
        await CreateMerchantAsync(Email);

        var response = await RegisterAsync(CreateClient(), Email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ya existe una cuenta con el correo", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Registro_con_contrasena_sin_numero_muestra_error_en_espanol()
    {
        var response = await RegisterAsync(CreateClient(), Email, "sinnumeros");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("al menos un número", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Login_con_contrasena_incorrecta_no_entra()
    {
        await CreateMerchantAsync(Email);

        var response = await LoginAsync(CreateClient(), Email, "Incorrecta99");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Correo o contraseña incorrectos", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Logout_cierra_la_sesion()
    {
        await CreateMerchantAsync(Email);
        var client = CreateClient();
        await LoginAsync(client, Email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app")).StatusCode);

        var logout = await PostFormAsync(client, "/app", new Dictionary<string, string>(), "/Identity/Account/Logout?returnUrl=%2F");
        Assert.Equal("/", Location(logout));

        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/app")).StatusCode);
    }

    [Fact]
    public async Task Recuperar_contrasena_permite_entrar_con_la_nueva()
    {
        await CreateMerchantAsync(Email);
        var client = CreateClient();

        var forgot = await PostFormAsync(client, "/Identity/Account/ForgotPassword",
            new Dictionary<string, string> { ["Input.Email"] = Email });
        Assert.Equal("/Identity/Account/ForgotPasswordConfirmation", Location(forgot));

        var resetLink = Factory.Emails.Last(Email).Link;
        const string newPassword = "NuevaClave2026";
        var resetPage = await client.GetAsync(resetLink);
        Assert.Equal(HttpStatusCode.OK, resetPage.StatusCode);
        var code = System.Text.RegularExpressions.Regex.Match(
            await resetPage.Content.ReadAsStringAsync(), "id=\"Input_Code\" name=\"Input.Code\" value=\"([^\"]+)\"").Groups[1].Value;

        var reset = await PostFormAsync(client, resetLink, new Dictionary<string, string>
        {
            ["Input.Email"] = Email,
            ["Input.Password"] = newPassword,
            ["Input.ConfirmPassword"] = newPassword,
            ["Input.Code"] = WebUtility.HtmlDecode(code),
        }, "/Identity/Account/ResetPassword");
        Assert.Equal("/Identity/Account/ResetPasswordConfirmation", Location(reset));

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(CreateClient(), Email)).StatusCode); // la vieja ya no sirve
        Assert.Equal("/app", Location(await LoginAsync(CreateClient(), Email, newPassword)));
    }

    [Fact]
    public async Task Recuperar_contrasena_de_correo_inexistente_no_lo_revela()
    {
        var forgot = await PostFormAsync(CreateClient(), "/Identity/Account/ForgotPassword",
            new Dictionary<string, string> { ["Input.Email"] = "nadie@prueba.mx" });

        Assert.Equal("/Identity/Account/ForgotPasswordConfirmation", Location(forgot));
        Assert.Empty(Factory.Emails.Sent);
    }
}
