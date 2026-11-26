using System.Security.Claims;
using Local133.Application.Auth;
using Local133.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Local133.Web.Identity;

/// <summary>A dónde mandar al usuario tras iniciar sesión: el inicio de su rol, o el <c>returnUrl</c> si es de su área.</summary>
public static class PostLoginRedirect
{
    private const string MerchantArea = "/app";
    private const string AdminArea = "/admin";
    private const string AccountArea = "/Identity/Account/Manage";

    /// <summary>Inicio de cada rol: el admin a Clientes, el vendedor a <c>/app</c>; sin rol, la home.</summary>
    public static async Task<string> HomeUrlAsync(UserManager<ApplicationUser> userManager, ApplicationUser? user)
    {
        if (user is null)
        {
            return "~/";
        }
        if (await userManager.IsInRoleAsync(user, Roles.Admin))
        {
            return "~/admin/clientes";
        }
        return await userManager.IsInRoleAsync(user, Roles.Merchant) ? "~/app" : "~/";
    }

    /// <summary>
    /// <see cref="HomeUrlAsync"/> con los roles de la cookie (sin ir a la base): para la home y el logo del layout
    /// público.
    /// </summary>
    public static string HomeUrlFor(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) ? "~/admin/clientes"
        : user.IsInRole(Roles.Merchant) ? "~/app"
        : "~/";

    /// <summary>
    /// Respeta <paramref name="returnUrl"/> solo si es local (<paramref name="isLocalUrl"/>, normalmente
    /// <c>Url.IsLocalUrl</c>) y pertenece al área del rol: <c>/app</c> para el vendedor, <c>/admin</c> para el admin y
    /// <c>/Identity/Account/Manage</c> para los dos. Cualquier otro caso, incluido <c>/</c>, va a <see cref="HomeUrlAsync"/>.
    /// Así un vendedor que llegó al login desde <c>/admin/...</c> no termina en «Acceso denegado», ni al revés.
    /// </summary>
    public static async Task<string> ResolveAsync(
        UserManager<ApplicationUser> userManager, ApplicationUser? user, string? returnUrl, Func<string, bool> isLocalUrl)
    {
        if (user is not null && !string.IsNullOrEmpty(returnUrl) && isLocalUrl(returnUrl))
        {
            var isAdmin = await userManager.IsInRoleAsync(user, Roles.Admin);
            var isMerchant = !isAdmin && await userManager.IsInRoleAsync(user, Roles.Merchant);
            if (IsAllowed(returnUrl, isAdmin, isMerchant))
            {
                return returnUrl;
            }
        }
        return await HomeUrlAsync(userManager, user);
    }

    /// <summary>¿<paramref name="returnUrl"/> (ya local) es del área del rol? Sin rol, ninguna.</summary>
    public static bool IsAllowed(string returnUrl, bool isAdmin, bool isMerchant)
    {
        if (!isAdmin && !isMerchant)
        {
            return false;
        }
        var path = PathOf(returnUrl);
        return IsUnder(path, AccountArea)
            || (isAdmin && IsUnder(path, AdminArea))
            || (isMerchant && IsUnder(path, MerchantArea));
    }

    // "~/app?x=1#y" ⇒ "/app": sin query ni fragmento, y la tilde de las rutas virtuales se quita.
    private static string PathOf(string url)
    {
        var end = url.IndexOfAny(['?', '#']);
        var path = end >= 0 ? url[..end] : url;
        return path.StartsWith("~/", StringComparison.Ordinal) ? path[1..] : path;
    }

    // "/app" y "/app/..." cuentan; "/application" no.
    private static bool IsUnder(string path, string area) =>
        path.Equals(area, StringComparison.OrdinalIgnoreCase)
        || path.Equals(area + "/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(area + "/", StringComparison.OrdinalIgnoreCase);
}
