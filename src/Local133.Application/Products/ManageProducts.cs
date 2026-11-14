using FluentValidation;
using Local133.Domain.Pey;
using Local133.Domain.Products;
using Local133.Domain.Sales;

namespace Local133.Application.Products;

/// <summary>Foto que subió el vendedor (aún sin procesar).</summary>
/// <param name="Content">Contenido del archivo; quien llama lo cierra.</param>
/// <param name="Length">Tamaño declarado del archivo en bytes.</param>
public sealed record ProductPhotoUpload(Stream Content, long Length);

/// <summary>
/// Alta (<paramref name="ProductId"/> <c>null</c>) o edición de un producto del vendedor (por su usuario de Identity).
/// </summary>
/// <param name="Photo">Foto nueva (reemplaza la actual); <c>null</c> = sin cambio.</param>
/// <param name="RemovePhoto">Quitar la foto actual (se ignora si viene <paramref name="Photo"/>).</param>
public sealed record SaveProductCommand(
    string UserId,
    int? ProductId,
    string? Name,
    string? Description,
    decimal Price,
    ProductPhotoUpload? Photo = null,
    bool RemovePhoto = false);

/// <summary>Baja lógica de un producto del vendedor.</summary>
public sealed record DeleteProductCommand(string UserId, int ProductId);

/// <summary>Validación en servidor (la del formulario en cliente es solo de apoyo).</summary>
public sealed class SaveProductCommandValidator : AbstractValidator<SaveProductCommand>
{
    public SaveProductCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Name)
            .Cascade(CascadeMode.Stop)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage(ProductMessages.NameRequired)
            .Must(n => n!.Trim().Length <= Product.NameMaxLength).WithMessage(ProductMessages.NameTooLong)
            .Must(Product.IsValidName).WithMessage(ProductMessages.NameInvalid);
        RuleFor(c => c.Description)
            .Must(Product.IsValidDescription).WithMessage(ProductMessages.DescriptionTooLong);
        RuleFor(c => c.Price)
            .GreaterThanOrEqualTo(Sale.MinAmount).WithMessage(ProductMessages.PriceTooLow)
            .LessThanOrEqualTo(Sale.MaxAmount).WithMessage(ProductMessages.PriceTooHigh)
            .Must(p => decimal.Round(p, PeyLimits.AmountDecimals) == p).WithMessage(ProductMessages.PriceTooManyDecimals);
        When(c => c.Photo is not null, () =>
            RuleFor(c => c.Photo!.Length)
                .Cascade(CascadeMode.Stop)
                .GreaterThan(0).WithMessage(ProductMessages.PhotoEmpty)
                .LessThanOrEqualTo(ProductPhotoRules.MaxUploadBytes).WithMessage(ProductMessages.PhotoTooLarge)
                .OverridePropertyName(nameof(SaveProductCommand.Photo)));
    }
}

public static class ProductMessages
{
    public const string NameRequired = "Escribe el nombre del producto.";

    public const string NameTooLong = "El nombre admite hasta 80 caracteres.";

    /// <summary>Mismo texto (con sus espacios de no separación) que el del concepto del cobro.</summary>
    public const string NameInvalid = Sales.SaleConceptMessages.Invalid;

    public const string DescriptionTooLong = "La descripción admite hasta 500 caracteres.";

    public const string PriceRequired = "Escribe el precio del producto.";

    public const string PriceInvalid = "Escribe un precio válido, sin signos y con hasta 2 decimales (por ejemplo 25.50).";

    public const string PriceTooLow = "El precio mínimo es $0.01.";

    public const string PriceTooHigh = "El precio máximo es $999,999.99.";

    public const string PriceTooManyDecimals = "El precio admite hasta 2 decimales.";

    public const string PhotoEmpty = "La foto está vacía. Elige otra.";

    public const string PhotoTooLarge = "La foto pesa más de 10 MB. Elige una más ligera.";

    public const string PhotoInvalid = "No pudimos leer la imagen. Usa una foto JPG, PNG o WebP.";

    public const string PhotoTooManyPixels = "La imagen es demasiado grande. Usa una foto de menos de 50 megapíxeles.";

    public const string NotAllowed = "Tu cuenta está dada de baja: no puedes modificar tus productos.";
}

/// <summary>Resultado de guardar un producto.</summary>
public abstract record SaveProductResult
{
    private SaveProductResult()
    {
    }

    public sealed record Saved(int ProductId) : SaveProductResult;

    /// <summary>Datos inválidos; llave = propiedad del comando (<c>Name</c>, <c>Description</c>, <c>Price</c>, <c>Photo</c>).</summary>
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : SaveProductResult;

    /// <summary>No existe, es de otro vendedor o está dado de baja.</summary>
    public sealed record NotFound : SaveProductResult;

    /// <summary>El vendedor no puede modificar su catálogo (dado de baja).</summary>
    public sealed record NotAllowed : SaveProductResult;
}

/// <summary>Resultado de dar de baja un producto.</summary>
public abstract record DeleteProductResult
{
    private DeleteProductResult()
    {
    }

    public sealed record Deleted : DeleteProductResult;

    public sealed record NotFound : DeleteProductResult;

    public sealed record NotAllowed : DeleteProductResult;
}

/// <summary>Catálogo del vendedor (implementado en Infrastructure: EF Core + <see cref="Storage.IFileStorage"/>).</summary>
public interface IProductCatalog
{
    Task<SaveProductResult> SaveAsync(SaveProductCommand command, CancellationToken cancellationToken = default);

    Task<DeleteProductResult> DeleteAsync(DeleteProductCommand command, CancellationToken cancellationToken = default);
}
