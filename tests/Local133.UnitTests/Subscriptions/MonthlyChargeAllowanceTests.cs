using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;

namespace Local133.UnitTests.Subscriptions;

/// <summary>Límite mensual del gratuito (§5): 100 concretados (sin cortesía), aviso al 80 % y tope de 300 generados.</summary>
public class MonthlyChargeAllowanceTests
{
    private static readonly SystemSettings Defaults = new();

    [Theory]
    [InlineData(0, ChargeAllowanceState.Normal, 100)]
    [InlineData(79, ChargeAllowanceState.Normal, 21)]
    [InlineData(80, ChargeAllowanceState.NearLimit, 20)]
    [InlineData(99, ChargeAllowanceState.NearLimit, 1)]
    [InlineData(100, ChargeAllowanceState.Exhausted, 0)]
    [InlineData(130, ChargeAllowanceState.Exhausted, 0)]
    public void Estado_por_cobros_concretados(int paid, ChargeAllowanceState expected, int remaining)
    {
        var allowance = MonthlyChargeAllowance.For(paid, paid, Defaults);

        Assert.Equal(expected, allowance.State);
        Assert.Equal(remaining, allowance.Remaining);
        Assert.Equal(expected != ChargeAllowanceState.Exhausted, allowance.CanGenerate);
    }

    [Fact]
    public void Valores_por_defecto()
    {
        var allowance = MonthlyChargeAllowance.For(0, 0, Defaults);

        Assert.Equal(100, allowance.PaidLimit);
        Assert.Equal(80, allowance.WarningThreshold);
        Assert.Equal(300, allowance.GeneratedLimit);
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, false)]
    public void El_tope_de_generados_bloquea_aunque_no_se_paguen(int generated, bool canGenerate)
    {
        var allowance = MonthlyChargeAllowance.For(10, generated, Defaults);

        Assert.Equal(canGenerate, allowance.CanGenerate);
        Assert.Equal(canGenerate ? ChargeAllowanceState.Normal : ChargeAllowanceState.GenerationCapReached, allowance.State);
    }

    [Fact]
    public void Llegar_al_limite_manda_sobre_el_tope_de_generados()
    {
        Assert.Equal(ChargeAllowanceState.Exhausted, MonthlyChargeAllowance.For(100, 300, Defaults).State);
    }

    [Fact]
    public void Usa_los_ajustes_del_sistema()
    {
        var settings = new SystemSettings { FreeMonthlyPaidLimit = 10, FreeMonthlyGeneratedLimit = 20 };

        Assert.Equal(8, MonthlyChargeAllowance.For(0, 0, settings).WarningThreshold);
        Assert.Equal(ChargeAllowanceState.Exhausted, MonthlyChargeAllowance.For(10, 10, settings).State);
        Assert.Equal(ChargeAllowanceState.NearLimit, MonthlyChargeAllowance.For(9, 9, settings).State);
        Assert.Equal(ChargeAllowanceState.GenerationCapReached, MonthlyChargeAllowance.For(1, 20, settings).State);
    }

    [Fact]
    public void El_aviso_se_redondea_hacia_arriba()
    {
        var settings = new SystemSettings { FreeMonthlyPaidLimit = 7 };

        Assert.Equal(6, MonthlyChargeAllowance.For(0, 0, settings).WarningThreshold); // 5.6 ⇒ 6
    }

    [Theory]
    [InlineData(79, 80, new[] { 80 })]
    [InlineData(99, 100, new[] { 100 })]
    [InlineData(78, 101, new[] { 80, 100 })]
    [InlineData(80, 81, new int[0])]
    [InlineData(100, 101, new int[0])]
    public void Umbrales_cruzados(int previous, int current, int[] expected)
    {
        Assert.Equal(expected, MonthlyChargeAllowance.ThresholdsCrossed(previous, current, Defaults));
    }

    [Fact]
    public void Si_el_aviso_y_el_limite_coinciden_son_un_solo_umbral()
    {
        var settings = new SystemSettings { FreeMonthlyPaidLimit = 1 }; // 80 % de 1 ⇒ 1

        Assert.Equal([1], MonthlyChargeAllowance.ThresholdsCrossed(0, 1, settings));
    }

    [Theory]
    [InlineData(4999, 5000, true)]
    [InlineData(5000, 5001, false)]
    [InlineData(4998, 4999, false)]
    public void Uso_justo_de_Pro(int previous, int current, bool crossed)
    {
        Assert.Equal(crossed, MonthlyChargeAllowance.ProFairUseCrossed(previous, current, Defaults));
    }

    [Fact]
    public void Rechaza_conteos_negativos()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MonthlyChargeAllowance.For(-1, 0, Defaults));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonthlyChargeAllowance.For(0, -1, Defaults));
    }
}
