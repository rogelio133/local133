using Local133.Application.Subscriptions;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Services;

/// <summary>
/// Lista paginada de productos vigentes del vendedor, ordenada por nombre y con búsqueda por nombre. La usan la lista
/// de productos (/app/productos) y el catálogo del carrito (/app/cobrar/productos).
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

    /// <param name="listPath">Página que muestra la lista (para la paginación y «Ver todos»).</param>
    /// <param name="readOnlyReason">Por qué no puede editar (<see cref="ReadOnlyReasonFor"/>); <c>null</c> = puede.</param>
    public static async Task<ProductListViewModel> BuildAsync(
        AppDbContext db, Domain.Merchants.Merchant merchant, string? q, int? page, IUrlHelper url, string listPath,
        CatalogReadOnlyReason? readOnlyReason, CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (query is { Length: > ProductListViewModel.QueryMaxLength })
        {
            query = query[..ProductListViewModel.QueryMaxLength];
        }

        // El filtro multi-tenant ya limita al vendedor de la sesión; el MerchantId explícito es la consulta en sí.
        var products = db.Products.AsNoTracking().Where(p => p.MerchantId == merchant.Id && !p.IsDeleted);
        if (query is not null)
        {
            products = products.Where(p => p.Name.Contains(query));
        }

        var pageInfo = PageInfo.For(page, ProductListViewModel.PageSize, await products.CountAsync(cancellationToken));
        var rows = await products
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(pageInfo.Skip).Take(pageInfo.PageSize)
            .Select(p => new { p.Id, p.Name, p.Price, p.PhotoKey })
            .ToListAsync(cancellationToken);

        return new ProductListViewModel
        {
            Items = rows.Select(p => new ProductListItem(
                p.Id,
                p.Name,
                p.Price,
                SaleQrViewModel.FormatAmount(p.Price),
                ThumbnailUrl(url, p.Id, p.PhotoKey))).ToList(),
            Page = pageInfo,
            Query = query,
            CanManage = readOnlyReason is null,
            ReadOnlyReason = readOnlyReason,
            ListPath = listPath,
        };
    }

    public static string? ThumbnailUrl(IUrlHelper url, int productId, string? photoKey) =>
        photoKey is null ? null : url.Content($"~{ProductUrls.Thumbnail(productId, photoKey)}");
}
