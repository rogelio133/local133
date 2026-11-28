using Local133.Domain.Products;

namespace Local133.UnitTests.Products;

/// <summary>Categorías del catálogo (ajustes 2026-11-26, punto 14): nombre, normalización, tope y asignación.</summary>
public class ProductCategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normaliza_el_nombre()
    {
        var category = ProductCategory.Create(7, "  Tacos    dorados ", 3, Now);

        Assert.Equal(7, category.MerchantId);
        Assert.Equal("Tacos dorados", category.Name);
        Assert.Equal(3, category.SortOrder);
        Assert.Equal(Now, category.CreatedAt);
    }

    [Theory]
    [InlineData("Bebidas")]
    [InlineData("Aguas (1 L)")]
    [InlineData("Piñatas y más")]
    [InlineData("Combos #1, #2")]
    public void Acepta_los_caracteres_del_nombre_de_un_producto(string name) =>
        Assert.True(ProductCategory.IsValidName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bebidas · frías")]
    [InlineData("Postres!")]
    [InlineData("Tacos <b>")]
    public void Rechaza_vacio_o_caracteres_no_permitidos(string? name) =>
        Assert.False(ProductCategory.IsValidName(name));

    [Fact]
    public void El_largo_se_mide_ya_normalizado()
    {
        Assert.True(ProductCategory.IsValidName(new string('a', ProductCategory.NameMaxLength)));
        Assert.True(ProductCategory.IsValidName("  " + new string('a', ProductCategory.NameMaxLength) + "  "));
        Assert.False(ProductCategory.IsValidName(new string('a', ProductCategory.NameMaxLength + 1)));
    }

    [Fact]
    public void Rename_invalido_no_cambia_el_nombre()
    {
        var category = ProductCategory.Create(7, "Bebidas", 0, Now);

        Assert.Throws<ArgumentException>(() => category.Rename("   "));
        Assert.Equal("Bebidas", category.Name);
    }

    [Fact]
    public void El_tope_es_de_50() => Assert.Equal(50, ProductCategory.MaxPerMerchant);

    [Fact]
    public void SetCategory_pone_y_quita_la_categoria()
    {
        var product = Product.Create(7, "Taco", null, 25m, Now);
        var category = ProductCategory.Create(7, "Tacos", 0, Now);
        category.Id = 12;
        var later = Now.AddMinutes(5);

        product.SetCategory(category, later);
        Assert.Equal(12, product.CategoryId);
        Assert.Same(category, product.Category);
        Assert.Equal(later, product.UpdatedAt);

        product.SetCategory(null, later.AddMinutes(1));
        Assert.Null(product.CategoryId);
        Assert.Null(product.Category);
    }

    [Fact]
    public void SetCategory_con_la_misma_no_cambia_la_fecha()
    {
        var product = Product.Create(7, "Taco", null, 25m, Now);
        var category = ProductCategory.Create(7, "Tacos", 0, Now);
        category.Id = 12;
        product.SetCategory(category, Now.AddMinutes(1));

        product.SetCategory(category, Now.AddMinutes(9));

        Assert.Equal(Now.AddMinutes(1), product.UpdatedAt);
    }

    [Fact]
    public void SetCategory_con_una_nueva_deja_el_id_para_EF()
    {
        var product = Product.Create(7, "Taco", null, 25m, Now);
        var category = ProductCategory.Create(7, "Tacos", 0, Now);

        product.SetCategory(category, Now);

        Assert.Null(product.CategoryId);
        Assert.Same(category, product.Category);
    }

    [Fact]
    public void SetCategory_rechaza_una_categoria_de_otro_vendedor()
    {
        var product = Product.Create(7, "Taco", null, 25m, Now);

        Assert.Throws<ArgumentException>(() => product.SetCategory(ProductCategory.Create(8, "Tacos", 0, Now), Now));
    }
}
