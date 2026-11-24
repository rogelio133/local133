using System.Net;
using Local133.Domain.Merchants;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;
using Local133.Web.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Identity;

/// <summary>
/// 5.8: código de invitación en el alta (Register y ExternalLogin), la liga <c>/r/{código}</c> y su cookie. Cada
/// vendedor nace con su propio código; un código que no existe da error en el campo y no crea nada.
/// </summary>
public class ReferralRegistrationTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string ReferrerEmail = "invita@prueba.mx";
    private const string RegisterUrl = "/Identity/Account/Register";

    [Fact]
    public async Task Cada_vendedor_nace_con_un_codigo_unico()
    {
        Assert.Equal(HttpStatusCode.Redirect, (await RegisterAsync(CreateClient(), Email)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await RegisterAsync(CreateClient(), OtherEmail)).StatusCode);

        var codes = await WithDbAsync(db => db.Merchants.Select(m => m.ReferralCode).ToListAsync());

        Assert.Equal(2, codes.Count);
        Assert.All(codes, c => Assert.True(ReferralCode.IsValid(c), c));
        Assert.NotEqual(codes[0], codes[1]);
        Assert.False(await WithDbAsync(db => db.Referrals.AnyAsync()));
    }

    [Fact]
    public async Task Registro_con_codigo_crea_el_referido()
    {
        var code = await ReferrerCodeAsync();

        var response = await RegisterWithCodeAsync(CreateClient(), Email, $"  {code.ToLowerInvariant()} ");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var referral = await WithDbAsync(db => db.Referrals.SingleAsync());
        Assert.Equal(await MerchantIdAsync(ReferrerEmail), referral.ReferrerMerchantId);
        Assert.Equal(await MerchantIdAsync(Email), referral.ReferredMerchantId);
        Assert.Equal(code, referral.Code);
        Assert.Null(referral.QualifiedAt);
    }

    [Fact]
    public async Task Codigo_que_no_existe_da_error_en_el_campo_y_no_crea_la_cuenta()
    {
        await ReferrerCodeAsync();

        var response = await RegisterWithCodeAsync(CreateClient(), Email, "ZZZ999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Matches($"data-valmsg-for=\"Input.ReferralCode\"[^>]*>{ReferralCodeMessages.Unknown}<", html);
        await AssertNoAccountAsync(Email);
    }

    [Fact]
    public async Task Codigo_con_mal_formato_no_crea_la_cuenta()
    {
        var response = await RegisterWithCodeAsync(CreateClient(), Email, "AB1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ReferralCodeMessages.Format, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await AssertNoAccountAsync(Email);
    }

    [Fact]
    public async Task El_alta_valida_el_codigo_tambien_fuera_del_formulario()
    {
        await ReferrerCodeAsync();
        using var scope = Factory.Services.CreateScope();
        var registration = scope.ServiceProvider.GetRequiredService<AccountRegistration>();
        var user = new Local133.Infrastructure.Identity.ApplicationUser { UserName = Email, Email = Email };

        var result = await registration.CreateMerchantAsync(user, OwnerName, Password, referralCode: "ZZZ999");

        Assert.False(result.Succeeded);
        Assert.Equal(ReferralCodeMessages.UnknownCodeError, Assert.Single(result.Errors).Code);
        await AssertNoAccountAsync(Email);
    }

    [Fact]
    public async Task La_liga_guarda_el_codigo_en_la_cookie_y_lo_escribe_en_el_registro()
    {
        var code = await ReferrerCodeAsync();
        var client = CreateClient();

        var follow = await client.GetAsync($"/r/{code.ToLowerInvariant()}");

        Assert.Equal(HttpStatusCode.Redirect, follow.StatusCode);
        Assert.Equal($"{RegisterUrl}?codigo={code}", Location(follow));
        var cookie = Assert.Single(follow.Headers.GetValues("Set-Cookie"), c => c.StartsWith(ReferralCookie.Name + "=", StringComparison.Ordinal));
        Assert.Contains($"{ReferralCookie.Name}={code}", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=2592000", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("noindex", follow.Headers.GetValues("X-Robots-Tag").Single());

        // Con la URL y, después, solo con la cookie (vuelve otro día).
        Assert.Contains($"value=\"{code}\"", await GetHtmlAsync(client, $"{RegisterUrl}?codigo={code}"));
        Assert.Contains($"value=\"{code}\"", await GetHtmlAsync(client, RegisterUrl));

        var register = await RegisterWithCodeAsync(client, Email, code);
        Assert.Equal(HttpStatusCode.Redirect, register.StatusCode);
        Assert.Contains(register.Headers.GetValues("Set-Cookie"), c => c.StartsWith(ReferralCookie.Name + "=;", StringComparison.Ordinal));
        Assert.True(await WithDbAsync(db => db.Referrals.AnyAsync()));
    }

    [Fact]
    public async Task La_liga_con_un_codigo_que_no_existe_lleva_al_registro_sin_cookie()
    {
        await ReferrerCodeAsync();
        var client = CreateClient();

        var follow = await client.GetAsync("/r/ZZZ999");

        Assert.Equal(HttpStatusCode.Redirect, follow.StatusCode);
        Assert.Equal(RegisterUrl, Location(follow));
        Assert.False(follow.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain("ZZZ999", await GetHtmlAsync(client, RegisterUrl));
    }

    [Fact]
    public async Task Registro_con_Google_o_Facebook_toma_el_codigo_de_la_cookie()
    {
        var code = await ReferrerCodeAsync();
        var client = CreateClient();
        await client.GetAsync($"/r/{code}");

        var callback = await ExternalLoginTests.StartExternalLoginAsync(client);
        Assert.Contains($"value=\"{code}\"", await GetHtmlAsync(client, callback));

        var confirm = await PostFormAsync(client, callback, new Dictionary<string, string>
        {
            ["Input.OwnerName"] = OwnerName,
            ["Input.Email"] = "externo@prueba.mx",
            ["Input.ReferralCode"] = code,
            ["Input.AcceptLegal"] = "true",
        }, "/Identity/Account/ExternalLogin?handler=Confirmation");

        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
        var referral = await WithDbAsync(db => db.Referrals.SingleAsync());
        Assert.Equal(await MerchantIdAsync("externo@prueba.mx"), referral.ReferredMerchantId);
    }

    [Fact]
    public async Task Registro_con_Google_o_Facebook_rechaza_un_codigo_que_no_existe()
    {
        var client = CreateClient();
        var callback = await ExternalLoginTests.StartExternalLoginAsync(client);

        var confirm = await PostFormAsync(client, callback, new Dictionary<string, string>
        {
            ["Input.OwnerName"] = OwnerName,
            ["Input.Email"] = "externo@prueba.mx",
            ["Input.ReferralCode"] = "ZZZ999",
            ["Input.AcceptLegal"] = "true",
        }, "/Identity/Account/ExternalLogin?handler=Confirmation");

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Contains(ReferralCodeMessages.Unknown, WebUtility.HtmlDecode(await confirm.Content.ReadAsStringAsync()));
        await AssertNoAccountAsync("externo@prueba.mx");
    }

    private async Task<string> ReferrerCodeAsync()
    {
        await CreateMerchantAsync(ReferrerEmail);
        return await WithDbAsync(async db => (await MerchantOfAsync(db, ReferrerEmail)).ReferralCode);
    }

    private static Task<HttpResponseMessage> RegisterWithCodeAsync(HttpClient client, string email, string code) =>
        PostFormAsync(client, RegisterUrl, new Dictionary<string, string>
        {
            ["Input.OwnerName"] = OwnerName,
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
            ["Input.ReferralCode"] = code,
            ["Input.AcceptLegal"] = "true",
        });

    private Task AssertNoAccountAsync(string email) => WithDbAsync(async db =>
    {
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        Assert.False(await db.Referrals.AnyAsync());
    });
}
