using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>5.7: banner de gracia en el portal y link de renovación en «Mi plan».</summary>
public class GraceBannerTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    private const string RenewalUrl = "https://pay.example.test/01RENOVACION57";

    [Fact]
    public async Task En_la_gracia_el_banner_lleva_al_link_de_renovacion_en_todo_el_portal_menos_en_Mi_plan()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: 1);
        await SeedPendingChargeAsync(Email, SubscriptionChargeOrigin.Renewal);

        foreach (var page in new[] { "/app", "/app/cobrar", "/app/ventas" })
        {
            var html = await client.GetStringAsync(page);
            Assert.Contains("data-grace-banner", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"{RenewalUrl}\"", html, StringComparison.Ordinal);
            Assert.Contains("Pagar ahora", html, StringComparison.Ordinal);
        }

        var plan = await client.GetStringAsync(PlanUrl);
        Assert.DoesNotContain("data-grace-banner", plan, StringComparison.Ordinal);
        Assert.Contains("data-renewal-link", plan, StringComparison.Ordinal);
        Assert.Contains("Tu link de renovación", plan, StringComparison.Ordinal);
        Assert.Contains($"href=\"{RenewalUrl}\"", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task En_la_gracia_sin_link_el_banner_lleva_a_Mi_plan()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: 1);

        var html = await client.GetStringAsync("/app");

        Assert.Contains("data-grace-banner", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/app/plan\" data-grace-pay", html, StringComparison.Ordinal);
        Assert.Contains("Ver mi plan", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Con_Pro_vigente_no_hay_banner_y_Mi_plan_muestra_el_pago_pendiente()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: -10); // vence en 10 días
        await SeedPendingChargeAsync(Email, SubscriptionChargeOrigin.Merchant);

        Assert.DoesNotContain("data-grace-banner", await client.GetStringAsync("/app"), StringComparison.Ordinal);
        var plan = await client.GetStringAsync(PlanUrl);
        Assert.Contains("data-renewal-link", plan, StringComparison.Ordinal);
        Assert.Contains("Tienes un pago pendiente", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task En_el_gratuito_no_hay_banner_ni_link_viejo()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: 10); // la gracia terminó hace 5 días
        await SeedPendingChargeAsync(Email, SubscriptionChargeOrigin.Renewal);

        Assert.DoesNotContain("data-grace-banner", await client.GetStringAsync("/app"), StringComparison.Ordinal);
        Assert.DoesNotContain("data-renewal-link", await client.GetStringAsync(PlanUrl), StringComparison.Ordinal);
    }

    /// <summary>Prueba y un mes pagado que terminó hace <paramref name="endedDaysAgo"/> días (negativo: aún no termina).</summary>
    private async Task SeedPaidAsync(string email, int endedDaysAgo)
    {
        var end = DateTimeOffset.UtcNow.AddDays(-endedDaysAgo);
        var start = end.AddDays(-30);
        await SeedPlanAsync(email,
            (SubscriptionPeriodKind.Trial, start.AddDays(-30), start),
            (SubscriptionPeriodKind.Paid, start, end));
        await WithDbAsync(async db =>
        {
            var merchant = await MerchantOfAsync(db, email);
            var subscription = await db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.MerchantId == merchant.Id);
            subscription.LastPackage = SubscriptionPackage.OneMonth;
            await db.SaveChangesAsync();
        });
    }

    private Task SeedPendingChargeAsync(string email, SubscriptionChargeOrigin origin) => WithDbAsync(async db =>
    {
        var merchant = await MerchantOfAsync(db, email);
        db.SubscriptionCharges.Add(new SubscriptionCharge
        {
            MerchantId = merchant.Id,
            Package = SubscriptionPackage.OneMonth,
            Months = 1,
            ListPrice = 199m,
            Amount = 199m,
            Origin = origin,
            Status = SubscriptionChargeStatus.Pending,
            PeyPaymentLinkId = "01RENOVACION57",
            Url = RenewalUrl,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });
        await db.SaveChangesAsync();
    });
}
