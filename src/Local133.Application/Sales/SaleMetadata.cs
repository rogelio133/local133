namespace Local133.Application.Sales;

/// <summary>
/// Metadata que viaja con cada cobro a Pey y vuelve (como texto) en <c>metadata.externalMetadata</c> del webhook y
/// de las transacciones: con ella se concilia el pago con la venta.
/// </summary>
public static class SaleMetadata
{
    public const string KindKey = "kind";
    public const string MerchantIdKey = "merchantId";
    public const string SaleIdKey = "saleId";

    /// <summary><c>kind</c> de una venta del vendedor.</summary>
    public const string SaleKind = "sale";

    /// <summary><c>kind</c> del QR fijo del puesto.</summary>
    public const string StaticKind = "static";
}
