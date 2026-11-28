using System.Text.RegularExpressions;
using Local133.Domain.Common;

namespace Local133.Domain.Products;

/// <summary>
/// Categoría del catálogo del vendedor (ajustes 2026-11-26, punto 14). El nombre es único por vendedor sin importar
/// mayúsculas (índice único con intercalación CI) y las categorías se muestran en <see cref="SortOrder"/>. Borrarla deja
/// sus productos sin categoría (<c>ON DELETE SET NULL</c>); las ventas no guardan la categoría.
/// </summary>
public partial class ProductCategory : Entity
{
    public const int NameMaxLength = 40;

    /// <summary>Tope de categorías por vendedor.</summary>
    public const int MaxPerMerchant = 50;

    /// <summary>Mismos caracteres que el nombre de un producto (<see cref="Product.NamePattern"/>).</summary>
    public const string NamePattern = Product.NamePattern;

    public int MerchantId { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Posición en la lista del vendedor (0 = primera). La pantalla de categorías la reescribe de 0 a N.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static ProductCategory Create(int merchantId, string name, int sortOrder, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(merchantId);
        ArgumentOutOfRangeException.ThrowIfNegative(sortOrder);
        var category = new ProductCategory { MerchantId = merchantId, SortOrder = sortOrder, CreatedAt = now };
        category.Rename(name);
        return category;
    }

    /// <summary>Cambia el nombre (normalizado con <see cref="Normalize"/>).</summary>
    public void Rename(string name)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException(
                $"El nombre es obligatorio, admite hasta {NameMaxLength} caracteres y solo letras, números, espacios y . , - # / ( )",
                nameof(name));
        }

        Name = Normalize(name)!;
    }

    /// <summary>Quita los espacios de los extremos y junta los repetidos («  Tacos   dorados » ⇒ «Tacos dorados»); vacío ⇒ <c>null</c>.</summary>
    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return SpacesRegex().Replace(name.Trim(), " ");
    }

    /// <summary>Nombre válido: no vacío, hasta <see cref="NameMaxLength"/> ya normalizado y con los caracteres de <see cref="NamePattern"/>.</summary>
    public static bool IsValidName(string? name) =>
        Normalize(name) is { } normalized && normalized.Length <= NameMaxLength && NameRegex().IsMatch(normalized);

    [GeneratedRegex(NamePattern)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
