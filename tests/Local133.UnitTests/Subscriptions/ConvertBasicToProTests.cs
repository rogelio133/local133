using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using static Local133.UnitTests.Subscriptions.PlanStatusTests;

namespace Local133.UnitTests.Subscriptions;

/// <summary>
/// Subir de Básico a Pro (§3 del diseño de planes Básico y Pro, 5.12): el tiempo de Básico que queda se vuelve Pro en
/// proporción al precio mensual (99/199, o 69/149 de fundador) y lo que sigue se recorre sin dejar hueco.
/// </summary>
public class ConvertBasicToProTests
{
    private const int GraceDays = 5;

    private static readonly SystemSettings Settings = new();

    private static readonly decimal Ratio = SubscriptionPricing.ConversionRatio(Settings, isFounder: false);

    private static readonly DateTimeOffset Now = Mx(2026, 11, 30, 10);

    private static DateTimeOffset MxSeconds(int year, int month, int day, int hour, int minute, int second) =>
        new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(-6)).ToUniversalTime();

    /// <summary>Prueba vencida en octubre y Básico pagado del 20 de noviembre al 20 de diciembre (10:00).</summary>
    private static (Subscription Subscription, SubscriptionPeriod Basic) WithBasic()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 10, 1, 10), trialDays: 15);
        var basic = subscription.AddPaidPeriod(
            SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Mx(2026, 11, 20, 10), GraceDays, subscriptionChargeId: 1);
        return (subscription, basic);
    }

    [Fact]
    public void El_ejemplo_del_diseno_20_dias_de_Basico_son_unos_10_de_Pro_y_el_anual_empieza_despues()
    {
        var (subscription, basic) = WithBasic();

        var conversion = subscription.ConvertBasicToPro(Now, Ratio);
        var annual = subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.Annual, Now, GraceDays, subscriptionChargeId: 2);

        // 20 × 99/199 = 9.9497 días = 9 d 22:47:38 (hacia abajo al segundo).
        Assert.Equal(TimeSpan.FromDays(20), conversion.BasicTime);
        Assert.Equal(new TimeSpan(9, 22, 47, 38), conversion.ProTime);

        Assert.Equal(Mx(2026, 11, 30, 10), basic.EndsAt); // lo usado sigue siendo Básico pagado
        Assert.Equal(SubscriptionPeriodKind.Paid, basic.Kind);
        Assert.Equal(SubscriptionPlan.Basic, basic.Plan);

        var pro = subscription.Periods.Single(p => p.Kind == SubscriptionPeriodKind.Conversion);
        Assert.Equal(SubscriptionPlan.Pro, pro.Plan);
        Assert.Equal(Now, pro.StartsAt);
        Assert.Equal(MxSeconds(2026, 12, 10, 8, 47, 38), pro.EndsAt);
        Assert.Null(pro.SubscriptionChargeId);

        Assert.Equal(MxSeconds(2026, 12, 10, 8, 47, 38), annual.StartsAt);
        Assert.Equal(MxSeconds(2027, 12, 10, 8, 47, 38), annual.EndsAt);
        Assert.Equal(annual.EndsAt, subscription.ProUntil);
        Assert.Equal(SubscriptionPeriodKind.Paid, subscription.LastPeriodKind);

        var status = subscription.StatusAt(Now, GraceDays);
        Assert.Equal(PlanTier.Pro, status.Tier);
        Assert.Null(status.NextPlan);
    }

    [Fact]
    public void De_fundador_usa_69_entre_149()
    {
        var (subscription, _) = WithBasic();

        var conversion = subscription.ConvertBasicToPro(Now, SubscriptionPricing.ConversionRatio(Settings, isFounder: true));

        // 20 × 69/149 = 9.2617 días = 9 d 06:16:54.
        Assert.Equal(new TimeSpan(9, 6, 16, 54), conversion.ProTime);
        Assert.Equal(MxSeconds(2026, 12, 9, 16, 16, 54), subscription.ProUntil);
    }

    [Fact]
    public void Varios_tramos_de_Basico_y_el_futuro_conserva_su_referido()
    {
        var (subscription, _) = WithBasic();
        var reward = subscription.AddDaysPeriod(
            SubscriptionPeriodKind.ReferralReward, SubscriptionPlan.Basic, 10, Mx(2026, 11, 21, 10), GraceDays, referralId: 9);

        var conversion = subscription.ConvertBasicToPro(Now, 0.5m);

        Assert.Equal(TimeSpan.FromDays(30), conversion.BasicTime);
        Assert.Equal(TimeSpan.FromDays(15), conversion.ProTime);
        Assert.Equal(SubscriptionPeriodKind.Conversion, reward.Kind);
        Assert.Equal(SubscriptionPlan.Pro, reward.Plan);
        Assert.Equal(9, reward.ReferralId);
        Assert.Equal(Mx(2026, 12, 10, 10), reward.StartsAt);
        Assert.Equal(Mx(2026, 12, 15, 10), reward.EndsAt);
        Assert.Equal(Mx(2026, 12, 15, 10), subscription.ProUntil);
    }

    [Fact]
    public void Basico_despues_de_Pro_se_convierte_en_su_lugar_y_conserva_su_cargo()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 10, 1, 10), trialDays: 15);
        var pro = subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Mx(2026, 11, 20, 10), GraceDays, 1);
        var basic = subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Mx(2026, 11, 25, 10), GraceDays, 2);

        subscription.ConvertBasicToPro(Now, 0.5m);

        Assert.Equal((Mx(2026, 11, 20, 10), Mx(2026, 12, 20, 10)), (pro.StartsAt, pro.EndsAt)); // no se toca
        Assert.Equal(SubscriptionPeriodKind.Conversion, basic.Kind);
        Assert.Equal(2, basic.SubscriptionChargeId);
        Assert.Equal(Mx(2026, 12, 20, 10), basic.StartsAt);
        Assert.Equal(Mx(2027, 1, 4, 22), basic.EndsAt); // 31 días × 0.5 = 15 d 12 h
    }

    [Fact]
    public void Lo_que_sigue_se_recorre_hacia_atras_sin_hueco()
    {
        var (subscription, _) = WithBasic();
        var pro = subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Mx(2026, 11, 25, 10), GraceDays, 2);
        var basic = subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Mx(2026, 11, 25, 11), GraceDays, 3);

        subscription.ConvertBasicToPro(Now, 0.5m);

        Assert.Equal(Mx(2026, 12, 10, 10), pro.StartsAt);
        Assert.Equal(Mx(2027, 1, 10, 10), pro.EndsAt); // conserva sus 31 días
        Assert.Equal(Mx(2027, 1, 10, 10), basic.StartsAt);
        Assert.Equal(Mx(2027, 1, 25, 22), basic.EndsAt);
        Assert.Equal(basic.EndsAt, subscription.ProUntil);
        Assert.Equal(SubscriptionPeriodKind.Conversion, subscription.LastPeriodKind);
    }

    [Fact]
    public void Sin_Basico_por_delante_no_cambia_nada()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 20, 10), trialDays: 15);
        var before = subscription.ProUntil;

        Assert.Same(BasicToProConversion.None, subscription.ConvertBasicToPro(Now, Ratio));
        Assert.Single(subscription.Periods);
        Assert.Equal(before, subscription.ProUntil);
    }

    [Fact]
    public void En_la_gracia_de_Basico_no_hay_conversion_y_Pro_empieza_donde_vencio()
    {
        var (subscription, _) = WithBasic();
        var inGrace = Mx(2026, 12, 22, 10);

        Assert.Same(BasicToProConversion.None, subscription.ConvertBasicToPro(inGrace, Ratio));
        Assert.Equal(Mx(2026, 12, 20, 10), subscription.NextPeriodStartFor(SubscriptionPlan.Pro, inGrace, GraceDays, Ratio));
    }

    [Fact]
    public void Fin_de_mes_y_medianoche_de_Mexico()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 1, 1, 0), trialDays: 15);
        subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Mx(2026, 1, 31, 0), GraceDays, 1);
        var now = Mx(2026, 2, 10, 0);

        subscription.ConvertBasicToPro(now, 0.5m);
        var paid = subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, now, GraceDays, 2);

        // Del 31/01 un mes llega al 28/02: quedaban 18 días ⇒ 9 de Pro, hasta la medianoche del 19/02.
        Assert.Equal(Mx(2026, 2, 19, 0), paid.StartsAt);
        Assert.Equal(Mx(2026, 3, 19, 0), paid.EndsAt);
        Assert.Equal(new DateOnly(2026, 3, 18), subscription.StatusAt(now, GraceDays).LastActiveDay);
    }

    [Fact]
    public void La_vista_previa_coincide_con_la_conversion_y_no_cambia_nada()
    {
        var (subscription, _) = WithBasic();
        var periods = subscription.Periods.Count;

        var preview = subscription.NextPeriodStartFor(SubscriptionPlan.Pro, Now, GraceDays, Ratio);
        var basicStart = subscription.NextPeriodStartFor(SubscriptionPlan.Basic, Now, GraceDays, Ratio);

        Assert.Equal(TimeSpan.FromDays(20), subscription.BasicTimeAhead(Now));
        Assert.Equal(periods, subscription.Periods.Count);
        Assert.Equal(Mx(2026, 12, 20, 10), subscription.ProUntil);
        Assert.Equal(Mx(2026, 12, 20, 10), basicStart);

        subscription.ConvertBasicToPro(Now, Ratio);
        Assert.Equal(subscription.ProUntil, preview);
    }

    [Fact]
    public void Las_ofertas_de_Pro_cuentan_desde_el_fin_de_la_conversion()
    {
        var (subscription, _) = WithBasic();

        var offers = SubscriptionOffers.For(subscription, SubscriptionPlan.Pro, Settings, Now, founderSlotAvailable: false);
        var basicOffers = SubscriptionOffers.For(subscription, SubscriptionPlan.Basic, Settings, Now, founderSlotAvailable: false);

        var month = offers.Single(o => o.Package == SubscriptionPackage.OneMonth);
        Assert.Equal(SubscriptionPlan.Pro, month.Plan);
        Assert.Equal(MxSeconds(2027, 1, 10, 8, 47, 38), month.Until);
        Assert.Equal(199m, month.Quote.Amount);
        var basicMonth = basicOffers.Single(o => o.Package == SubscriptionPackage.OneMonth);
        Assert.Equal(Mx(2027, 1, 20, 10), basicMonth.Until);
        Assert.Equal(99m, basicMonth.Quote.Amount);
        Assert.Equal(396m, basicOffers.Single(o => o.Package == SubscriptionPackage.SixMonths).AnnualUpgradeDifference);
        Assert.Equal(2, basicOffers.Single(o => o.Package == SubscriptionPackage.Annual).GiftMonths);
    }

    [Fact]
    public void Un_premio_de_Basico_convertido_se_puede_anular()
    {
        var (subscription, _) = WithBasic();
        var reward = subscription.AddDaysPeriod(
            SubscriptionPeriodKind.ReferralReward, SubscriptionPlan.Basic, 10, Mx(2026, 11, 21, 10), GraceDays, referralId: 9);
        subscription.ConvertBasicToPro(Now, 0.5m);

        var (removed, removePeriod) = subscription.RemoveUnusedDays(reward, Now);

        Assert.Equal(TimeSpan.FromDays(5), removed);
        Assert.True(removePeriod);
        Assert.Equal(Mx(2026, 12, 10, 10), subscription.ProUntil);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void La_proporcion_va_de_cero_a_uno(decimal ratio) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WithBasic().Subscription.ConvertBasicToPro(Now, ratio));
}
