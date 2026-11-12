using System.Globalization;
using Local133.Application.Auth;
using Local133.Application.Tenancy;

namespace Local133.Web.Tenancy;

/// <summary>
/// Tenant de la petición web: un usuario con rol <c>Merchant</c> solo ve lo de su vendedor (claim
/// <see cref="AppClaimTypes.MerchantId"/>). Si le falta el claim no ve nada (falla cerrada). Admin, anónimo
/// (webhook) o fuera de una petición (jobs): sin alcance.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    /// <summary>Id que no existe: un vendedor sin claim no ve datos de nadie.</summary>
    public const int NoMerchant = -1;

    public bool IsMerchantScoped => accessor.HttpContext?.User is { } user && user.IsInRole(Roles.Merchant);

    public int? MerchantId
    {
        get
        {
            if (!IsMerchantScoped)
            {
                return null;
            }

            var value = accessor.HttpContext!.User.FindFirst(AppClaimTypes.MerchantId)?.Value;
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : NoMerchant;
        }
    }
}
