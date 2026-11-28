using Local133.Domain.Products;

namespace Local133.Application.Products;

/// <summary>Alta de una categoría desde la pantalla de categorías (por el usuario de Identity del vendedor).</summary>
public sealed record CreateProductCategoryCommand(string UserId, string? Name);

public sealed record RenameProductCategoryCommand(string UserId, int CategoryId, string? Name);

/// <summary>Borra la categoría; sus productos quedan sin categoría.</summary>
public sealed record DeleteProductCategoryCommand(string UserId, int CategoryId);

/// <summary>Orden nuevo: los ids de <b>todas</b> las categorías del vendedor, de la primera a la última.</summary>
public sealed record ReorderProductCategoriesCommand(string UserId, IReadOnlyList<int> CategoryIds);

/// <summary>Sube (<paramref name="Offset"/> = -1) o baja (+1) una categoría un lugar (botones ↑/↓).</summary>
public sealed record MoveProductCategoryCommand(string UserId, int CategoryId, int Offset);

public static class ProductCategoryMessages
{
    public const string NameRequired = "Escribe el nombre de la categoría.";

    public const string NameTooLong = "El nombre de la categoría admite hasta 40 caracteres.";

    /// <summary>Mismo texto (con sus espacios de no separación) que el del concepto del cobro.</summary>
    public const string NameInvalid = Sales.SaleConceptMessages.Invalid;

    public const string NewNameRequired = "Escribe el nombre de la categoría nueva.";

    public const string LimitReached = "Ya tienes 50 categorías, el máximo. Usa una de ellas o borra alguna que no uses.";

    public static string Duplicate(string name) => $"Ya tienes la categoría «{name}».";

    public const string OrderInvalid = "La lista de categorías cambió. Recarga la página e intenta de nuevo.";

    /// <summary>Mensaje del primer error del nombre (<see cref="ProductCategory.IsValidName"/>), o <c>null</c> si es válido.</summary>
    public static string? NameError(string? name)
    {
        var normalized = ProductCategory.Normalize(name);
        if (normalized is null)
        {
            return NameRequired;
        }

        if (normalized.Length > ProductCategory.NameMaxLength)
        {
            return NameTooLong;
        }

        return ProductCategory.IsValidName(normalized) ? null : NameInvalid;
    }
}

/// <summary>Resultado de crear, renombrar, borrar o mover categorías.</summary>
public abstract record ProductCategoryResult
{
    private ProductCategoryResult()
    {
    }

    public sealed record Saved(int CategoryId, string Name) : ProductCategoryResult;

    /// <summary>Borrada; <paramref name="ProductCount"/> productos vigentes quedaron sin categoría.</summary>
    public sealed record Deleted(string Name, int ProductCount) : ProductCategoryResult;

    /// <summary>Orden guardado (también si no cambió, p. ej. ↑ en la primera).</summary>
    public sealed record Reordered : ProductCategoryResult;

    /// <summary>Nombre inválido, repetido, tope alcanzado u orden que no corresponde a las categorías del vendedor.</summary>
    public sealed record Invalid(string Message) : ProductCategoryResult;

    /// <summary>No existe o es de otro vendedor.</summary>
    public sealed record NotFound : ProductCategoryResult;

    /// <summary>Vendedor dado de baja (solo lectura).</summary>
    public sealed record NotAllowed : ProductCategoryResult;

    /// <summary>El plan (Básico o vencido) deja el catálogo en solo lectura.</summary>
    public sealed record PlanRequired : ProductCategoryResult;
}

/// <summary>
/// Categorías del catálogo (implementado en Infrastructure). Mismos permisos que los productos:
/// <c>Merchant.CanManageProducts</c> y <c>PlanEntitlements.CanEditCatalog</c>.
/// </summary>
public interface IProductCategories
{
    Task<ProductCategoryResult> CreateAsync(CreateProductCategoryCommand command, CancellationToken cancellationToken = default);

    Task<ProductCategoryResult> RenameAsync(RenameProductCategoryCommand command, CancellationToken cancellationToken = default);

    Task<ProductCategoryResult> DeleteAsync(DeleteProductCategoryCommand command, CancellationToken cancellationToken = default);

    Task<ProductCategoryResult> ReorderAsync(ReorderProductCategoriesCommand command, CancellationToken cancellationToken = default);

    Task<ProductCategoryResult> MoveAsync(MoveProductCategoryCommand command, CancellationToken cancellationToken = default);
}
