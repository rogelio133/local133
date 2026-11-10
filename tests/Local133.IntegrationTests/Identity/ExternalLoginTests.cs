using System.Net;
using Local133.Application.Auth;
using Local133.IntegrationTests.Infrastructure;
using Local133.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Identity;

/// <summary>Login y alta con proveedor externo (falso, en lugar de Google/Facebook) y ligado desde Manage.</summary>
public class ExternalLoginTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private const string ProviderEmail = "externo@prueba.mx";
    private const string LoginUrl = "/Identity/Account/Login";
    private const string ManageUrl = "/Identity/Account/Manage/ExternalLogins";

    [Fact]
    public async Task Login_y_registro_muestran_el_boton_del_proveedor()
    {
        var client = CreateClient();
        Assert.Contains("Entrar con Proveedor", await HtmlAsync(client, LoginUrl));
        Assert.Contains("Regístrate con Proveedor", await HtmlAsync(client, "/Identity/Account/Register"));
    }

    [Fact]
    public async Task Usuario_nuevo_que_conserva_el_correo_entra_directo_como_merchant()
    {
        var client = CreateClient();

        var callback = await StartExternalLoginAsync(client);
        var form = await HtmlAsync(client, callback);
        Assert.Contains($"value=\"{ProviderEmail}\"", form);

        var confirm = await ConfirmExternalAsync(client, callback, ProviderEmail);
        Assert.Equal("/app", Location(confirm));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app")).StatusCode);
        Assert.Empty(Factory.Emails.Sent);

        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(ProviderEmail);
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.False(await users.HasPasswordAsync(user));
        Assert.True(await users.IsInRoleAsync(user, Roles.Merchant));
        var login = Assert.Single(await users.GetLoginsAsync(user));
        Assert.Equal(FakeExternalAuthHandler.SchemeName, login.LoginProvider);
    }

    [Fact]
    public async Task Usuario_nuevo_que_cambia_el_correo_debe_confirmarlo()
    {
        const string otherEmail = "otro@prueba.mx";
        var client = CreateClient();

        var callback = await StartExternalLoginAsync(client);
        var confirm = await ConfirmExternalAsync(client, callback, otherEmail);
        Assert.StartsWith("/Identity/Account/RegisterConfirmation", Location(confirm));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/app")).StatusCode);

        // Sin confirmar, el proveedor no deja entrar.
        var early = await client.GetAsync(await StartExternalLoginAsync(client));
        Assert.Equal(LoginUrl, Location(early));
        Assert.Contains("Confirma tu correo", await HtmlAsync(client, LoginUrl));

        var email = Factory.Emails.Last(otherEmail);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(email.Link)).StatusCode);

        var login = await client.GetAsync(await StartExternalLoginAsync(client));
        Assert.Equal("/app", Location(login));
    }

    [Fact]
    public async Task Correo_de_cuenta_existente_no_se_liga_solo()
    {
        await CreateMerchantAsync(ProviderEmail);
        var client = CreateClient();

        var callback = await client.GetAsync(await StartExternalLoginAsync(client));
        Assert.Equal(LoginUrl, Location(callback));
        Assert.Contains("Ya tienes una cuenta con ese correo", await HtmlAsync(client, LoginUrl));

        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Empty(await users.GetLoginsAsync((await users.FindByEmailAsync(ProviderEmail))!));
    }

    [Fact]
    public async Task Escribir_un_correo_existente_en_el_alta_muestra_error()
    {
        const string existing = "existente@prueba.mx";
        await CreateMerchantAsync(existing);
        Factory.ExternalUser.Email = null; // El proveedor no mandó correo.
        var client = CreateClient();

        var callback = await StartExternalLoginAsync(client);
        var confirm = await ConfirmExternalAsync(client, callback, existing);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Contains("Ya tienes una cuenta con ese correo", WebUtility.HtmlDecode(await confirm.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Cuenta_ya_ligada_entra_directo_segun_su_rol()
    {
        const string adminEmail = "admin@prueba.mx";
        await CreateConfirmedUserAsync(adminEmail, Roles.Admin);
        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = (await users.FindByEmailAsync(adminEmail))!;
            await users.AddLoginAsync(admin, new UserLoginInfo(FakeExternalAuthHandler.SchemeName, "admin-key", "Proveedor"));
        }
        Factory.ExternalUser.ProviderKey = "admin-key";
        Factory.ExternalUser.Email = adminEmail;
        var client = CreateClient();

        var login = await client.GetAsync(await StartExternalLoginAsync(client));
        Assert.Equal("/admin", Location(login));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task Cancelar_en_el_proveedor_regresa_al_login_con_mensaje()
    {
        var client = CreateClient();
        var callback = await client.GetAsync("/Identity/Account/ExternalLogin?handler=Callback&remoteError=cancelled");
        Assert.Equal(LoginUrl, Location(callback));
        Assert.Contains("Cancelaste el inicio de sesión", await HtmlAsync(client, LoginUrl));
    }

    [Fact]
    public async Task Manage_permite_ligar_y_quitar_la_cuenta_externa()
    {
        await CreateMerchantAsync(ProviderEmail);
        var client = CreateClient();
        await LoginAsync(client, ProviderEmail);
        Assert.Contains("Ligar con Proveedor", await HtmlAsync(client, ManageUrl));

        var link = await PostFormAsync(client, ManageUrl, new Dictionary<string, string> { ["provider"] = FakeExternalAuthHandler.SchemeName },
            ManageUrl + "?handler=LinkLogin");
        var linkCallback = await client.GetAsync(Location(link));
        Assert.Equal(ManageUrl, Location(linkCallback));
        var linked = await HtmlAsync(client, ManageUrl);
        Assert.Contains("Se ligó la cuenta externa", linked);
        Assert.Contains("Ligadas a tu cuenta", linked);

        // Ya ligada, el proveedor entra directo desde otro navegador.
        var other = CreateClient();
        Assert.Equal("/app", Location(await other.GetAsync(await StartExternalLoginAsync(other))));

        var remove = await PostFormAsync(client, ManageUrl, new Dictionary<string, string>
        {
            ["LoginProvider"] = FakeExternalAuthHandler.SchemeName,
            ["ProviderKey"] = Factory.ExternalUser.ProviderKey,
        }, ManageUrl + "?handler=RemoveLogin");
        Assert.Equal(ManageUrl, Location(remove));
        Assert.Contains("Se quitó la cuenta externa", await HtmlAsync(client, ManageUrl));
    }

    [Fact]
    public async Task No_se_puede_quitar_la_unica_forma_de_entrar()
    {
        var client = CreateClient();
        var callback = await StartExternalLoginAsync(client);
        Assert.Equal("/app", Location(await ConfirmExternalAsync(client, callback, ProviderEmail)));

        var page = await HtmlAsync(client, ManageUrl);
        Assert.DoesNotContain("remove-login-", page);

        var remove = await PostFormAsync(client, ManageUrl, new Dictionary<string, string>
        {
            ["LoginProvider"] = FakeExternalAuthHandler.SchemeName,
            ["ProviderKey"] = Factory.ExternalUser.ProviderKey,
        }, ManageUrl + "?handler=RemoveLogin");
        Assert.Equal(ManageUrl, Location(remove));
        Assert.Contains("no puedes quitar tu única forma de entrar", await HtmlAsync(client, ManageUrl));
    }

    /// <summary>Pulsa el botón del proveedor en Login; devuelve la URL del callback a la que manda el proveedor.</summary>
    private static async Task<string> StartExternalLoginAsync(HttpClient client)
    {
        var challenge = await PostFormAsync(client, LoginUrl,
            new Dictionary<string, string> { ["provider"] = FakeExternalAuthHandler.SchemeName },
            "/Identity/Account/ExternalLogin");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var callback = Location(challenge);
        Assert.NotNull(callback);
        Assert.StartsWith("/Identity/Account/ExternalLogin?handler=Callback", callback);
        return callback;
    }

    private static Task<HttpResponseMessage> ConfirmExternalAsync(HttpClient client, string callbackUrl, string email) =>
        PostFormAsync(client, callbackUrl, new Dictionary<string, string> { ["Input.Email"] = email },
            "/Identity/Account/ExternalLogin?handler=Confirmation");

    private static async Task<string> HtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
