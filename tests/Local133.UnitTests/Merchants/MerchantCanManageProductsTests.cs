using Local133.Domain.Merchants;

namespace Local133.UnitTests.Merchants;

/// <summary>Quién puede modificar su catálogo: todos salvo un vendedor dado de baja.</summary>
public class MerchantCanManageProductsTests
{
    [Theory]
    [InlineData(MerchantStatus.PendingBankAccount, true)]
    [InlineData(MerchantStatus.BankAccountInReview, true)]
    [InlineData(MerchantStatus.Active, true)]
    [InlineData(MerchantStatus.Suspended, true)]
    [InlineData(MerchantStatus.Disabled, false)]
    public void Puede_manejar_productos_salvo_dado_de_baja(MerchantStatus status, bool expected)
    {
        var merchant = Merchant.Register("user-1", DateTimeOffset.UtcNow);
        merchant.Status = status;

        Assert.Equal(expected, merchant.CanManageProducts);
    }
}
