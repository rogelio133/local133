using Local133.Application.Auth;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Identity;

namespace Local133.IntegrationTests.Identity;

/// <summary>
/// <c>returnUrl</c> del login: solo se respeta si es del área del rol (ajustes 2026-11-26, punto 3). Sin SQL Server:
/// corren en CI.
/// </summary>
public class PostLoginRedirectRulesTests
{
    [Theory]
    [InlineData("/app", true)]
    [InlineData("/app/", true)]
    [InlineData("/app/ventas", true)]
    [InlineData("/APP/Ventas?estado=pagadas", true)]
    [InlineData("~/app/cobrar", true)]
    [InlineData("/Identity/Account/Manage/ChangePassword", true)]
    [InlineData("/identity/account/manage", true)]
    [InlineData("/application", false)]
    [InlineData("/apple-touch-icon.png", false)]
    [InlineData("/admin/tablero", false)]
    [InlineData("/", false)]
    [InlineData("/recibo/abc", false)]
    [InlineData("/Identity/Account/Login", false)]
    public void Vendedor(string returnUrl, bool allowed) =>
        Assert.Equal(allowed, PostLoginRedirect.IsAllowed(returnUrl, isAdmin: false, isMerchant: true));

    [Theory]
    [InlineData("/admin", true)]
    [InlineData("/admin/clientes/12/plan", true)]
    [InlineData("/Admin/Tablero#x", true)]
    [InlineData("/Identity/Account/Manage/ExternalLogins", true)]
    [InlineData("/administracion", false)]
    [InlineData("/app/cobrar", false)]
    [InlineData("/", false)]
    public void Admin(string returnUrl, bool allowed) =>
        Assert.Equal(allowed, PostLoginRedirect.IsAllowed(returnUrl, isAdmin: true, isMerchant: false));

    [Fact]
    public void Sin_rol_no_respeta_ninguno() =>
        Assert.False(PostLoginRedirect.IsAllowed("/Identity/Account/Manage", isAdmin: false, isMerchant: false));
}

/// <summary>Login y ExternalLogin con <c>returnUrl</c> de otra área: van al inicio del rol.</summary>
public class PostLoginRedirectTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private const string MerchantEmail = "vendedor@prueba.mx";
    private const string AdminEmail = "admin@prueba.mx";

    [Theory]
    [InlineData("/admin/tablero", "/app")]
    [InlineData("/app/ventas", "/app/ventas")]
    [InlineData("/application", "/app")]
    [InlineData("/", "/app")]
    [InlineData("https://ejemplo.com/app", "/app")]
    [InlineData("/Identity/Account/Manage/ChangePassword", "/Identity/Account/Manage/ChangePassword")]
    public async Task Vendedor_respeta_solo_su_area(string returnUrl, string expected)
    {
        await CreateMerchantAsync(MerchantEmail);

        var login = await LoginWithReturnUrlAsync(MerchantEmail, returnUrl);

        Assert.Equal(expected, Location(login));
    }

    [Theory]
    [InlineData("/app/cobrar", "/admin/clientes")]
    [InlineData("/admin/tablero", "/admin/tablero")]
    [InlineData("/", "/admin/clientes")]
    public async Task Admin_respeta_solo_su_area(string returnUrl, string expected)
    {
        await CreateConfirmedUserAsync(AdminEmail, Roles.Admin);

        var login = await LoginWithReturnUrlAsync(AdminEmail, returnUrl);

        Assert.Equal(expected, Location(login));
    }

    [Theory]
    [InlineData("/admin/tablero", "/app")]
    [InlineData("/app/ventas", "/app/ventas")]
    public async Task ExternalLogin_respeta_solo_el_area_del_vendedor(string returnUrl, string expected)
    {
        var client = CreateClient();
        var challenge = await PostFormAsync(client, "/Identity/Account/Login",
            new Dictionary<string, string> { ["provider"] = FakeExternalAuthHandler.SchemeName },
            "/Identity/Account/ExternalLogin?returnUrl=" + Uri.EscapeDataString(returnUrl));
        var callback = Location(challenge);
        Assert.NotNull(callback);

        // El formulario de confirmación lleva el returnUrl en su action (asp-route-returnUrl), igual que aquí.
        var confirm = await PostFormAsync(client, callback, new Dictionary<string, string>
        {
            ["Input.OwnerName"] = OwnerName,
            ["Input.Email"] = Factory.ExternalUser.Email!,
            ["Input.AcceptLegal"] = "true",
        }, "/Identity/Account/ExternalLogin?handler=Confirmation&returnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal(expected, Location(confirm));
    }

    private async Task<HttpResponseMessage> LoginWithReturnUrlAsync(string email, string returnUrl) =>
        await PostFormAsync(CreateClient(), "/Identity/Account/Login?returnUrl=" + Uri.EscapeDataString(returnUrl),
            new Dictionary<string, string> { ["Input.Email"] = email, ["Input.Password"] = Password });
}
