using Local133.Application.Merchants;
using Local133.Application.Subscriptions;
using Local133.Domain.Merchants;
using Local133.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Local133.Infrastructure.Merchants;

/// <summary>
/// Catálogo público (5.10). El slug se revisa contra la base antes de guardar; el índice único filtrado de
/// <c>Merchants.PublicSlug</c> es la última defensa si dos vendedores eligen el mismo a la vez.
/// </summary>
internal sealed class PublicCatalogSettings(AppDbContext db, IPlanAccess plans) : IPublicCatalogSettings
{
    public async Task<PublishCatalogResult> PublishAsync(int merchantId, string? slug, CancellationToken cancellationToken = default)
    {
        var merchant = await db.Merchants.SingleOrDefaultAsync(m => m.Id == merchantId, cancellationToken);
        if (merchant is null || !merchant.CanEditProfile)
        {
            return new PublishCatalogResult.NotAllowed();
        }

        var plan = await plans.GetForMerchantAsync(merchantId, cancellationToken);
        if (!plan.Entitlements.CanPublishCatalog)
        {
            return new PublishCatalogResult.PlanRequired();
        }

        if (string.IsNullOrWhiteSpace(merchant.BusinessName))
        {
            return new PublishCatalogResult.BusinessNameRequired();
        }

        var normalized = PublicSlug.Normalize(slug);
        if (!PublicSlug.IsValid(normalized))
        {
            return new PublishCatalogResult.InvalidSlug();
        }

        if (await db.Merchants.AnyAsync(m => m.PublicSlug == normalized && m.Id != merchantId, cancellationToken))
        {
            return new PublishCatalogResult.SlugTaken(normalized);
        }

        merchant.PublishCatalog(normalized);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return new PublishCatalogResult.SlugTaken(normalized);
        }

        return new PublishCatalogResult.Published(normalized);
    }

    public async Task<UnpublishCatalogResult> UnpublishAsync(int merchantId, CancellationToken cancellationToken = default)
    {
        var merchant = await db.Merchants.SingleOrDefaultAsync(m => m.Id == merchantId, cancellationToken);
        if (merchant is null || !merchant.CanEditProfile)
        {
            return new UnpublishCatalogResult.NotAllowed();
        }

        if (merchant.PublicCatalogEnabled)
        {
            merchant.UnpublishCatalog();
            await db.SaveChangesAsync(cancellationToken);
        }

        return new UnpublishCatalogResult.Unpublished();
    }
}
