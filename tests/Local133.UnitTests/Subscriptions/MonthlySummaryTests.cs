using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;

namespace Local133.UnitTests.Subscriptions;

/// <summary>5.10: números del resumen mensual (§7).</summary>
public class MonthlySummaryTests
{
    [Fact]
    public void La_comision_de_terminal_es_3_5_por_ciento_mas_IVA_redondeada()
    {
        // 18,400 × 0.035 × 1.16 = 747.04 ⇒ $747 (el ejemplo del diseño).
        var summary = MonthlySummary.For(18_400m, 212, new SystemSettings(), isFounder: false);

        Assert.Equal(747m, summary.EstimatedTerminalFee);
        Assert.Equal(18_400m, summary.Collected);
        Assert.Equal(212, summary.SalesCount);
        Assert.Equal(0.035m, summary.FeeRate);
        Assert.Equal(199m, summary.ProMonthlyPrice);
    }

    [Fact]
    public void El_fundador_compara_con_su_precio()
    {
        var summary = MonthlySummary.For(1_000m, 3, new SystemSettings(), isFounder: true);

        Assert.Equal(149m, summary.ProMonthlyPrice);
    }

    [Fact]
    public void Usa_los_numeros_de_SystemSettings()
    {
        var settings = new SystemSettings { ReferenceTerminalFeeRate = 0.04m, VatRate = 0.16m, MonthlyPrice = 250m };

        var summary = MonthlySummary.For(1_000m, 1, settings, isFounder: false);

        Assert.Equal(46m, summary.EstimatedTerminalFee); // 1000 × 0.04 × 1.16 = 46.4
        Assert.Equal(250m, summary.ProMonthlyPrice);
    }

    [Fact]
    public void Montos_negativos_se_rechazan() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MonthlySummary.For(-1m, 0, new SystemSettings(), false));
}
