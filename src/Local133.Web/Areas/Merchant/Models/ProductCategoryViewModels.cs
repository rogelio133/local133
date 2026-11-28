using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Local133.Application.Products;
using Local133.Domain.Products;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Nombre de una categoría (agregar y renombrar); el servidor lo valida otra vez con el dominio.</summary>
public sealed class ProductCategoryInput
{
    [Required(ErrorMessage = ProductCategoryMessages.NameRequired)]
    [StringLength(ProductCategory.NameMaxLength, ErrorMessage = ProductCategoryMessages.NameTooLong)]
    [RegularExpression(ProductCategory.NamePattern, ErrorMessage = ProductCategoryMessages.NameInvalid)]
    [Display(Name = "Nombre")]
    public string? Name { get; set; }
}

/// <summary>Pantalla «Categorías» (/app/productos/categorias).</summary>
public sealed class ProductCategoriesViewModel
{
    public required IReadOnlyList<ProductCategoryRow> Items { get; init; }

    /// <summary>Formulario «Agregar» (con los errores si volvió del servidor).</summary>
    public ProductCategoryInput Input { get; init; } = new();

    /// <summary>Por qué es de solo lectura (<c>null</c> = puede editar).</summary>
    public CatalogReadOnlyReason? ReadOnlyReason { get; init; }

    public bool CanManage => ReadOnlyReason is null;

    /// <summary>Puede agregar otra (no llegó a <see cref="ProductCategory.MaxPerMerchant"/>).</summary>
    public bool CanCreate => Items.Count < ProductCategory.MaxPerMerchant;
}

/// <summary>Fila de la lista, con sus productos vigentes.</summary>
public sealed record ProductCategoryRow(int Id, string Name, int ProductCount)
{
    public string ProductCountText => ProductCount switch
    {
        0 => "Sin productos",
        1 => "1 producto",
        _ => $"{ProductCount.ToString("#,0", CultureInfo.GetCultureInfo("es-MX"))} productos",
    };
}

/// <summary>Aviso de catálogo de solo lectura (Productos y Categorías).</summary>
/// <param name="WaitingText">«Tu catálogo de N productos te espera en Pro.» (solo Básico); <c>null</c> = sin texto.</param>
public sealed record CatalogReadOnlyNoticeViewModel(CatalogReadOnlyReason? Reason, string? WaitingText = null);
