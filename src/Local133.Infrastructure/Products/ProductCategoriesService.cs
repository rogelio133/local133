using Local133.Application.Products;
using Local133.Application.Subscriptions;
using Local133.Domain.Merchants;
using Local133.Domain.Products;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Products;

/// <summary>
/// Categorías del catálogo: alta, renombrar, borrar (los productos quedan sin categoría) y orden. Todo va por
/// <c>MerchantId</c> explícito (no depende del claim de la sesión). Los permisos son los del catálogo.
/// </summary>
internal sealed class ProductCategoriesService(
    AppDbContext db,
    IPlanAccess plans,
    TimeProvider clock,
    ILogger<ProductCategoriesService> logger) : IProductCategories
{
    public async Task<ProductCategoryResult> CreateAsync(CreateProductCategoryCommand command, CancellationToken cancellationToken = default)
    {
        var (merchant, denied) = await AuthorizeAsync(command.UserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        if (ProductCategoryMessages.NameError(command.Name) is { } error)
        {
            return new ProductCategoryResult.Invalid(error);
        }

        var name = ProductCategory.Normalize(command.Name)!;
        if (await ProductCategoryStore.FindByNameAsync(db, merchant!.Id, name, cancellationToken) is { } existing)
        {
            return new ProductCategoryResult.Invalid(ProductCategoryMessages.Duplicate(existing.Name));
        }

        var count = await ProductCategoryStore.Of(db, merchant.Id).CountAsync(cancellationToken);
        if (count >= ProductCategory.MaxPerMerchant)
        {
            return new ProductCategoryResult.Invalid(ProductCategoryMessages.LimitReached);
        }

        var category = ProductCategory.Create(
            merchant.Id, name, await ProductCategoryStore.NextSortOrderAsync(db, merchant.Id, cancellationToken), clock.GetUtcNow());
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Categoría {CategoryId} del vendedor {MerchantId} creada", category.Id, merchant.Id);
        return new ProductCategoryResult.Saved(category.Id, category.Name);
    }

    public async Task<ProductCategoryResult> RenameAsync(RenameProductCategoryCommand command, CancellationToken cancellationToken = default)
    {
        var (merchant, denied) = await AuthorizeAsync(command.UserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var category = await FindAsync(merchant!.Id, command.CategoryId, cancellationToken);
        if (category is null)
        {
            return new ProductCategoryResult.NotFound();
        }

        if (ProductCategoryMessages.NameError(command.Name) is { } error)
        {
            return new ProductCategoryResult.Invalid(error);
        }

        var name = ProductCategory.Normalize(command.Name)!;
        if (await ProductCategoryStore.FindByNameAsync(db, merchant.Id, name, cancellationToken) is { } existing
            && existing.Id != category.Id)
        {
            return new ProductCategoryResult.Invalid(ProductCategoryMessages.Duplicate(existing.Name));
        }

        category.Rename(name);
        await db.SaveChangesAsync(cancellationToken);
        return new ProductCategoryResult.Saved(category.Id, category.Name);
    }

    public async Task<ProductCategoryResult> DeleteAsync(DeleteProductCategoryCommand command, CancellationToken cancellationToken = default)
    {
        var (merchant, denied) = await AuthorizeAsync(command.UserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var category = await FindAsync(merchant!.Id, command.CategoryId, cancellationToken);
        if (category is null)
        {
            return new ProductCategoryResult.NotFound();
        }

        var productCount = await db.Products.IgnoreQueryFilters()
            .CountAsync(p => p.MerchantId == merchant.Id && p.CategoryId == category.Id && !p.IsDeleted, cancellationToken);

        // La FK es ON DELETE SET NULL: los productos (también los dados de baja) quedan sin categoría.
        db.ProductCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Categoría {CategoryId} del vendedor {MerchantId} borrada ({ProductCount} productos sin categoría)",
            category.Id, merchant.Id, productCount);
        return new ProductCategoryResult.Deleted(category.Name, productCount);
    }

    public async Task<ProductCategoryResult> ReorderAsync(ReorderProductCategoriesCommand command, CancellationToken cancellationToken = default)
    {
        var (merchant, denied) = await AuthorizeAsync(command.UserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var categories = await ProductCategoryStore.Of(db, merchant!.Id).ToDictionaryAsync(c => c.Id, cancellationToken);

        // Exactamente las del vendedor, cada una una vez: ni faltantes, ni repetidas, ni ajenas.
        if (command.CategoryIds.Count != categories.Count
            || command.CategoryIds.Distinct().Count() != categories.Count
            || command.CategoryIds.Any(id => !categories.ContainsKey(id)))
        {
            return new ProductCategoryResult.Invalid(ProductCategoryMessages.OrderInvalid);
        }

        await SaveOrderAsync(command.CategoryIds.Select(id => categories[id]).ToList(), cancellationToken);
        return new ProductCategoryResult.Reordered();
    }

    public async Task<ProductCategoryResult> MoveAsync(MoveProductCategoryCommand command, CancellationToken cancellationToken = default)
    {
        var (merchant, denied) = await AuthorizeAsync(command.UserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var ordered = await ProductCategoryStore.Ordered(ProductCategoryStore.Of(db, merchant!.Id)).ToListAsync(cancellationToken);
        var index = ordered.FindIndex(c => c.Id == command.CategoryId);
        if (index < 0)
        {
            return new ProductCategoryResult.NotFound();
        }

        var target = Math.Clamp(index + Math.Sign(command.Offset), 0, ordered.Count - 1);
        if (target != index)
        {
            (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        }

        await SaveOrderAsync(ordered, cancellationToken);
        return new ProductCategoryResult.Reordered();
    }

    /// <summary>Reescribe <c>SortOrder</c> de 0 a N en el orden dado.</summary>
    private async Task SaveOrderAsync(IReadOnlyList<ProductCategory> ordered, CancellationToken cancellationToken)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Mismos permisos que los productos: el estatus (dado de baja) manda sobre el plan.</summary>
    private async Task<(Merchant? Merchant, ProductCategoryResult? Denied)> AuthorizeAsync(string userId, CancellationToken cancellationToken)
    {
        var merchant = await db.Merchants.AsNoTracking().SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException($"El usuario {userId} no tiene Merchant.");
        if (!merchant.CanManageProducts)
        {
            return (merchant, new ProductCategoryResult.NotAllowed());
        }

        return (await plans.GetForMerchantAsync(merchant.Id, cancellationToken)).Entitlements.CanEditCatalog
            ? (merchant, null)
            : (merchant, new ProductCategoryResult.PlanRequired());
    }

    private Task<ProductCategory?> FindAsync(int merchantId, int categoryId, CancellationToken cancellationToken) =>
        ProductCategoryStore.Of(db, merchantId).SingleOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
}

/// <summary>Consultas de categorías que comparten el catálogo y la pantalla de categorías.</summary>
internal static class ProductCategoryStore
{
    /// <summary>Categorías del vendedor, con <c>MerchantId</c> explícito.</summary>
    public static IQueryable<ProductCategory> Of(AppDbContext db, int merchantId) =>
        db.ProductCategories.IgnoreQueryFilters().Where(c => c.MerchantId == merchantId);

    /// <summary>Orden de la lista: <c>SortOrder</c>, luego nombre e id (por si hay empates).</summary>
    public static IQueryable<ProductCategory> Ordered(IQueryable<ProductCategory> categories) =>
        categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ThenBy(c => c.Id);

    /// <summary>Categoría con ese nombre (ya normalizado), sin importar mayúsculas: la columna es CI.</summary>
    public static Task<ProductCategory?> FindByNameAsync(AppDbContext db, int merchantId, string name, CancellationToken cancellationToken) =>
        Of(db, merchantId).SingleOrDefaultAsync(c => c.Name == name, cancellationToken);

    /// <summary>Lugar de una categoría nueva: al final.</summary>
    public static async Task<int> NextSortOrderAsync(AppDbContext db, int merchantId, CancellationToken cancellationToken) =>
        (await Of(db, merchantId).MaxAsync(c => (int?)c.SortOrder, cancellationToken) ?? -1) + 1;
}
