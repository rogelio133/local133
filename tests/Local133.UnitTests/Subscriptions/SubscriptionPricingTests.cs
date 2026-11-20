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
        var quote = SubscriptionPricing.Quote(package, Settings);

        Assert.Equal(new SubscriptionQuote(package, months, price, 0m, null, price), quote);
    }

    [Theory]
    [InlineData(SubscriptionPackage.OneMonth, 149)]
    [InlineData(SubscriptionPackage.ThreeMonths, 447)]
    [InlineData(SubscriptionPackage.SixMonths, 894)]
    [InlineData(SubscriptionPackage.Annual, 1490)]
    public void Precio_de_fundador(SubscriptionPackage package, decimal price) =>
        Assert.Equal(price, SubscriptionPricing.Quote(package, Settings, isFounder: true).Amount);

    [Fact]
    public void El_anual_congelado_se_respeta_si_el_precio_subio()
    {
        var raised = new SystemSettings { AnnualPrice = 2290m };

        Assert.Equal(1990m, SubscriptionPricing.Quote(SubscriptionPackage.Annual, raised, lockedAnnualPrice: 1990m).Amount);
    }

    [Fact]
    public void Si_el_precio_vigente_es_menor_que_el_congelado_se_cobra_el_vigente() =>
        Assert.Equal(1990m, SubscriptionPricing.Quote(SubscriptionPackage.Annual, Settings, lockedAnnualPrice: 2290m).Amount);

    [Fact]
    public void El_precio_congelado_solo_aplica_al_anual() =>
        Assert.Equal(199m, SubscriptionPricing.Quote(SubscriptionPackage.OneMonth, Settings, lockedAnnualPrice: 1500m).Amount);

    [Fact]
    public void Descuento_de_bienvenida_es_30_por_ciento_de_un_mes()
    {
        var quote = SubscriptionPricing.Quote(SubscriptionPackage.OneMonth, Settings, applyWelcomeDiscount: true);

        Assert.Equal(199m, quote.ListPrice);
        Assert.Equal(59.70m, quote.DiscountAmount);
        Assert.Equal(SubscriptionDiscountReason.ReferralWelcome, quote.DiscountReason);
        Assert.Equal(139.30m, quote.Amount);
    }

    [Fact]
    public void Descuento_de_bienvenida_sobre_el_mes_de_fundador_en_cualquier_paquete()
    {
        var quote = SubscriptionPricing.Quote(SubscriptionPackage.Annual, Settings, isFounder: true, applyWelcomeDiscount: true);

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
        Assert.Equal(199m, Settings.MonthlyPrice);
        Assert.Equal(1990m, Settings.AnnualPrice);
        Assert.Equal(30, Settings.TrialDays);
        Assert.Equal(5, Settings.GraceDays);
        Assert.Equal(100, Settings.FreeMonthlyPaidLimit);
        Assert.Equal(0.30m, Settings.ReferralWelcomeDiscountRate);
    }
}
