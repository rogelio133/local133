using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.7: banner de gracia en el portal y aviso de renovación en «Mi plan». Ninguno lleva a un link de Pey: el vendedor
/// elige su paquete en «Mi plan» y ahí se genera el link. Un link que generó y no pagó no se muestra.
/// </summary>
public class GraceBannerTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    private const string AbandonedUrl = "https://pay.example.test/01ABANDONADO57";

    [Fact]
    public async Task En_la_gracia_el_banner_lleva_a_Mi_plan_aunque_haya_un_link_pendiente()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: 1);
        await SeedPendingChargeAsync(Email);

        foreach (var page in new[] { "/app", "/app/cobrar", "/app/ventas" })
        {
            var html = await client.GetStringAsync(page);
            Assert.Contains("data-grace-banner", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/app/plan\" data-grace-pay", html, StringComparison.Ordinal);
            Assert.Contains("Renovar ahora", html, StringComparison.Ordinal);
            Assert.DoesNotContain(AbandonedUrl, html, StringComparison.Ordinal);
        }

        var plan = await client.GetStringAsync(PlanUrl);
        Assert.DoesNotContain("data-grace-banner", plan, StringComparison.Ordinal);
        Assert.Contains("data-renewal-notice", plan, StringComparison.Ordinal);
        Assert.Contains("Renueva antes del", plan, StringComparison.Ordinal);
        Assert.Contains("data-plan-offer-current", plan, StringComparison.Ordinal);
        Assert.DoesNotContain(AbandonedUrl, plan, StringComparison.Ordinal);
        Assert.DoesNotContain("pago pendiente", plan, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_dos_dias_de_vencer_Mi_plan_avisa_que_toca_renovar_sin_banner()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: -2);

        Assert.DoesNotContain("data-grace-banner", await client.GetStringAsync("/app"), StringComparison.Ordinal);
        var plan = await client.GetStringAsync(PlanUrl);
        Assert.Contains("data-renewal-notice", plan, StringComparison.Ordinal);
        Assert.Contains("Tu plan Pro vence", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Con_Pro_lejos_de_vencer_no_hay_banner_ni_aviso_ni_link_pendiente()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: -10);
        await SeedPendingChargeAsync(Email);

        Assert.DoesNotContain("data-grace-banner", await client.GetStringAsync("/app"), StringComparison.Ordinal);
        var plan = await client.GetStringAsync(PlanUrl);
        Assert.DoesNotContain("data-renewal-notice", plan, StringComparison.Ordinal);
        Assert.DoesNotContain(AbandonedUrl, plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task En_el_gratuito_no_hay_banner_ni_aviso_de_renovar()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: 10); // la gracia terminó hace 5 días
        await SeedPendingChargeAsync(Email);

        Assert.DoesNotContain("data-grace-banner", await client.GetStringAsync("/app"), StringComparison.Ordinal);
        var plan = await client.GetStringAsync(PlanUrl);
        Assert.DoesNotContain("data-renewal-notice", plan, StringComparison.Ordinal);
        Assert.DoesNotContain(AbandonedUrl, plan, StringComparison.Ordinal);
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

    /// <summary>Link que el vendedor generó en «Mi plan» y no pagó.</summary>
    private Task SeedPendingChargeAsync(string email) => WithDbAsync(async db =>
    {
        var merchant = await MerchantOfAsync(db, email);
        db.SubscriptionCharges.Add(new SubscriptionCharge
        {
            MerchantId = merchant.Id,
            Package = SubscriptionPackage.OneMonth,
            Months = 1,
            ListPrice = 199m,
            Amount = 199m,
            Origin = SubscriptionChargeOrigin.Merchant,
            Status = SubscriptionChargeStatus.Pending,
            PeyPaymentLinkId = "01ABANDONADO57",
            Url = AbandonedUrl,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });
        await db.SaveChangesAsync();
    });
}
