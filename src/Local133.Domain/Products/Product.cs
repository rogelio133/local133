using System.Text.RegularExpressions;
using Local133.Domain.Common;
using Local133.Domain.Sales;

namespace Local133.Domain.Products;

/// <summary>
/// Producto del catálogo del vendedor. Se da de baja de forma lógica (<see cref="IsDeleted"/>): las ventas guardan
/// su propia copia del nombre y el precio (<c>SaleItems</c>), así que borrar un producto nunca altera lo vendido.
/// </summary>
public partial class Product : Entity
{
    public const int NameMaxLength = 80;

    public const int DescriptionMaxLength = 500;

    /// <summary>Llave base de la foto en el almacenamiento (<c>products/{merchantId}/{guid}</c>).</summary>
    public const int PhotoKeyMaxLength = 100;

    /// <summary>
    /// Caracteres del nombre: los mismos del concepto de cobro (<see cref="Sale.ConceptTextPattern"/>), porque en el
    /// carrito los nombres forman el concepto que se envía a Pey.
    /// </summary>
    public const string NamePattern = Sale.ConceptTextPattern;

    public int MerchantId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>Precio en MXN con hasta 2 decimales (mismos límites que una venta).</summary>
    public decimal Price { get; set; }

    /// <summary>Llave base de la foto; <c>null</c> = sin foto. Cambia (guid nuevo) cada vez que se reemplaza.</summary>
    public string? PhotoKey { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public static Product Create(int merchantId, string name, string? description, decimal price, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(merchantId);
        var product = new Product { MerchantId = merchantId, CreatedAt = now };
        product.Update(name, description, price, now);
        return product;
    }

    /// <summary>Cambia nombre, descripción y precio (los espacios en los extremos se quitan; descripción vacía = sin descripción).</summary>
    public void Update(string name, string? description, decimal price, DateTimeOffset now)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException(
                $"El nombre es obligatorio, admite hasta {NameMaxLength} caracteres y solo letras, números, espacios y . , - # / ( )",
                nameof(name));
        }

        if (!IsValidDescription(description))
        {
            throw new ArgumentException($"La descripción admite hasta {DescriptionMaxLength} caracteres.", nameof(description));
        }

        if (!IsValidPrice(price))
        {
            throw new ArgumentOutOfRangeException(nameof(price), price,
                $"El precio debe estar entre {Sale.MinAmount} y {Sale.MaxAmount} con hasta 2 decimales.");
        }

        EnsureNotDeleted();
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Price = price;
        UpdatedAt = now;
    }

    /// <summary>Nombre válido: no vacío, hasta <see cref="NameMaxLength"/> (sin contar extremos) y con los caracteres de <see cref="NamePattern"/>.</summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        return trimmed.Length <= NameMaxLength && NameRegex().IsMatch(trimmed);
    }

    public static bool IsValidDescription(string? description) =>
        description is null || description.Trim().Length <= DescriptionMaxLength;

    /// <summary>Mismos límites que el monto de una venta: de $0.01 a $999,999.99, en centavos.</summary>
    public static bool IsValidPrice(decimal price) => Sale.IsValidAmount(price);

    /// <summary>Pone una foto nueva; devuelve la llave de la anterior (para borrar sus archivos) o <c>null</c>.</summary>
    public string? ReplacePhoto(string photoKey, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(photoKey);
        if (photoKey.Length > PhotoKeyMaxLength)
        {
            throw new ArgumentException($"La llave de la foto admite hasta {PhotoKeyMaxLength} caracteres.", nameof(photoKey));
        }

        EnsureNotDeleted();
        var previous = PhotoKey;
        PhotoKey = photoKey;
        UpdatedAt = now;
        return previous;
    }

    /// <summary>Quita la foto; devuelve la llave que tenía (para borrar sus archivos) o <c>null</c> si no tenía.</summary>
    public string? RemovePhoto(DateTimeOffset now)
    {
        EnsureNotDeleted();
        var previous = PhotoKey;
        if (previous is not null)
        {
            PhotoKey = null;
            UpdatedAt = now;
        }

        return previous;
    }

    /// <summary>Baja lógica (conserva la fila y la foto). Devuelve <c>false</c> si ya estaba dado de baja.</summary>
    public bool Delete(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            return false;
        }

        IsDeleted = true;
        DeletedAt = now;
        UpdatedAt = now;
        return true;
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException($"El producto {Id} está dado de baja.");
        }
    }

    [GeneratedRegex(NamePattern)]
    private static partial Regex NameRegex();
}
