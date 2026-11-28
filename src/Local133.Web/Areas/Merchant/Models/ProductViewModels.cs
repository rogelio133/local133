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

    /// <summary>
    /// Categoría elegida: vacío = sin categoría, el id de una existente o <see cref="NewCategoryValue"/> (crear una con
    /// <see cref="NewCategoryName"/>).
    /// </summary>
    [Display(Name = "Categoría (opcional)")]
    public string? Category { get; set; }

    /// <summary>
    /// Categoría nueva. Si trae texto, gana sobre <see cref="Category"/>: sin JS el campo siempre se ve; con JS solo al
    /// elegir «+ Nueva categoría…» (y si no, va deshabilitado y no se envía).
    /// </summary>
    [StringLength(ProductCategory.NameMaxLength, ErrorMessage = ProductCategoryMessages.NameTooLong)]
    [RegularExpression(ProductCategory.NamePattern, ErrorMessage = ProductCategoryMessages.NameInvalid)]
    [Display(Name = "Nombre de la categoría nueva")]
    public string? NewCategoryName { get; set; }

    /// <summary>Valor de la opción «+ Nueva categoría…».</summary>
    public const string NewCategoryValue = "nueva";

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

    /// <summary>Categorías del vendedor en su orden (opciones del <c>select</c>).</summary>
    public IReadOnlyList<CategoryOption> Categories { get; init; } = [];

    /// <summary>Puede crear otra (no llegó a <see cref="ProductCategory.MaxPerMerchant"/>).</summary>
    public bool CanCreateCategory { get; init; } = true;
}

/// <summary>Categoría del vendedor (opción de un <c>select</c> o chip de filtro).</summary>
public sealed record CategoryOption(int Id, string Name)
{
    public string Value => Id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Filtro por categoría de las listas de productos (<c>?categoria={id}</c> o <c>?categoria=sin</c>).</summary>
public static class CategoryFilters
{
    public const string QueryKey = "categoria";

    /// <summary>Productos sin categoría.</summary>
    public const string Uncategorized = "sin";
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

    /// <summary>Puede dar de alta y editar: no está dado de baja y su plan no deja el catálogo en solo lectura.</summary>
    public bool CanManage { get; init; }

    /// <summary>Por qué es de solo lectura (<c>null</c> si <see cref="CanManage"/>).</summary>
    public CatalogReadOnlyReason? ReadOnlyReason { get; init; }

    /// <summary>«Tu catálogo de 63 productos te espera en Pro.» (Básico o vencido); <c>null</c> sin productos.</summary>
    public static string? WaitingCatalogText(int count) => count switch
    {
        <= 0 => null,
        1 => "Tu catálogo de 1 producto te espera en Pro.",
        _ => $"Tu catálogo de {count.ToString("#,0", CultureInfo.GetCultureInfo("es-MX"))} productos te espera en Pro.",
    };

    /// <summary>Ruta de la página que muestra la lista (paginación y «Ver todos»): la de productos o la del carrito.</summary>
    public string ListPath { get; init; } = ProductUrls.List;

    /// <summary>Categorías del vendedor en su orden (chips del filtro); vacía = sin filtro.</summary>
    public IReadOnlyList<CategoryOption> Categories { get; init; } = [];

    /// <summary>Hay productos sin categoría (chip «Sin categoría»).</summary>
    public bool HasUncategorized { get; init; }

    /// <summary>
    /// Filtro aplicado: <c>null</c> = todas, <see cref="CategoryFilters.Uncategorized"/> o el id de una categoría del
    /// vendedor (uno ajeno o inexistente se ignora).
    /// </summary>
    public string? CategoryFilter { get; init; }

    /// <summary>Nombre de la categoría del filtro («Sin categoría» incluido), o <c>null</c> sin filtro.</summary>
    public string? CategoryFilterName => CategoryFilter switch
    {
        null => null,
        CategoryFilters.Uncategorized => "Sin categoría",
        var id => Categories.FirstOrDefault(c => c.Value == id)?.Name,
    };

    /// <summary>Los chips solo aparecen si el vendedor tiene categorías.</summary>
    public bool ShowCategoryFilters => Categories.Count > 0;

