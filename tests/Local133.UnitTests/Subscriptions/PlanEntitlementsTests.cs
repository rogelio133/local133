using Local133.Domain.Subscriptions;

namespace Local133.UnitTests.Subscriptions;

/// <summary>Lo que permite cada estado del plan (§2 del diseño de planes Básico y Pro, 5.12).</summary>
public class PlanEntitlementsTests
{
    private static readonly DateTimeOffset Until = new(2026, 12, 20, 16, 0, 0, TimeSpan.Zero);

    private static PlanStatus Status(PlanTier tier, SubscriptionPlan? plan) => new(tier, Until, null) { Plan = plan };

    public static TheoryData<PlanTier, SubscriptionPlan?> ProStates => new()
    {
        { PlanTier.Trial, SubscriptionPlan.Pro },
        { PlanTier.Pro, SubscriptionPlan.Pro },
        { PlanTier.Grace, SubscriptionPlan.Pro },
    };

    [Theory]
    [MemberData(nameof(ProStates))]
    public void Prueba_Pro_y_gracia_de_Pro_tienen_todo(PlanTier tier, SubscriptionPlan? plan)
    {
        var e = PlanEntitlements.For(Status(tier, plan));

        Assert.True(e.CanCharge);
        Assert.True(e.CanEditCatalog);
        Assert.True(e.CanChargeWithCart);
        Assert.True(e.CanExportHistory);
        Assert.True(e.HasDailyCashCut);
        Assert.True(e.CanPublishCatalog);
        Assert.True(e.HasBrandedReceipt);
        AssertWithoutFreeLimits(e);
    }

    [Fact]
    public void En_el_onboarding_tiene_todo_Pro_salvo_publicar_el_catalogo()
    {
        var e = PlanEntitlements.For(PlanStatus.Onboarding);

        Assert.True(e.CanCharge);
        Assert.True(e.CanEditCatalog);
        Assert.True(e.CanChargeWithCart);
        Assert.True(e.CanExportHistory);
        Assert.True(e.HasDailyCashCut);
        Assert.False(e.CanPublishCatalog);
        Assert.True(e.HasBrandedReceipt);
        AssertWithoutFreeLimits(e);
    }

    [Theory]
    [InlineData(PlanTier.Basic)]
    [InlineData(PlanTier.Grace)]
    public void Basico_y_su_gracia_cobran_con_corte_pero_sin_lo_de_Pro(PlanTier tier)
    {
        var e = PlanEntitlements.For(Status(tier, SubscriptionPlan.Basic));

        Assert.True(e.CanCharge);
        Assert.True(e.HasDailyCashCut);
        Assert.False(e.CanEditCatalog);
        Assert.False(e.CanChargeWithCart);
        Assert.False(e.CanExportHistory);
        Assert.False(e.CanPublishCatalog);
        Assert.False(e.HasBrandedReceipt);
        AssertWithoutFreeLimits(e);
    }

    [Fact]
    public void El_vencido_no_cobra_ni_ve_el_corte()
    {
        var e = PlanEntitlements.For(Status(PlanTier.Expired, null));

        Assert.False(e.CanCharge);
        Assert.False(e.HasDailyCashCut);
        Assert.False(e.CanEditCatalog);
        Assert.False(e.CanChargeWithCart);
        Assert.False(e.CanExportHistory);
        Assert.False(e.CanPublishCatalog);
        Assert.False(e.HasBrandedReceipt);
        // Hasta la 5.14 conserva los límites del gratuito, que es lo que hoy se aplica en el servidor.
        Assert.False(e.CanShareReceipt);
        Assert.False(e.HasFullHistory);
        Assert.True(e.HasMonthlyChargeLimit);
    }

    [Fact]
    public void La_gracia_sin_plan_se_rechaza() =>
        Assert.Throws<ArgumentException>(() => PlanEntitlements.For(Status(PlanTier.Grace, null)));

    [Fact]
    public void Estado_desconocido_se_rechaza() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanEntitlements.For(Status((PlanTier)99, null)));

    /// <summary>Fuera del vencido, las banderas del gratuito no limitan nada (la 5.14 las retira).</summary>
    private static void AssertWithoutFreeLimits(PlanEntitlements e)
    {
        Assert.True(e.CanShareReceipt);
        Assert.True(e.HasFullHistory);
        Assert.False(e.HasMonthlyChargeLimit);
    }
}
