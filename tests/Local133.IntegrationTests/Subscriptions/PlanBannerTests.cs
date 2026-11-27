using System.Net;
using Local133.Domain.Merchants;
using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.7 y 5.14: banner de gracia y de vencido en el portal, y aviso de renovación en «Mi plan». Ninguno lleva a un link
/// de Pey: el vendedor elige su paquete en «Mi plan» y ahí se genera el link. Un link que generó y no pagó no se muestra.
/// </summary>
public class PlanBannerTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    private const string AbandonedUrl = "https://pay.example.test/01ABANDONADO57";

    [Fact]
    public async Task En_la_gracia_el_banner_lleva_al_resumen_de_su_ultimo_pedido_aunque_haya_un_link_pendiente()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: 1);
        await SeedPendingChargeAsync(Email);

        foreach (var page in new[] { "/app", "/app/cobrar", "/app/ventas" })
        {
            var html = await client.GetStringAsync(page);
            Assert.Contains("data-plan-banner=\"grace\"", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/app/plan/checkout/pro/1-mes\" data-plan-banner-action", html, StringComparison.Ordinal);
            Assert.Contains("Renovar ahora", html, StringComparison.Ordinal);
            Assert.DoesNotContain(AbandonedUrl, html, StringComparison.Ordinal);
        }

        var plan = await client.GetStringAsync(PlanUrl);
        Assert.DoesNotContain("data-plan-banner", plan, StringComparison.Ordinal);
        Assert.Contains("data-renewal-notice", plan, StringComparison.Ordinal);
        Assert.Contains("Renueva antes del", plan, StringComparison.Ordinal);
        // En la gracia: «Renovar mi plan Pro» a la sección de Pro del checkout, y el aviso al mismo lugar.
        Assert.Contains("href=\"/app/plan/checkout#pro\" data-plan-action=\"renew\"", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("data-renewal-choose", plan, StringComparison.Ordinal); // el aviso ya no trae su propio botón
        Assert.Contains("Renovar mi plan Pro", WebUtility.HtmlDecode(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("data-plan-action=\"choose\"", plan, StringComparison.Ordinal);
        Assert.DoesNotContain(AbandonedUrl, plan, StringComparison.Ordinal);
        Assert.DoesNotContain("pago pendiente", plan, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_dos_dias_de_vencer_Mi_plan_avisa_que_toca_renovar_sin_banner()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: -2);

        Assert.DoesNotContain("data-plan-banner", await client.GetStringAsync("/app"), StringComparison.Ordinal);
        var plan = await client.GetStringAsync(PlanUrl);
        Assert.Contains("data-renewal-notice", plan, StringComparison.Ordinal);
        Assert.Contains("Tu plan Pro vence", plan, StringComparison.Ordinal);
        Assert.Contains("href=\"/app/plan/checkout#pro\" data-plan-action=\"renew\"", plan, StringComparison.Ordinal);
        Assert.Contains("Renovar mi plan Pro", WebUtility.HtmlDecode(plan), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Basico_a_dos_dias_de_vencer_dice_Basico_y_Renovar_mi_plan_Basico()
    {
        var client = await ActiveMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        await SeedPlanAsync(Email,
            (SubscriptionPeriodKind.Trial, SubscriptionPlan.Pro, now.AddDays(-60), now.AddDays(-30)),
            (SubscriptionPeriodKind.Paid, SubscriptionPlan.Basic, now.AddDays(-28), now.AddDays(2)));

        var plan = WebUtility.HtmlDecode(await client.GetStringAsync(PlanUrl));

        Assert.Contains("data-renewal-notice", plan, StringComparison.Ordinal);
        Assert.Contains("Tu plan Básico vence", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("Tu plan Pro vence", plan, StringComparison.Ordinal);
        Assert.Contains("href=\"/app/plan/checkout#basico\" data-plan-action=\"renew\"", plan, StringComparison.Ordinal);
        Assert.Contains("Renovar mi plan Básico", plan, StringComparison.Ordinal);
        Assert.Contains("href=\"/app/plan/checkout#pro\" data-plan-action=\"upgrade\"", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Con_Pro_lejos_de_vencer_no_hay_banner_ni_aviso_ni_link_pendiente()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: -10);
        await SeedPendingChargeAsync(Email);

        Assert.DoesNotContain("data-plan-banner", await client.GetStringAsync("/app"), StringComparison.Ordinal);
        var plan = await client.GetStringAsync(PlanUrl);
        Assert.DoesNotContain("data-renewal-notice", plan, StringComparison.Ordinal);
        Assert.DoesNotContain(AbandonedUrl, plan, StringComparison.Ordinal);
        // Fuera de la ventana de renovar: «Adquirir más días de Pro», sin «Cambiar a Pro» ni «Elegir plan».
        Assert.Contains("href=\"/app/plan/checkout#pro\" data-plan-action=\"extend\"", plan, StringComparison.Ordinal);
        Assert.Contains("Adquirir más días de Pro", WebUtility.HtmlDecode(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("data-plan-action=\"upgrade\"", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("data-plan-action=\"choose\"", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vencido_el_banner_pide_elegir_plan_salvo_en_Inicio_Cobrar_y_Mi_plan()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedPaidAsync(Email, endedDaysAgo: 10); // la gracia terminó hace 5 días
        await SeedPendingChargeAsync(Email);

        foreach (var page in new[] { "/app/ventas", "/app/productos", "/app/mas" })
        {
            var html = WebUtility.HtmlDecode(await client.GetStringAsync(page));
            Assert.Contains("data-plan-banner=\"expired\"", html, StringComparison.Ordinal);
            Assert.Contains("Tu plan venció", html, StringComparison.Ordinal);
            // Ya pagó antes: «Elegir plan» lleva al resumen de su último pedido (5.15).
            Assert.Contains("href=\"/app/plan/checkout/pro/1-mes\" data-plan-banner-action", html, StringComparison.Ordinal);
            Assert.DoesNotContain(AbandonedUrl, html, StringComparison.Ordinal);
        }

        foreach (var page in new[] { "/app", "/app/cobrar", "/app/cobrar/productos", PlanUrl })
        {
            Assert.DoesNotContain("data-plan-banner", await client.GetStringAsync(page), StringComparison.Ordinal);
        }

        var plan = await client.GetStringAsync(PlanUrl);
        Assert.DoesNotContain("data-renewal-notice", plan, StringComparison.Ordinal);
        Assert.DoesNotContain(AbandonedUrl, plan, StringComparison.Ordinal);
        // Vencido: «Renovar mi plan Pro» (su último plan) a la sección de Pro.
        Assert.Contains("href=\"/app/plan/checkout#pro\" data-plan-action=\"renew\"", plan, StringComparison.Ordinal);
        Assert.Contains("Renovar mi plan Pro", WebUtility.HtmlDecode(plan), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dado_de_baja_y_vencido_no_hay_banner()
    {
        var client = await ActiveMerchantAsync(Email, status: MerchantStatus.Disabled);
        await SeedPaidAsync(Email, endedDaysAgo: 10);

        Assert.DoesNotContain("data-plan-banner", await client.GetStringAsync("/app/ventas"), StringComparison.Ordinal);
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
