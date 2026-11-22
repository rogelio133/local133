using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Services;

/// <summary>
/// Link de pago pendiente del paquete del último pago (5.7): el que crea el job diario como renovación o el que el
/// vendedor ya había generado en «Mi plan» (el job lo reutiliza si coincide el monto). Lo usan el banner de gracia y
/// «Mi plan».
/// </summary>
public static class RenewalLinkQuery
{
    public static async Task<SubscriptionCharge?> FindAsync(AppDbContext db, int merchantId, CancellationToken cancellationToken)
    {
        var package = await db.Subscriptions.AsNoTracking()
            .Where(s => s.MerchantId == merchantId)
            .Select(s => s.LastPackage)
            .SingleOrDefaultAsync(cancellationToken);
        if (package is null)
        {
            return null;
        }

        return await db.SubscriptionCharges.AsNoTracking()
            .Where(c => c.MerchantId == merchantId
                && c.Status == SubscriptionChargeStatus.Pending
                && c.Package == package
                && c.PeyPaymentLinkId != null
                && c.Url != null)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
