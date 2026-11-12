using System.Security.Claims;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant;

public static class CurrentMerchant
{
    /// <summary>
    /// <c>Merchant</c> del usuario de la sesión (sin seguimiento de cambios). Todo usuario Merchant tiene el suyo
    /// (<c>AccountRegistration</c> y el backfill de la migración <c>Merchants</c>).
    /// </summary>
    public static async Task<Domain.Merchants.Merchant> GetCurrentMerchantAsync(
        this AppDbContext db, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return await db.Merchants.AsNoTracking().SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException($"El usuario {userId} tiene rol Merchant pero no tiene Merchant.");
    }
}
