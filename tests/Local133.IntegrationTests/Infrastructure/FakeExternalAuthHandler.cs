using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>Usuario que "devuelve" el proveedor falso en el siguiente challenge.</summary>
public sealed class FakeExternalUser
{
    public string ProviderKey { get; set; } = "fake-key-1";

    public string? Email { get; set; } = "externo@prueba.mx";
}

/// <summary>
/// Proveedor externo falso (en lugar de Google/Facebook): el challenge escribe directo la cookie externa
/// de Identity, como lo haría el callback <c>/signin-*</c>, y redirige al <c>RedirectUri</c> de la página.
/// </summary>
public sealed class FakeExternalAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    FakeExternalUser user) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Fake";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ProviderKey), new(ClaimTypes.Name, "Usuario Externo") };
        if (user.Email is not null)
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

        // properties trae LoginProvider (y XsrfId al ligar) puestos por ConfigureExternalAuthenticationProperties.
        await Context.SignInAsync(IdentityConstants.ExternalScheme, principal, properties);
        Context.Response.Redirect(properties.RedirectUri ?? "/");
    }
}
