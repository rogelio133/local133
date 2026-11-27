using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;

namespace Local133.UnitTests.Subscriptions;

/// <summary>Precios de la suscripción con IVA incluido (§2 y §6).</summary>
public class SubscriptionPricingTests
{
    private static readonly SystemSettings Settings = new();

    [Theory]
    [InlineData(SubscriptionPackage.OneMonth, 1, 199)]
    [InlineData(SubscriptionPackage.ThreeMonths, 3, 597)]
    [InlineData(SubscriptionPackage.SixMonths, 6, 1194)]
    [InlineData(SubscriptionPackage.Annual, 12, 1990)]
    public void Precio_normal_por_paquete(SubscriptionPackage package, int months, decimal price)
    {
        var quote = SubscriptionPricing.Quote(SubscriptionPlan.Pro, package, Settings);

        Assert.Equal(new SubscriptionQuote(SubscriptionPlan.Pro, package, months, price, 0m, null, price), quote);
    }

    [Theory]
    [InlineData(SubscriptionPackage.OneMonth, 149)]
    [InlineData(SubscriptionPackage.ThreeMonths, 447)]
    [InlineData(SubscriptionPackage.SixMonths, 894)]
    [InlineData(SubscriptionPackage.Annual, 1490)]
    public void Precio_de_fundador(SubscriptionPackage package, decimal price) =>
        Assert.Equal(price, SubscriptionPricing.Quote(SubscriptionPlan.Pro, package, Settings, isFounder: true).Amount);

    [Fact]
    public void El_anual_congelado_se_respeta_si_el_precio_subio()
    {
        var raised = new SystemSettings { ProAnnualPrice = 2290m };

        Assert.Equal(1990m, SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.Annual, raised, lockedAnnualPrice: 1990m).Amount);
    }

    [Fact]
    public void Si_el_precio_vigente_es_menor_que_el_congelado_se_cobra_el_vigente() =>
        Assert.Equal(1990m, SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.Annual, Settings, lockedAnnualPrice: 2290m).Amount);

    [Fact]
    public void El_precio_congelado_solo_aplica_al_anual() =>
        Assert.Equal(199m, SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings, lockedAnnualPrice: 1500m).Amount);

    [Fact]
    public void Descuento_de_bienvenida_es_30_por_ciento_de_un_mes()
    {
        var quote = SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings, applyWelcomeDiscount: true);

        Assert.Equal(199m, quote.ListPrice);
        Assert.Equal(59.70m, quote.DiscountAmount);
        Assert.Equal(SubscriptionDiscountReason.ReferralWelcome, quote.DiscountReason);
        Assert.Equal(139.30m, quote.Amount);
    }

    [Fact]
    public void Descuento_de_bienvenida_sobre_el_mes_de_fundador_en_cualquier_paquete()
    {
        var quote = SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.Annual, Settings, isFounder: true, applyWelcomeDiscount: true);

        Assert.Equal(1490m, quote.ListPrice);
        Assert.Equal(44.70m, quote.DiscountAmount);
        Assert.Equal(1445.30m, quote.Amount);
    }

    [Fact]
    public void Desglose_del_IVA_incluido()
    {
        var (@base, vat) = SubscriptionPricing.VatBreakdown(199m, SystemSettings.DefaultVatRate);

        Assert.Equal(171.55m, @base);
        Assert.Equal(27.45m, vat);
    }

    [Fact]
    public void Los_valores_por_defecto_son_los_del_diseno()
    {
        Assert.Equal(99m, Settings.BasicMonthlyPrice);
        Assert.Equal(990m, Settings.BasicAnnualPrice);
        Assert.Equal(69m, Settings.BasicFounderMonthlyPrice);
        Assert.Equal(690m, Settings.BasicFounderAnnualPrice);
        Assert.Equal(199m, Settings.ProMonthlyPrice);
        Assert.Equal(1990m, Settings.ProAnnualPrice);
        Assert.Equal(149m, Settings.ProFounderMonthlyPrice);
        Assert.Equal(1490m, Settings.ProFounderAnnualPrice);
        Assert.Equal(15, Settings.TrialDays);
        Assert.Equal(5, Settings.GraceDays);
        Assert.Equal(5000, Settings.FairUseMonthlyPaid);
        Assert.Equal(0.30m, Settings.ReferralWelcomeDiscountRate);
    }

    /// <summary>La tabla de precios de §2 (5.12): plan × paquete, normal y de fundador.</summary>
    [Theory]
    [InlineData(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, 99, 69)]
    [InlineData(SubscriptionPlan.Basic, SubscriptionPackage.ThreeMonths, 297, 207)]
    [InlineData(SubscriptionPlan.Basic, SubscriptionPackage.SixMonths, 594, 414)]
    [InlineData(SubscriptionPlan.Basic, SubscriptionPackage.Annual, 990, 690)]
    [InlineData(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, 199, 149)]
    [InlineData(SubscriptionPlan.Pro, SubscriptionPackage.ThreeMonths, 597, 447)]
    [InlineData(SubscriptionPlan.Pro, SubscriptionPackage.SixMonths, 1194, 894)]
    [InlineData(SubscriptionPlan.Pro, SubscriptionPackage.Annual, 1990, 1490)]
    public void Precios_por_plan_y_paquete(SubscriptionPlan plan, SubscriptionPackage package, decimal normal, decimal founder)
    {
        var quote = SubscriptionPricing.Quote(plan, package, Settings);

        Assert.Equal(plan, quote.Plan);
        Assert.Equal(normal, quote.Amount);
        Assert.Equal(founder, SubscriptionPricing.Quote(plan, package, Settings, isFounder: true).Amount);
    }

    [Theory]
    [InlineData(SubscriptionPlan.Basic, false, 29.70)]
    [InlineData(SubscriptionPlan.Basic, true, 20.70)]
    [InlineData(SubscriptionPlan.Pro, false, 59.70)]
    [InlineData(SubscriptionPlan.Pro, true, 44.70)]
    public void Descuento_de_bienvenida_sobre_el_mes_del_plan_elegido(SubscriptionPlan plan, bool founder, decimal discount)
    {
        var quote = SubscriptionPricing.Quote(plan, SubscriptionPackage.SixMonths, Settings, founder, applyWelcomeDiscount: true);

        Assert.Equal(discount, quote.DiscountAmount);
        Assert.Equal(quote.ListPrice - discount, quote.Amount);
    }

    [Theory]
    [InlineData(99, 85.34, 13.66)]
    [InlineData(69, 59.48, 9.52)]
    public void Desglose_del_IVA_de_Basico(decimal amount, decimal expectedBase, decimal expectedVat)
    {
        var (@base, vat) = SubscriptionPricing.VatBreakdown(amount, SystemSettings.DefaultVatRate);

        Assert.Equal(expectedBase, @base);
        Assert.Equal(expectedVat, vat);
    }

    [Fact]
    public void La_conversion_usa_el_precio_mensual_que_le_toca()
    {
        Assert.Equal(99m / 199m, SubscriptionPricing.ConversionRatio(Settings, isFounder: false));
        Assert.Equal(69m / 149m, SubscriptionPricing.ConversionRatio(Settings, isFounder: true));
    }

    [Fact]
    public void La_conversion_nunca_pasa_de_uno() =>
        Assert.Equal(1m, SubscriptionPricing.ConversionRatio(new SystemSettings { BasicMonthlyPrice = 250m }, isFounder: false));
}
