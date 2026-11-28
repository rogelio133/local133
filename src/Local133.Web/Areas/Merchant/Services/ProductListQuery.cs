using System.Globalization;
using Local133.Application.Subscriptions;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Services;

/// <summary>
/// Lista paginada de productos vigentes del vendedor, ordenada por nombre, con búsqueda por nombre y filtro por
/// categoría. La usan la lista de productos (/app/productos) y el catálogo del carrito (/app/cobrar/productos).
/// </summary>
public static class ProductListQuery
{
    /// <summary>
    /// Por qué el catálogo es de solo lectura: dado de baja, o un plan sin <c>CanEditCatalog</c> (Básico o vencido,
    /// 5.14); <c>null</c> = puede editar.
    /// </summary>
    public static CatalogReadOnlyReason? ReadOnlyReasonFor(Domain.Merchants.Merchant merchant, MerchantPlan plan) =>
        !merchant.CanManageProducts ? CatalogReadOnlyReason.Disabled
        : plan.Entitlements.CanEditCatalog ? null
        : plan.Tier == PlanTier.Expired ? CatalogReadOnlyReason.Expired
        : CatalogReadOnlyReason.Basic;

    /// <param name="category">
    /// Filtro <c>?categoria=</c>: el id de una categoría del vendedor o <see cref="CategoryFilters.Uncategorized"/>; otro
    /// valor (o una categoría ajena) = todas.
    /// </param>
    /// <param name="listPath">Página que muestra la lista (para la paginación y «Ver todos»).</param>
    /// <param name="readOnlyReason">Por qué no puede editar (<see cref="ReadOnlyReasonFor"/>); <c>null</c> = puede.</param>
    public static async Task<ProductListViewModel> BuildAsync(
        AppDbContext db, Domain.Merchants.Merchant merchant, string? q, string? category, int? page, IUrlHelper url,
        string listPath, CatalogReadOnlyReason? readOnlyReason, CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (query is { Length: > ProductListViewModel.QueryMaxLength })
        {
            query = query[..ProductListViewModel.QueryMaxLength];
        }

        // El filtro multi-tenant ya limita al vendedor de la sesión; el MerchantId explícito es la consulta en sí.
        var all = db.Products.AsNoTracking().Where(p => p.MerchantId == merchant.Id && !p.IsDeleted);
        var categories = await CategoriesAsync(db, merchant.Id, cancellationToken);
        var hasUncategorized = categories.Count > 0 && await all.AnyAsync(p => p.CategoryId == null, cancellationToken);

        var products = all;
        string? filter = null;
        if (category == CategoryFilters.Uncategorized)
        {
            filter = CategoryFilters.Uncategorized;
            products = products.Where(p => p.CategoryId == null);
        }
        else if (int.TryParse(category, NumberStyles.None, CultureInfo.InvariantCulture, out var categoryId)
                 && categories.Any(c => c.Id == categoryId))
        {
            filter = categoryId.ToString(CultureInfo.InvariantCulture);
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (query is not null)
        {
            products = products.Where(p => p.Name.Contains(query));
        }

        var pageInfo = PageInfo.For(page, ProductListViewModel.PageSize, await products.CountAsync(cancellationToken));
        var rows = await products
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(pageInfo.Skip).Take(pageInfo.PageSize)
            .Select(p => new { p.Id, p.Name, p.Price, p.PhotoKey, p.CategoryId })
            .ToListAsync(cancellationToken);
        var names = categories.ToDictionary(c => c.Id, c => c.Name);

        return new ProductListViewModel
        {
            Items = rows.Select(p => new ProductListItem(
                p.Id,
                p.Name,
                p.Price,
                SaleQrViewModel.FormatAmount(p.Price),
                ThumbnailUrl(url, p.Id, p.PhotoKey),
                // Con un filtro, la categoría ya se ve en el chip activo.
                filter is null && p.CategoryId is { } id ? names.GetValueOrDefault(id) : null)).ToList(),
            Page = pageInfo,
            Query = query,
            Categories = categories,
            HasUncategorized = hasUncategorized,
            CategoryFilter = filter,
            CanManage = readOnlyReason is null,
            ReadOnlyReason = readOnlyReason,
            ListPath = listPath,
        };
    }

    /// <summary>Categorías del vendedor en su orden (<c>SortOrder</c>, luego nombre).</summary>
    public static async Task<List<CategoryOption>> CategoriesAsync(AppDbContext db, int merchantId, CancellationToken cancellationToken) =>
        await db.ProductCategories.AsNoTracking()
            .Where(c => c.MerchantId == merchantId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new CategoryOption(c.Id, c.Name))
            .ToListAsync(cancellationToken);

    public static string? ThumbnailUrl(IUrlHelper url, int productId, string? photoKey) =>
        photoKey is null ? null : url.Content($"~{ProductUrls.Thumbnail(productId, photoKey)}");
}
