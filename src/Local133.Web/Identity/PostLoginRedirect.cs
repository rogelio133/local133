using Local133.Application.Auth;
using Local133.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Local133.Web.Identity;

/// <summary>A dónde mandar al usuario tras iniciar sesión cuando no hay <c>returnUrl</c>.</summary>
public static class PostLoginRedirect
{
    public static async Task<string> HomeUrlAsync(UserManager<ApplicationUser> userManager, ApplicationUser? user)
    {
        if (user is null)
        {
            return "~/";
        }
        if (await userManager.IsInRoleAsync(user, Roles.Admin))
        {
            return "~/admin";
        }
        return await userManager.IsInRoleAsync(user, Roles.Merchant) ? "~/app" : "~/";
    }
}
