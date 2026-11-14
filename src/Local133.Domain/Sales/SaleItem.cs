using Local133.Domain.Common;
using Local133.Domain.Products;

namespace Local133.Domain.Sales;

/// <summary>
/// Producto de una venta con carrito. Guarda su propia copia del nombre y el precio al momento de cobrar: editar o
/// dar de baja el producto después nunca altera lo vendido.
/// </summary>
public sealed class SaleItem : Entity
{
    /// <summary>Cantidad máxima de un mismo producto en una venta.</summary>
    public const int MaxQuantity = 999;

    public int SaleId { get; set; }

    public int MerchantId { get; set; }

    /// <summary>Producto del catálogo del que salió (referencia; los datos que valen son la copia).</summary>
    public int? ProductId { get; set; }

    /// <summary>Nombre del producto al momento de la venta.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Precio unitario al momento de la venta.</summary>
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    /// <summary>Importe: precio unitario × cantidad.</summary>
    public decimal Amount { get; set; }

    /// <summary>Copia del producto (vigente) con la cantidad indicada.</summary>
    public static SaleItem FromProduct(Product product, int quantity)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (product.IsDeleted)
        {
            throw new InvalidOperationException($"El producto {product.Id} está dado de baja.");
        }

        if (!IsValidQuantity(quantity))
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, $"La cantidad debe ser de 1 a {MaxQuantity}.");
        }

        return new SaleItem
        {
            MerchantId = product.MerchantId,
            ProductId = product.Id,
            Name = product.Name,
            UnitPrice = product.Price,
            Quantity = quantity,
            Amount = product.Price * quantity,
        };
    }

    public static bool IsValidQuantity(int quantity) => quantity is >= 1 and <= MaxQuantity;
}

/// <summary>Reglas del carrito de una venta.</summary>
public static class SaleCart
{
    /// <summary>Productos distintos que admite una venta.</summary>
    public const int MaxLines = 50;

    public static decimal Total(IEnumerable<SaleItem> items) => items.Sum(i => i.Amount);
}