    /// <summary>No tiene ningún producto (sin búsqueda ni filtro): se muestra el estado vacío.</summary>
    public bool IsCatalogEmpty => Query is null && CategoryFilter is null && Page.TotalCount == 0;

    /// <summary>URL de la lista con la búsqueda y la categoría actuales en otra página.</summary>
    public string UrlForPage(int page) => UrlFor(Query, CategoryFilter, page);

    /// <summary>URL de un chip: la búsqueda actual con otra categoría (<c>null</c> = todas), en la primera página.</summary>
    public string UrlForCategory(string? category) => UrlFor(Query, category, 1);

    /// <summary>«Ver todos»: sin búsqueda, conserva la categoría.</summary>
    public string ClearSearchUrl => UrlFor(null, CategoryFilter, 1);

    private string UrlFor(string? query, string? category, int page)
    {
        var parameters = new List<string>();
        if (query is not null)
        {
            parameters.Add($"q={Uri.EscapeDataString(query)}");
        }

        if (category is not null)
        {
            parameters.Add($"{CategoryFilters.QueryKey}={Uri.EscapeDataString(category)}");
        }

        if (page > 1)
        {
            parameters.Add($"{PageInfo.QueryKey}={page.ToString(CultureInfo.InvariantCulture)}");
        }

        return parameters.Count == 0 ? ListPath : $"{ListPath}?{string.Join('&', parameters)}";
    }
}

/// <summary>
/// Fila de la lista; <see cref="Price"/> lo usa el carrito (atributo <c>data-price</c>). <see cref="CategoryName"/> se
/// muestra solo sin filtro de categoría.
/// </summary>
public sealed record ProductListItem(int Id, string Name, decimal Price, string PriceText, string? ThumbnailUrl, string? CategoryName = null)
{
    /// <summary>Precio en texto invariante con 2 decimales (para JS).</summary>
    public string PriceValue => Price.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>URLs de productos (rutas de <c>ProductsController</c>).</summary>
public static class ProductUrls
{
    public const string List = "/app/productos";

    /// <summary>Fragmento de resultados para la búsqueda asíncrona (mismos <c>q</c> y <c>pagina</c> que la lista).</summary>
    public const string Results = "/app/productos/resultados";

    public const string New = "/app/productos/nuevo";

    /// <summary>Pantalla de categorías (<c>ProductCategoriesController</c>).</summary>
    public const string Categories = "/app/productos/categorias";

    public const string CategoriesOrder = "/app/productos/categorias/orden";

    public static string RenameCategory(int id) => $"/app/productos/categorias/{id}/renombrar";

    public static string DeleteCategory(int id) => $"/app/productos/categorias/{id}/eliminar";

    public static string MoveCategoryUp(int id) => $"/app/productos/categorias/{id}/subir";

    public static string MoveCategoryDown(int id) => $"/app/productos/categorias/{id}/bajar";

    public static string Edit(int id) => $"/app/productos/{id}/editar";

    public static string Delete(int id) => $"/app/productos/{id}/eliminar";

    /// <summary>Foto grande; <c>v</c> cambia con cada foto nueva (la respuesta se guarda en caché para siempre).</summary>
    public static string Photo(int id, string photoKey) => $"/app/productos/{id}/foto?v={Version(photoKey)}";

    public static string Thumbnail(int id, string photoKey) => $"/app/productos/{id}/foto/miniatura?v={Version(photoKey)}";

    /// <summary>Último segmento de la llave (el guid de la foto); también lo usa el catálogo público (5.10).</summary>
    public static string Version(string photoKey) => photoKey[(photoKey.LastIndexOf('/') + 1)..];
}

/// <summary>Por qué el catálogo es de solo lectura.</summary>
public enum CatalogReadOnlyReason
{
    /// <summary>El vendedor está dado de baja.</summary>
    Disabled,

    /// <summary>Básico: el catálogo se consulta; editarlo es de Pro.</summary>
    Basic,

    /// <summary>El plan venció (5.14): el catálogo se consulta hasta que elija un plan.</summary>
    Expired,
}
