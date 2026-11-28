using Local133.Application.Merchants;
using Local133.Application.Subscriptions;
using Local133.Domain.Merchants;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Models;
using Local133.Web.PublicPages;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Services;

/// <summary>
/// Arma el catálogo público (5.10): datos del negocio, celular para WhatsApp y productos vigentes, con búsqueda y páginas
/// de <see cref="StorefrontViewModel.PageSize"/>. Si el vendedor usa categorías (ajustes 2026-11-26), van agrupados:
/// por el orden de la categoría y luego por nombre, con los que no tienen categoría al final («Otros»); si no, por
/// nombre. Sin filtro multi-tenant (quien lo abre puede tener
/// sesión de otro vendedor): todo va por <c>MerchantId</c> explícito. Las URLs de las imágenes las decide quien llama
/// (rutas públicas en <c>/tienda</c>, privadas en la vista previa). El catálogo público lo dibuja la portada desde el
/// 2026-11-27, con los datos de <see cref="ToPublic"/> (<c>GET /api/public/tiendas/{slug}</c>).
/// </summary>
public static class StorefrontQuery
{
    /// <summary>Cómo se arman las URLs de las imágenes (llave guardada ⇒ URL).</summary>
    public sealed record ImageUrls(
        Func<string, string> Logo, Func<string, string> ShopPhoto, Func<int, string, string> Thumbnail);

    /// <summary>Rutas públicas de las imágenes en el portal; <paramref name="baseUrl"/> las hace absolutas (la portada las pide aquí).</summary>
    public static ImageUrls PublicImages(string slug, string baseUrl = "") => new(
        logo => baseUrl + StorefrontUrls.Logo(slug, MerchantImageRules.Version(logo)),
        photo => baseUrl + StorefrontUrls.ShopPhoto(slug, MerchantImageRules.Version(photo)),
        (id, key) => baseUrl + StorefrontUrls.Thumbnail(slug, id, ProductUrls.Version(key)));

    /// <summary>Vendedor dueño del slug (sin distinguir mayúsculas, sin filtro multi-tenant), o <c>null</c>.</summary>
    public static async Task<Merchant?> FindMerchantAsync(AppDbContext db, string slug, CancellationToken cancellationToken)
    {
        var normalized = slug.ToLowerInvariant();
        return PublicSlug.IsValid(normalized)
            ? await db.Merchants.AsNoTracking().SingleOrDefaultAsync(m => m.PublicSlug == normalized, cancellationToken)
            : null;
    }

    /// <summary>
    /// El catálogo se ve: publicado, con un plan que lo permite (Pro) y sin suspender ni dar de baja
    /// (<see cref="Merchant.CanShowPublicCatalog(Domain.Subscriptions.PlanEntitlements)"/>).
    /// </summary>
    public static async Task<bool> IsVisibleAsync(Merchant merchant, IPlanAccess plans, CancellationToken cancellationToken) =>
        merchant.PublicCatalogEnabled
        && merchant.CanShowPublicCatalog((await plans.GetForMerchantAsync(merchant.Id, cancellationToken)).Entitlements);

    /// <summary>Datos del catálogo para la portada (<see cref="PublicStorefront"/>).</summary>
    public static PublicStorefront ToPublic(StorefrontViewModel model, string referralCode) => new(
        true,
        model.BusinessName,
        model.Address,
        model.LogoUrl,
        model.CoverUrl,
        model.WhatsAppUrl,
        model.Products.Select(p => new PublicStorefrontProduct(p.Name, p.Description, p.PriceText, p.ThumbnailUrl, p.Category)).ToList(),
        model.Page.Page,
        model.Page.PageSize,
        model.Page.TotalCount,
        model.Query,
        referralCode,
        model.Grouped);

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

        var all = db.Products.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.MerchantId == merchant.Id && !p.IsDeleted);

        // Se agrupa si algún producto vigente tiene categoría (también con búsqueda, para que no cambie el aspecto).
        var grouped = await all.AnyAsync(p => p.CategoryId != null, cancellationToken);
        var products = all;
        if (query is not null)
        {
            products = products.Where(p => p.Name.Contains(query));
        }

        var pageInfo = PageInfo.For(page, StorefrontViewModel.PageSize, await products.CountAsync(cancellationToken));
        var ordered = grouped
            ? products
                .OrderBy(p => p.CategoryId == null)
                .ThenBy(p => p.Category!.SortOrder).ThenBy(p => p.Category!.Name).ThenBy(p => p.CategoryId)
                .ThenBy(p => p.Name).ThenBy(p => p.Id)
            : products.OrderBy(p => p.Name).ThenBy(p => p.Id);
        var rows = await ordered
            .Skip(pageInfo.Skip).Take(pageInfo.PageSize)
            .Select(p => new
            {
                p.Id, p.Name, p.Description, p.Price, p.PhotoKey,
                Category = p.Category == null ? null : p.Category.Name,
            })
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
                p.PhotoKey is null ? null : images.Thumbnail(p.Id, p.PhotoKey),
                grouped ? p.Category : null)).ToList(),
            pageInfo,
            query,
            listPath,
            madeWithUrl,
            isPreview,
            grouped);
    }
}
