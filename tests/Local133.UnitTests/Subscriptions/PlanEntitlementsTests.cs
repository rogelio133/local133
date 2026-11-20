using Local133.Domain.Subscriptions;

namespace Local133.UnitTests.Subscriptions;

/// <summary>Lo que permite cada estado del plan (§2).</summary>
public class PlanEntitlementsTests
{
    [Theory]
    [InlineData(PlanTier.Onboarding)]
    [InlineData(PlanTier.Trial)]
    [InlineData(PlanTier.Pro)]
    [InlineData(PlanTier.Grace)]
    public void Prueba_Pro_gracia_y_onboarding_tienen_todo_Pro(PlanTier tier)
    {
        var e = PlanEntitlements.For(tier);

        Assert.True(e.CanEditCatalog);
        Assert.True(e.CanChargeWithCart);
        Assert.True(e.CanShareReceipt);
        Assert.True(e.HasFullHistory);
        Assert.True(e.CanExportHistory);
        Assert.True(e.HasDailyCashCut);
        Assert.True(e.CanPublishCatalog);
        Assert.False(e.HasMonthlyChargeLimit);
    }

    [Fact]
    public void El_gratuito_tiene_limites()
    {
        var e = PlanEntitlements.For(PlanTier.Free);

        Assert.False(e.CanEditCatalog);
        Assert.False(e.CanChargeWithCart);
        Assert.False(e.CanShareReceipt);
        Assert.False(e.HasFullHistory);
        Assert.False(e.CanExportHistory);
        Assert.False(e.HasDailyCashCut);
        Assert.False(e.CanPublishCatalog);
        Assert.True(e.HasMonthlyChargeLimit);
    }

    [Fact]
    public void Estado_desconocido_se_rechaza() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanEntitlements.For((PlanTier)99));
}
