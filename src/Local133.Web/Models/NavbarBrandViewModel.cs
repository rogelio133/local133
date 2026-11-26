using System.Security.Claims;
using Local133.Web.Identity;

namespace Local133.Web.Models;

/// <summary>Variante de la marca de la barra: el admin pinta «133» en rosa y la insignia «Admin».</summary>
public enum NavbarBrandVariant
{
    Public,
    Merchant,
    Admin,
}

/// <summary>Logo + «Local133» en la barra superior (parcial <c>_NavbarBrand</c>).</summary>
/// <param name="HideWordmarkOnPhone">
/// Sin sesión, la barra pública lleva «Entrar» y «Crear cuenta»: a 375 px no caben junto al texto, así que en
/// <c>xs</c> solo va el logo (que ya dice «Local 133») con su <c>alt</c>.
/// </param>
public sealed record NavbarBrandViewModel(string Href, NavbarBrandVariant Variant, bool HideWordmarkOnPhone = false)
{
    public static readonly NavbarBrandViewModel Merchant = new("~/app", NavbarBrandVariant.Merchant);

    public static readonly NavbarBrandViewModel Admin = new("~/admin/clientes", NavbarBrandVariant.Admin);

    /// <summary>
    /// Layout público (Identity, recibo, catálogo): el logo lleva al inicio de cada rol
    /// (<see cref="PostLoginRedirect.HomeUrlFor"/>); sin rol, a la home.
    /// </summary>
    public static NavbarBrandViewModel ForPublic(ClaimsPrincipal user) =>
        new(PostLoginRedirect.HomeUrlFor(user), NavbarBrandVariant.Public,
            HideWordmarkOnPhone: user.Identity?.IsAuthenticated != true);
}
