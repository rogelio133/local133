using Local133.Domain.Products;
using Local133.Domain.Sales;

namespace Local133.UnitTests.Sales;

/// <summary>Venta con carrito: copia de los productos (<see cref="SaleItem"/>) y reglas del total.</summary>
public class SaleCartTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private const string Concept = "Tacos Don Ramón · 2 Taco al pastor, Refresco";

    private const string Phone = "5512345678";

    private static Product NewProduct(int id, decimal price, string name = "Taco al pastor", int merchantId = 7)
    {
        var product = Product.Create(merchantId, name, null, price, Now);
        product.Id = id;
        return product;
    }

    private static List<SaleItem> TwoItems() =>
    [
        SaleItem.FromProduct(NewProduct(1, 25m), 2),
        SaleItem.FromProduct(NewProduct(2, 20.5m, "Refresco"), 1),
    ];

    [Fact]
    public void FromProduct_copia_nombre_precio_vendedor_y_calcula_el_importe()
    {
        var item = SaleItem.FromProduct(NewProduct(3, 12.35m), 3);

        Assert.Equal(7, item.MerchantId);
        Assert.Equal(3, item.ProductId);
        Assert.Equal("Taco al pastor", item.Name);
        Assert.Equal(12.35m, item.UnitPrice);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(37.05m, item.Amount);
    }

    [Fact]
    public void FromProduct_la_copia_no_cambia_si_el_producto_cambia_despues()
    {
        var product = NewProduct(3, 10m);
        var item = SaleItem.FromProduct(product, 1);

        product.Update("Otro nombre", null, 99m, Now);

        Assert.Equal("Taco al pastor", item.Name);
        Assert.Equal(10m, item.UnitPrice);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(SaleItem.MaxQuantity)]
    public void FromProduct_acepta_cantidades_de_1_a_999(int quantity) =>
        Assert.Equal(quantity, SaleItem.FromProduct(NewProduct(1, 1m), quantity).Quantity);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SaleItem.MaxQuantity + 1)]
    public void FromProduct_rechaza_cantidades_fuera_de_rango(int quantity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SaleItem.FromProduct(NewProduct(1, 1m), quantity));

    [Fact]
    public void FromProduct_rechaza_un_producto_dado_de_baja()
    {
        var product = NewProduct(1, 10m);
        product.Delete(Now);

        Assert.Throws<InvalidOperationException>(() => SaleItem.FromProduct(product, 1));
    }

    [Fact]
    public void Total_suma_los_importes()
    {
        Assert.Equal(70.5m, SaleCart.Total(TwoItems()));
        Assert.Equal(0m, SaleCart.Total([]));
    }

    [Fact]
    public void StartDirect_con_productos_es_una_venta_de_carrito()
    {
        var items = TwoItems();
        var sale = Sale.StartDirect(7, 1, 70.5m, Concept, Now.AddSeconds(600), Now, items);

        Assert.Equal(SaleSource.Cart, sale.Source);
        Assert.Equal(ChargeMethod.DynamicQr, sale.Method);
        Assert.Equal(70.5m, sale.Amount);
        Assert.Equal(items, sale.Items);
    }

    [Fact]
    public void StartDirect_sin_productos_sigue_siendo_monto_directo()
    {
        Assert.Equal(SaleSource.DirectAmount, Sale.StartDirect(7, 1, 10m, Concept, null, Now).Source);
        Assert.Equal(SaleSource.DirectAmount, Sale.StartDirect(7, 1, 10m, Concept, null, Now, []).Source);
        Assert.Empty(Sale.StartDirect(7, 1, 10m, Concept, null, Now).Items);
    }

    [Fact]
    public void StartPush_con_productos_es_una_venta_de_carrito_al_celular()
    {
        var sale = Sale.StartPush(7, 1, 70.5m, Concept, Phone, Now.AddSeconds(600), Now, TwoItems());

        Assert.Equal(SaleSource.Cart, sale.Source);
        Assert.Equal(ChargeMethod.PushNotification, sale.Method);
        Assert.Equal(2, sale.Items.Count);
        Assert.Equal(Phone, sale.PhoneNumber);
    }

    [Theory]
    [InlineData("70.49")]
    [InlineData("70.51")]
    [InlineData("10")]
    public void Rechaza_un_monto_que_no_es_la_suma_de_los_productos(string text)
    {
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentException>(() => Sale.StartDirect(7, 1, amount, Concept, null, Now, TwoItems()));
        Assert.Throws<ArgumentException>(() => Sale.StartPush(7, 1, amount, Concept, Phone, Now.AddSeconds(600), Now, TwoItems()));
    }

    [Fact]
    public void Rechaza_productos_de_otro_vendedor()
    {
        List<SaleItem> items = [SaleItem.FromProduct(NewProduct(1, 10m, merchantId: 8), 1)];

        Assert.Throws<ArgumentException>(() => Sale.StartDirect(7, 1, 10m, Concept, null, Now, items));
    }

    [Fact]
    public void Admite_hasta_50_productos_distintos()
    {
        var fifty = Enumerable.Range(1, SaleCart.MaxLines).Select(i => SaleItem.FromProduct(NewProduct(i, 1m), 1)).ToList();
        var fiftyOne = Enumerable.Range(1, SaleCart.MaxLines + 1).Select(i => SaleItem.FromProduct(NewProduct(i, 1m), 1)).ToList();

        Assert.Equal(SaleCart.MaxLines, Sale.StartDirect(7, 1, 50m, Concept, null, Now, fifty).Items.Count);
        Assert.Throws<ArgumentException>(() => Sale.StartDirect(7, 1, 51m, Concept, null, Now, fiftyOne));
    }

    [Fact]
    public void El_total_del_carrito_respeta_el_maximo_de_una_venta()
    {
        List<SaleItem> items = [SaleItem.FromProduct(NewProduct(1, 999_999.99m), 2)];

        Assert.Throws<ArgumentOutOfRangeException>(() => Sale.StartDirect(7, 1, SaleCart.Total(items), Concept, null, Now, items));
    }
}
