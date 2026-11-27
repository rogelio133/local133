using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using static Local133.UnitTests.Subscriptions.PlanStatusTests;

namespace Local133.UnitTests.Subscriptions;

/// <summary>
/// Resumen del pedido del checkout (5.15, §6 del diseño de planes Básico y Pro): precio de lista, fundador, congelado,
/// bienvenida, IVA y qué pasa con lo que ya tiene el vendedor.
/// </summary>
public class SubscriptionOrderTests
{
    private const int GraceDays = 5;

    private static readonly SystemSettings Settings = new();

    private static readonly DateTimeOffset Now = Mx(2026, 11, 30, 10);

    private static DateTimeOffset MxSeconds(int year, int month, int day, int hour, int minute, int second) =>
        new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(-6)).ToUniversalTime();

    /// <summary>Prueba vencida en octubre y Básico pagado del 20 de noviembre al 20 de diciembre (10:00).</summary>
    private static Subscription WithBasic()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 10, 1, 10), trialDays: 15);
        subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Mx(2026, 11, 20, 10), GraceDays, 1);
        return subscription;
    }

    [Fact]
    public void Subir_a_Pro_con_20_dias_de_Basico_dice_unos_9_dias_y_el_anual_despues()
    {
        var order = SubscriptionOrder.For(WithBasic(), SubscriptionPlan.Pro, SubscriptionPackage.Annual, Settings, Now,
            founderSlotAvailable: false);

        Assert.Equal(SubscriptionOrderTimeline.ConvertsBasic, order.Timeline);
        Assert.Equal(20, order.BasicDays);
        Assert.Equal(9, order.ProDays); // 20 × 99/199 = 9.95: «unos 9 días»
        Assert.True(order.ConvertsFromNow);
        Assert.Equal(MxSeconds(2026, 12, 10, 8, 47, 38), order.Offer.Starts); // el mismo cálculo que el abono
        Assert.Equal(1990m, order.RegularPrice);
        Assert.Equal(0m, order.PriceAdjustment);
        Assert.Equal(1990m, order.Total);
        Assert.Equal(274.48m, order.Vat);
    }

    [Fact]
    public void De_fundador_muestra_el_ajuste_y_el_IVA_del_diseno()
    {
        var order = SubscriptionOrder.For(WithBasic(), SubscriptionPlan.Pro, SubscriptionPackage.Annual, Settings, Now,
            founderSlotAvailable: true);

        Assert.True(order.IsFounderPrice);
        Assert.Equal(1990m, order.RegularPrice);
        Assert.Equal(500m, order.PriceAdjustment);
        Assert.Equal(1490m, order.Total);
        Assert.Equal(205.52m, order.Vat); // «IVA incluido: $205.52» (§6)
        Assert.Equal(9, order.ProDays); // 20 × 69/149 = 9.26
    }

    [Fact]
    public void Basico_en_la_prueba_empieza_cuando_termina_la_prueba()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 25, 10), trialDays: 15);

        var order = SubscriptionOrder.For(subscription, SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Settings, Now, false);

        Assert.Equal(SubscriptionOrderTimeline.AfterCurrent, order.Timeline);
        Assert.True(order.PrecedingIsTrial);
        Assert.Equal(Mx(2026, 12, 10, 10), order.Offer.Starts);
        Assert.Equal(99m, order.Total);
    }

    [Fact]
    public void Basico_con_Pro_pagado_empieza_cuando_termina_Pro()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 10, 1, 10), trialDays: 15);
        subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Mx(2026, 11, 20, 10), GraceDays, 1);

        var order = SubscriptionOrder.For(subscription, SubscriptionPlan.Basic, SubscriptionPackage.Annual, Settings, Now, false);

        Assert.Equal(SubscriptionOrderTimeline.AfterCurrent, order.Timeline);
        Assert.False(order.PrecedingIsTrial);
        Assert.Equal(SubscriptionPlan.Pro, order.PrecedingPlan);
        Assert.Equal(Mx(2026, 12, 20, 10), order.Offer.Starts);
        Assert.Equal(990m, order.Total);
    }

    [Theory]
    [InlineData(SubscriptionPackage.ThreeMonths)]
    [InlineData(SubscriptionPackage.SixMonths)]
    public void Los_paquetes_de_3_y_6_meses_ya_no_se_ofrecen(SubscriptionPackage package)
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 10, 1, 10), trialDays: 15);

        Assert.False(package.IsOffered());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SubscriptionOrder.For(subscription, SubscriptionPlan.Pro, package, Settings, Now, false));
    }

    [Fact]
    public void Vencido_empieza_hoy()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 10, 1, 10), trialDays: 15);

        var order = SubscriptionOrder.For(subscription, SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings, Now, false);

        Assert.Equal(SubscriptionOrderTimeline.StartsNow, order.Timeline);
        Assert.Equal(Now, order.Offer.Starts);
        Assert.Equal(Mx(2026, 12, 30, 10), order.Offer.Until);
    }

    [Fact]
    public void En_la_gracia_de_Basico_no_hay_conversion_y_cuenta_desde_que_vencio()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 10, 1, 10), trialDays: 15);
        subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Mx(2026, 10, 28, 10), GraceDays, 1);

        var order = SubscriptionOrder.For(subscription, SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings, Now, false);

        Assert.Equal(SubscriptionOrderTimeline.FromGrace, order.Timeline);
        Assert.Equal(Mx(2026, 11, 28, 10), order.Offer.Starts);
        Assert.Equal(0, order.ProDays);
    }

    [Fact]
    public void El_descuento_de_bienvenida_es_del_plan_elegido()
    {
        var basic = SubscriptionOrder.For(WithBasic(), SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Settings, Now, false,
            welcomeDiscount: true);
        var pro = SubscriptionOrder.For(WithBasic(), SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings, Now, false,
            welcomeDiscount: true);

        Assert.Equal(29.70m, basic.WelcomeDiscount);
        Assert.Equal(69.30m, basic.Total);
        Assert.Equal(59.70m, pro.WelcomeDiscount);
        Assert.Equal(139.30m, pro.Total);
        Assert.Equal(199m, pro.RegularPrice);
    }

    [Fact]
    public void El_anual_congelado_baja_el_precio_y_no_es_de_fundador()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 1, 1, 10), trialDays: 15);
        subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.Annual, Mx(2026, 1, 16, 10), GraceDays, 1);
        subscription.LockAnnualPrice(SubscriptionPlan.Pro, 1790m);

        var order = SubscriptionOrder.For(subscription, SubscriptionPlan.Pro, SubscriptionPackage.Annual, Settings, Now, false);

        Assert.False(order.IsFounderPrice);
        Assert.Equal(1990m, order.RegularPrice);
        Assert.Equal(200m, order.PriceAdjustment);
        Assert.Equal(1790m, order.Total);
        Assert.Equal(SubscriptionOrderTimeline.AfterCurrent, order.Timeline);
    }
}
