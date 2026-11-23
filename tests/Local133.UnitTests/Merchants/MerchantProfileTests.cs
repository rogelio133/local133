using Local133.Domain.Merchants;

namespace Local133.UnitTests.Merchants;

/// <summary>4.1: «Mi información» en el vendedor (datos, imágenes, quién edita y el paso opcional de Inicio).</summary>
public class MerchantProfileTests
{
    private static Merchant NewMerchant() => Merchant.Register("user-1", DateTimeOffset.UtcNow, "María López");

    [Fact]
    public void UpdateProfile_guarda_los_datos_normalizados()
    {
        var merchant = NewMerchant();

        merchant.UpdateProfile("  María   José O’Brien ", "  Tacos   Don Ramón ", "  Pasillo 4 \r\n\r\n Mercado Sur ");

        Assert.Equal("María José O'Brien", merchant.OwnerName);
        Assert.Equal("Tacos Don Ramón", merchant.BusinessName);
        Assert.Equal("Pasillo 4\nMercado Sur", merchant.Address);
        Assert.Equal("María", merchant.GreetingName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void UpdateProfile_sin_direccion_la_guarda_nula(string? address)
    {
        var merchant = NewMerchant();
        merchant.Address = "Pasillo 4";

        merchant.UpdateProfile("María López", "Tacos Don Ramón", address);

        Assert.Null(merchant.Address);
    }

    [Theory]
    [InlineData("María 2", "Tacos", null, "ownerName")]
    [InlineData("María", "", null, "businessName")]
    [InlineData("María", "Tacos · Don Ramón", null, "businessName")]
    [InlineData("María", "Tacos", "Uno\nDos\nTres\nCuatro", "address")]
    public void UpdateProfile_rechaza_datos_invalidos_sin_cambiar_nada(
        string ownerName, string businessName, string? address, string parameter)
    {
        var merchant = NewMerchant();
        merchant.BusinessName = "Negocio";

        var error = Assert.Throws<ArgumentException>(() => merchant.UpdateProfile(ownerName, businessName, address));

        Assert.Equal(parameter, error.ParamName);
        Assert.Equal("María López", merchant.OwnerName);
        Assert.Equal("Negocio", merchant.BusinessName);
    }

    [Fact]
    public void ReplaceLogo_y_ReplaceShopPhoto_devuelven_la_llave_anterior()
    {
        var merchant = NewMerchant();

        Assert.Null(merchant.ReplaceLogo("merchants/1/logo-a"));
        Assert.Equal("merchants/1/logo-a", merchant.ReplaceLogo("merchants/1/logo-b"));
        Assert.Equal("merchants/1/logo-b", merchant.ReplaceLogo(null));
        Assert.Null(merchant.LogoKey);

        Assert.Null(merchant.ReplaceShopPhoto("merchants/1/local-a"));
        Assert.Equal("merchants/1/local-a", merchant.ReplaceShopPhoto(null));
        Assert.Null(merchant.ShopPhotoKey);
    }

    [Theory]
    [InlineData(MerchantStatus.PendingBankAccount, true)]
    [InlineData(MerchantStatus.BankAccountInReview, true)]
    [InlineData(MerchantStatus.Active, true)]
    [InlineData(MerchantStatus.Suspended, true)]
    [InlineData(MerchantStatus.Disabled, false)]
    public void Puede_editar_su_informacion_salvo_dado_de_baja(MerchantStatus status, bool expected)
    {
        var merchant = NewMerchant();
        merchant.Status = status;

        Assert.Equal(expected, merchant.CanEditProfile);
    }

    [Theory]
    [InlineData("Tacos Don Ramón", "5512345678", true)]
    [InlineData("Tacos Don Ramón", null, false)]
    [InlineData("Tacos Don Ramón", "551234567", false)]
    [InlineData(null, "5512345678", false)]
    [InlineData("  ", "5512345678", false)]
    public void La_informacion_esta_completa_con_negocio_y_telefono(string? business, string? phone, bool expected)
    {
        var merchant = NewMerchant();
        merchant.BusinessName = business;

        Assert.Equal(expected, merchant.HasCompleteProfile(phone));
    }
}
