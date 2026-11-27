using Local133.Domain.Merchants;
using Local133.Domain.Subscriptions;

namespace Local133.UnitTests.Merchants;

/// <summary>5.10: publicar el catálogo y cuándo se muestra.</summary>
public class MerchantPublicCatalogTests
{
    private static Merchant NewMerchant(MerchantStatus status = MerchantStatus.Active)
    {
        var merchant = Merchant.Register("user-1", DateTimeOffset.UtcNow, "María López");
        merchant.Status = status;
        return merchant;
    }

    [Fact]
    public void Publicar_guarda_el_slug_y_despublicar_lo_conserva()
    {
        var merchant = NewMerchant();

        merchant.PublishCatalog("tacos-ramon");
        Assert.True(merchant.PublicCatalogEnabled);
        Assert.Equal("tacos-ramon", merchant.PublicSlug);

        merchant.UnpublishCatalog();
        Assert.False(merchant.PublicCatalogEnabled);
        Assert.Equal("tacos-ramon", merchant.PublicSlug);
    }

    [Theory]
    [InlineData("Tacos")]
    [InlineData("ab")]
    [InlineData("")]
    public void Un_slug_invalido_se_rechaza(string slug)
    {
        var merchant = NewMerchant();

        Assert.Throws<ArgumentException>(() => merchant.PublishCatalog(slug));
        Assert.False(merchant.PublicCatalogEnabled);
        Assert.Null(merchant.PublicSlug);
    }

    [Theory]
    [InlineData(MerchantStatus.PendingBankAccount, true)]
    [InlineData(MerchantStatus.BankAccountInReview, true)]
    [InlineData(MerchantStatus.Active, true)]
    [InlineData(MerchantStatus.Suspended, false)]
    [InlineData(MerchantStatus.Disabled, false)]
    public void Publicado_en_Pro_se_muestra_salvo_suspendido_o_de_baja(MerchantStatus status, bool expected)
    {
        var merchant = NewMerchant(status);
        merchant.PublishCatalog("tacos");

        Assert.Equal(expected, merchant.CanShowPublicCatalog(PlanEntitlements.ForPlan(SubscriptionPlan.Pro)));
    }

    [Fact]
    public void En_Basico_no_se_muestra()
    {
        var merchant = NewMerchant();
        merchant.PublishCatalog("tacos");

        Assert.False(merchant.CanShowPublicCatalog(PlanEntitlements.ForPlan(SubscriptionPlan.Basic)));
    }

    [Fact]
    public void Sin_publicar_no_se_muestra()
    {
        var merchant = NewMerchant();
        merchant.PublishCatalog("tacos");
        merchant.UnpublishCatalog();

        Assert.False(merchant.CanShowPublicCatalog(PlanEntitlements.ForPlan(SubscriptionPlan.Pro)));
        Assert.False(NewMerchant().CanShowPublicCatalog(PlanEntitlements.ForPlan(SubscriptionPlan.Pro)));
    }
}
