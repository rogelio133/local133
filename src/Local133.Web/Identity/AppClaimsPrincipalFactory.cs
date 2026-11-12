using System.Globalization;
using System.Security.Claims;
using Local133.Application.Auth;
using Local133.Infrastructure.Identity;
using Local133.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Local133.Web.Identity;

/// <summary>
/// Agrega a la sesión el id del <c>Merchant</c> del usuario (<see cref="AppClaimTypes.MerchantId"/>), que usa
/// <see cref="Tenancy.HttpTenantContext"/>. Corre al iniciar sesión y en cada revalidación del security stamp.
/// </summary>
public sealed class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options,
    AppDbContext db)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        var merchantId = await db.Merchants.Where(m => m.UserId == user.Id).Select(m => (int?)m.Id).SingleOrDefaultAsync();
        if (merchantId is { } id)
        {
            identity.AddClaim(new Claim(AppClaimTypes.MerchantId, id.ToString(CultureInfo.InvariantCulture)));
        }
        return identity;
    }
}
