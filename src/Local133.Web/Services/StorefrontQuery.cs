using Local133.Application.Merchants;
using Local133.Domain.Merchants;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Services;

/// <summary>
/// Arma el catálogo público (5.10): datos del negocio, celular para WhatsApp y productos vigentes por nombre, con
/// búsqueda y páginas de <see cref="StorefrontViewModel.PageSize"/>. Sin filtro multi-tenant (quien lo abre puede tener
/// sesión de otro vendedor): todo va por <c>MerchantId</c> explícito. Las URLs de las imágenes las decide quien llama
/// (rutas públicas en <c>/tienda</c>, privadas en la vista previa).
/// </summary>
public static class StorefrontQuery
{
    /// <summary>Cómo se arman las URLs de las imágenes (llave guardada ⇒ URL).</summary>
    public sealed record ImageUrls(
        Func<string, string> Logo, Func<string, string> ShopPhoto, Func<int, string, string> Thumbnail);

    public static ImageUrls PublicImages(string slug) => new(
        logo => StorefrontUrls.Logo(slug, MerchantImageRules.Version(logo)),
        photo => StorefrontUrls.ShopPhoto(slug, MerchantImageRules.Version(photo)),
        (id, key) => StorefrontUrls.Thumbnail(slug, id, ProductUrls.Version(key)));

    /// <summary>Vista previa del vendedor: las rutas privadas de «Mi información» y de productos.</summary>
    public static ImageUrls PrivateImages { get; } = new(ProfileUrls.Logo, ProfileUrls.ShopPhoto, ProductUrls.Thumbnail);

    public static async Task<StorefrontViewModel> BuildAsync(
        AppDbContext db, Merchant merchant, string? q, int? page, ImageUrls images, string listPath, string madeWithUrl,
        bool isPreview, CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (query is { Length: > StorefrontViewModel.QueryMaxLength })
        {
            query = query[..StorefrontViewModel.QueryMaxLength];
        }

        var products = db.Products.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.MerchantId == merchant.Id && !p.IsDeleted);
        if (query is not null)
        {
            products = products.Where(p => p.Name.Contains(query));
        }

        var pageInfo = PageInfo.For(page, StorefrontViewModel.PageSize, await products.CountAsync(cancellationToken));
        var rows = await products
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(pageInfo.Skip).Take(pageInfo.PageSize)
            .Select(p => new { p.Id, p.Name, p.Description, p.Price, p.PhotoKey })
            .ToListAsync(cancellationToken);

        var phone = await db.Users.AsNoTracking()
            .Where(u => u.Id == merchant.UserId)
            .Select(u => u.PhoneNumber)
            .SingleOrDefaultAsync(cancellationToken);
        var businessName = merchant.BusinessName?.Trim() is { Length: > 0 } name ? name : "Catálogo";

        return new StorefrontViewModel(
            businessName,
            merchant.Address,
            merchant.LogoKey is { } logo ? images.Logo(logo) : null,
            merchant.ShopPhotoKey is { } photo ? images.ShopPhoto(photo) : null,
            Domain.Sales.PhoneNumber.IsValid(phone)
                ? ReceiptLinks.WhatsAppUrl(StorefrontViewModel.WhatsAppMessage(businessName), phone)
                : null,
            rows.Select(p => new StorefrontProduct(
                p.Name,
                p.Description,
                SaleQrViewModel.FormatAmount(p.Price),
                p.PhotoKey is null ? null : images.Thumbnail(p.Id, p.PhotoKey))).ToList(),
            pageInfo,
            query,
            listPath,
            madeWithUrl,
            isPreview);
    }
}
