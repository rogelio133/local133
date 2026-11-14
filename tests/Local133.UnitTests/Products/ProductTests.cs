using System.Globalization;
using Local133.Domain.Products;

namespace Local133.UnitTests.Products;

/// <summary>Producto del catálogo: nombre, descripción, precio, foto y baja lógica.</summary>
public class ProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static Product NewProduct() => Product.Create(7, "Taco al pastor", "Con piña", 25m, Now);

    [Fact]
    public void Create_guarda_los_datos_sin_espacios_en_los_extremos()
    {
        var product = Product.Create(7, "  Taco al pastor ", "  Con piña  ", 25.5m, Now);

        Assert.Equal(7, product.MerchantId);
        Assert.Equal("Taco al pastor", product.Name);
        Assert.Equal("Con piña", product.Description);
        Assert.Equal(25.5m, product.Price);
        Assert.Null(product.PhotoKey);
        Assert.False(product.IsDeleted);
        Assert.Equal(Now, product.CreatedAt);
        Assert.Equal(Now, product.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Descripcion_vacia_queda_sin_descripcion(string? description) =>
        Assert.Null(Product.Create(7, "Taco", description, 25m, Now).Description);

    [Theory]
    [InlineData("Taco al pastor")]
    [InlineData("Agua de jamaica (1 L)")]
    [InlineData("Paquete #3, chico/grande - 2.5 kg")]
    [InlineData("Piñata ÁÉÍÓÚÜÑ áéíóúüñ")]
    public void Acepta_nombres_con_los_caracteres_del_concepto(string name)
    {
        Assert.True(Product.IsValidName(name));
        Assert.Equal(name, Product.Create(7, name, null, 1m, Now).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Taco 🌮")]
    [InlineData("Taco @ casa")]
    [InlineData("Taco_pastor")]
    [InlineData("Precio $10")]
    public void Rechaza_nombres_vacios_o_con_caracteres_no_permitidos(string? name)
    {
        Assert.False(Product.IsValidName(name));
        Assert.Throws<ArgumentException>(() => Product.Create(7, name!, null, 1m, Now));
    }

    [Fact]
    public void El_nombre_admite_hasta_80_caracteres_sin_contar_los_extremos()
    {
        var max = new string('a', Product.NameMaxLength);

        Assert.True(Product.IsValidName($"  {max}  "));
        Assert.False(Product.IsValidName(max + "a"));
    }

    [Fact]
    public void La_descripcion_admite_hasta_500_caracteres()
    {
        var max = new string('x', Product.DescriptionMaxLength);

        Assert.Equal(max, Product.Create(7, "Taco", max, 1m, Now).Description);
        Assert.False(Product.IsValidDescription(max + "x"));
        Assert.Throws<ArgumentException>(() => Product.Create(7, "Taco", max + "x", 1m, Now));
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("25")]
    [InlineData("25.5")]
    [InlineData("999999.99")]
    public void Acepta_precios_de_un_centavo_al_maximo(string text)
    {
        var price = decimal.Parse(text, CultureInfo.InvariantCulture);

        Assert.True(Product.IsValidPrice(price));
        Assert.Equal(price, Product.Create(7, "Taco", null, price, Now).Price);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.005")]
    [InlineData("1000000")]
    public void Rechaza_precios_invalidos(string text)
    {
        var price = decimal.Parse(text, CultureInfo.InvariantCulture);

        Assert.False(Product.IsValidPrice(price));
        Assert.Throws<ArgumentOutOfRangeException>(() => Product.Create(7, "Taco", null, price, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Requiere_vendedor(int merchantId) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Product.Create(merchantId, "Taco", null, 1m, Now));

    [Fact]
    public void Update_cambia_los_datos_y_la_fecha()
    {
        var product = NewProduct();
        var later = Now.AddHours(1);

        product.Update("Gringa", null, 45m, later);

        Assert.Equal("Gringa", product.Name);
        Assert.Null(product.Description);
        Assert.Equal(45m, product.Price);
        Assert.Equal(Now, product.CreatedAt);
        Assert.Equal(later, product.UpdatedAt);
    }

    [Fact]
    public void ReplacePhoto_devuelve_la_llave_anterior()
    {
        var product = NewProduct();

        Assert.Null(product.ReplacePhoto("products/7/a", Now));
        Assert.Equal("products/7/a", product.ReplacePhoto("products/7/b", Now.AddMinutes(1)));
        Assert.Equal("products/7/b", product.PhotoKey);
        Assert.Equal(Now.AddMinutes(1), product.UpdatedAt);
    }

    [Fact]
    public void ReplacePhoto_rechaza_llaves_vacias_o_largas()
    {
        var product = NewProduct();

        Assert.Throws<ArgumentException>(() => product.ReplacePhoto(" ", Now));
        Assert.Throws<ArgumentException>(() => product.ReplacePhoto(new string('a', Product.PhotoKeyMaxLength + 1), Now));
    }

    [Fact]
    public void RemovePhoto_devuelve_la_llave_que_tenia()
    {
        var product = NewProduct();
        product.ReplacePhoto("products/7/a", Now);

        Assert.Equal("products/7/a", product.RemovePhoto(Now));
        Assert.Null(product.PhotoKey);
        Assert.Null(product.RemovePhoto(Now));
    }

    [Fact]
    public void Delete_es_baja_logica_e_idempotente()
    {
        var product = NewProduct();
        product.ReplacePhoto("products/7/a", Now);
        var later = Now.AddDays(1);

        Assert.True(product.Delete(later));
        Assert.False(product.Delete(later.AddDays(1)));

        Assert.True(product.IsDeleted);
        Assert.Equal(later, product.DeletedAt);
        // La foto se conserva (baja lógica completa).
        Assert.Equal("products/7/a", product.PhotoKey);
    }

    [Fact]
    public void Un_producto_dado_de_baja_no_se_modifica()
    {
        var product = NewProduct();
        product.Delete(Now);

        Assert.Throws<InvalidOperationException>(() => product.Update("Otro", null, 1m, Now));
        Assert.Throws<InvalidOperationException>(() => product.ReplacePhoto("products/7/a", Now));
        Assert.Throws<InvalidOperationException>(() => product.RemovePhoto(Now));
    }
}
