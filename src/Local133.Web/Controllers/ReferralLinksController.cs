using Local133.Domain.Merchants;
using Local133.Infrastructure.Persistence;
using Local133.Web.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Controllers;

/// <summary>
/// Liga de invitación <c>/r/{código}</c> (5.8): si el código existe, lo guarda en la cookie de 30 días
/// (<see cref="ReferralCookie"/>) y lleva al registro con el código ya escrito. Si no existe, lleva al registro sin código.
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class ReferralLinksController(AppDbContext db) : Controller
{
    public const string RegisterPath = "/Identity/Account/Register";

    [HttpGet("r/{code}")]
    public async Task<IActionResult> Follow(string code, CancellationToken cancellationToken)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers.CacheControl = "private, no-store";

        var normalized = ReferralCode.Normalize(code);
        var exists = ReferralCode.IsValid(normalized)
            && await db.Merchants.AsNoTracking().AnyAsync(m => m.ReferralCode == normalized, cancellationToken);
        if (!exists)
        {
            return Redirect(Url.Content("~" + RegisterPath));
        }

        ReferralCookie.Write(Response, normalized);
        return Redirect(Url.Content("~" + RegisterPath) + "?codigo=" + normalized);
    }
}
