using Local133.Domain.Subscriptions;

namespace Local133.UnitTests.Subscriptions;

/// <summary>Plan y paquete en las URL del checkout (5.15).</summary>
public class SubscriptionSlugsTests
{
    [Theory]
    [InlineData(SubscriptionPlan.Basic, "basico")]
    [InlineData(SubscriptionPlan.Pro, "pro")]
    public void Plan_ida_y_vuelta(SubscriptionPlan plan, string slug)
    {
        Assert.Equal(slug, plan.Slug());
        Assert.True(SubscriptionSlugs.TryParsePlan(slug, out var parsed));
        Assert.Equal(plan, parsed);
    }

    [Theory]
    [InlineData(SubscriptionPackage.OneMonth, "1-mes")]
    [InlineData(SubscriptionPackage.ThreeMonths, "3-meses")]
    [InlineData(SubscriptionPackage.SixMonths, "6-meses")]
    [InlineData(SubscriptionPackage.Annual, "anual")]
    public void Paquete_ida_y_vuelta(SubscriptionPackage package, string slug)
    {
        Assert.Equal(slug, package.Slug());
        Assert.True(SubscriptionSlugs.TryParsePackage(slug, out var parsed));
        Assert.Equal(package, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Pro")]
    [InlineData("gratis")]
    [InlineData("Basic")]
    public void Un_plan_desconocido_no_existe(string? slug) => Assert.False(SubscriptionSlugs.TryParsePlan(slug, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("Annual")]
    [InlineData("ANUAL")]
    [InlineData("12-meses")]
    public void Un_paquete_desconocido_no_existe(string? slug) => Assert.False(SubscriptionSlugs.TryParsePackage(slug, out _));
}
