using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;

namespace Local133.UnitTests.Subscriptions;

/// <summary>5.10: números del resumen mensual (§7); por plan desde la 5.16.</summary>
public class MonthlySummaryTests
{
    [Fact]
    public void La_comision_de_terminal_es_3_5_por_ciento_mas_IVA_redondeada()
    {
        // 18,400 × 0.035 × 1.16 = 747.04 ⇒ $747 (el ejemplo del diseño).
        var summary = MonthlySummary.For(18_400m, 212, new SystemSettings(), isFounder: false, SubscriptionPlan.Pro, isExpired: false);

        Assert.Equal(747m, summary.EstimatedTerminalFee);
        Assert.Equal(18_400m, summary.Collected);
        Assert.Equal(212, summary.SalesCount);
        Assert.Equal(0.035m, summary.FeeRate);
        Assert.Equal(SubscriptionPlan.Pro, summary.Plan);
        Assert.Equal(199m, summary.PlanMonthlyPrice);
        Assert.False(summary.IsExpired);
    }

    [Theory]
    [InlineData(SubscriptionPlan.Basic, false, 99)]
    [InlineData(SubscriptionPlan.Basic, true, 69)]
    [InlineData(SubscriptionPlan.Pro, false, 199)]
    [InlineData(SubscriptionPlan.Pro, true, 149)]
    public void Compara_con_el_precio_de_su_plan_y_de_fundador_si_lo_es(SubscriptionPlan plan, bool founder, int price)
    {
        var summary = MonthlySummary.For(1_000m, 3, new SystemSettings(), founder, plan, isExpired: false);

        Assert.Equal(plan, summary.Plan);
        Assert.Equal(price, summary.PlanMonthlyPrice);
    }

    [Theory]
    [InlineData(false, 99)]
    [InlineData(true, 69)]
    public void El_vencido_compara_con_Basico(bool founder, int price)
    {
        var summary = MonthlySummary.For(1_000m, 3, new SystemSettings(), founder, currentPlan: null, isExpired: true);

        Assert.True(summary.IsExpired);
        Assert.Equal(SubscriptionPlan.Basic, summary.Plan);
        Assert.Equal(price, summary.PlanMonthlyPrice);
    }

    [Fact]
    public void Sin_plan_y_sin_vencer_es_el_onboarding_y_compara_con_Pro()
    {
        var summary = MonthlySummary.For(1_000m, 1, new SystemSettings(), isFounder: false, currentPlan: null, isExpired: false);

        Assert.Equal(SubscriptionPlan.Pro, summary.Plan);
        Assert.Equal(199m, summary.PlanMonthlyPrice);
    }

    [Fact]
    public void Usa_los_numeros_de_SystemSettings()
    {
        var settings = new SystemSettings { ReferenceTerminalFeeRate = 0.04m, VatRate = 0.16m, BasicMonthlyPrice = 120m };

        var summary = MonthlySummary.For(1_000m, 1, settings, isFounder: false, SubscriptionPlan.Basic, isExpired: false);

        Assert.Equal(46m, summary.EstimatedTerminalFee); // 1000 × 0.04 × 1.16 = 46.4
        Assert.Equal(120m, summary.PlanMonthlyPrice);
    }

    [Fact]
    public void Montos_negativos_se_rechazan() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MonthlySummary.For(-1m, 0, new SystemSettings(), false, SubscriptionPlan.Pro, false));
}
