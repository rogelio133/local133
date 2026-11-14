using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Local133.Application.Products;
using Local133.Domain.Products;
using Local133.Web.Models;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>
/// Formulario de alta/edición de un producto. El precio llega como texto y se convierte con cultura invariante
/// (misma regla que el monto de <see cref="ChargeInput"/>).
/// </summary>
public sealed class ProductInput
{
    [Required(ErrorMessage = ProductMessages.NameRequired)]
    [StringLength(Product.NameMaxLength, ErrorMessage = ProductMessages.NameTooLong)]
    [RegularExpression(Product.NamePattern, ErrorMessage = ProductMessages.NameInvalid)]
    [Display(Name = "Nombre")]
    public string? Name { get; set; }

    [Required(ErrorMessage = ProductMessages.PriceRequired)]
    [RegularExpression(ChargeInput.AmountPattern, ErrorMessage = ProductMessages.PriceInvalid)]
    [Display(Name = "Precio")]
    public string? Price { get; set; }

    [StringLength(Product.DescriptionMaxLength, ErrorMessage = ProductMessages.DescriptionTooLong)]
    [Display(Name = "Descripción (opcional)")]
    public string? Description { get; set; }

    /// <summary>
    /// Foto nueva: llega de «Tomar foto» (cámara) o de «Elegir imagen» (galería o archivos); los dos campos se llaman
    /// igual y <c>product-form.js</c> vacía el otro al elegir.
    /// </summary>
    [Display(Name = "Foto")]
    public IFormFile? Photo { get; set; }

    /// <summary>Quitar la foto actual (edición).</summary>
    public bool RemovePhoto { get; set; }

    public bool TryGetPrice(out decimal price) =>
        decimal.TryParse(Price, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price);

    public static string FormatPrice(decimal price) => price.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>Pantalla de alta («Nuevo producto») o edición de un producto.</summary>
public sealed class ProductFormViewModel
{
    public ProductInput Input { get; set; } = new();

    /// <summary><c>null</c> = alta.</summary>
    public int? ProductId { get; init; }

    /// <summary>Foto guardada (edición); <c>null</c> = sin foto.</summary>
    public string? CurrentPhotoUrl { get; init; }

    /// <summary>
    /// Se envió una foto pero el formulario volvió con errores: el navegador no puede volver a llenar el campo, así
    /// que se le pide al vendedor que la elija de nuevo.
    /// </summary>
    public bool PhotoNeedsReselect { get; init; }

    public bool IsNew => ProductId is null;
}

/// <summary>Lista paginada de productos del vendedor (<see cref="PageSize"/> por página), con búsqueda por nombre.</summary>
public sealed class ProductListViewModel
{
    public const int PageSize = 20;

    /// <summary>Largo máximo de la búsqueda (el del nombre).</summary>
    public const int QueryMaxLength = Product.NameMaxLength;

    public required IReadOnlyList<ProductListItem> Items { get; init; }

    public required PageInfo Page { get; init; }

    /// <summary>Búsqueda aplicada (ya sin espacios en los extremos); <c>null</c> = sin búsqueda.</summary>
    public string? Query { get; init; }

    /// <summary>Puede dar de alta y editar (no está dado de baja).</summary>
    public bool CanManage { get; init; }

    /// <summary>No tiene ningún producto (sin búsqueda): se muestra el estado vacío.</summary>
    public bool IsCatalogEmpty => Query is null && Page.TotalCount == 0;

    /// <summary>URL de la lista con la búsqueda actual en otra página.</summary>
    public string UrlForPage(int page)
    {
        var parameters = new List<string>();
        if (Query is not null)
        {
            parameters.Add($"q={Uri.EscapeDataString(Query)}");
        }

        if (page > 1)
        {
            parameters.Add($"{PageInfo.QueryKey}={page.ToString(CultureInfo.InvariantCulture)}");
        }

        return parameters.Count == 0 ? ProductUrls.List : $"{ProductUrls.List}?{string.Join('&', parameters)}";
    }
}

public sealed record ProductListItem(int Id, string Name, string PriceText, string? ThumbnailUrl);

/// <summary>URLs de productos (rutas de <c>ProductsController</c>).</summary>
public static class ProductUrls
{
    public const string List = "/app/productos";

    /// <summary>Fragmento de resultados para la búsqueda asíncrona (mismos <c>q</c> y <c>pagina</c> que la lista).</summary>
    public const string Results = "/app/productos/resultados";

    public const string New = "/app/productos/nuevo";

    public static string Edit(int id) => $"/app/productos/{id}/editar";

    public static string Delete(int id) => $"/app/productos/{id}/eliminar";

    /// <summary>Foto grande; <c>v</c> cambia con cada foto nueva (la respuesta se guarda en caché para siempre).</summary>
    public static string Photo(int id, string photoKey) => $"/app/productos/{id}/foto?v={Version(photoKey)}";

    public static string Thumbnail(int id, string photoKey) => $"/app/productos/{id}/foto/miniatura?v={Version(photoKey)}";

    /// <summary>Último segmento de la llave (el guid de la foto).</summary>
    private static string Version(string photoKey) => photoKey[(photoKey.LastIndexOf('/') + 1)..];
}
