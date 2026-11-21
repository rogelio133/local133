using Local133.Domain.Subscriptions;
using static Local133.UnitTests.Subscriptions.PlanStatusTests;

namespace Local133.UnitTests.Subscriptions;

/// <summary>
/// Dónde empieza un periodo nuevo (§3): donde termina el anterior si sigue con Pro o está en gracia, o en ese momento
/// si ya estaba en el gratuito. Los meses se suman en hora de México.
/// </summary>
public class SubscriptionPeriodChainTests
{
    private const int GraceDays = 5;

    [Fact]
    public void La_prueba_crea_la_suscripcion_con_un_solo_periodo()
    {
        var start = Mx(2026, 11, 19, 15);

        var subscription = Subscription.StartTrial(7, start, trialDays: 30);

        var trial = Assert.Single(subscription.Periods);
        Assert.Equal(SubscriptionPeriodKind.Trial, trial.Kind);
        Assert.Equal(7, trial.MerchantId);
        Assert.Equal(start, trial.StartsAt);
        Assert.Equal(Mx(2026, 12, 19, 15), trial.EndsAt);
        Assert.Equal(start, subscription.TrialStartedAt);
        Assert.Equal(trial.EndsAt, subscription.ProUntil);
        Assert.Equal(SubscriptionPeriodKind.Trial, subscription.LastPeriodKind);
        Assert.Null(subscription.LastPackage);
        Assert.False(subscription.IsFounder);
    }

    [Fact]
    public void Pagar_durante_la_prueba_no_la_acorta()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 19, 15), trialDays: 30);

        var paid = subscription.AddPaidPeriod(SubscriptionPackage.OneMonth, Mx(2026, 11, 25, 10), GraceDays);

        Assert.Equal(Mx(2026, 12, 19, 15), paid.StartsAt);
        Assert.Equal(Mx(2027, 1, 19, 15), paid.EndsAt);
        Assert.Equal(paid.EndsAt, subscription.ProUntil);
        Assert.Equal(SubscriptionPeriodKind.Paid, subscription.LastPeriodKind);
        Assert.Equal(SubscriptionPackage.OneMonth, subscription.LastPackage);
        var status = subscription.StatusAt(Mx(2026, 11, 25, 10), GraceDays);
        Assert.Equal(PlanTier.Pro, status.Tier);
        Assert.Equal(Mx(2026, 12, 19, 15), status.TrialEndsAt);
    }

    [Fact]
    public void Pagar_durante_la_gracia_cuenta_desde_el_vencimiento_original()
    {
        // Vence el 10 de marzo y paga un mes el 13 ⇒ Pro hasta el 10 de abril.
        var subscription = PaidUntilMarch10();
        var payday = Mx(2026, 3, 13, 18);
        Assert.Equal(PlanTier.Grace, subscription.StatusAt(payday, GraceDays).Tier);

        var paid = subscription.AddPaidPeriod(SubscriptionPackage.OneMonth, payday, GraceDays);

        Assert.Equal(Mx(2026, 3, 10, 10), paid.StartsAt);
        Assert.Equal(Mx(2026, 4, 10, 10), paid.EndsAt);
        Assert.Equal(PlanTier.Pro, subscription.StatusAt(payday, GraceDays).Tier);
    }

    [Fact]
    public void Pagar_despues_de_la_gracia_empieza_en_ese_momento()
    {
        var subscription = PaidUntilMarch10();
        var payday = Mx(2026, 3, 20, 12);
        Assert.Equal(PlanTier.Free, subscription.StatusAt(payday, GraceDays).Tier);

        var paid = subscription.AddPaidPeriod(SubscriptionPackage.OneMonth, payday, GraceDays);

        Assert.Equal(payday, paid.StartsAt);
        Assert.Equal(Mx(2026, 4, 20, 12), paid.EndsAt);
    }

    [Fact]
    public void El_anual_sobre_un_mes_pagado_empieza_al_terminar_el_mes()
    {
        var subscription = PaidUntilMarch10();

        var annual = subscription.AddPaidPeriod(SubscriptionPackage.Annual, Mx(2026, 2, 20, 9), GraceDays);

        Assert.Equal(Mx(2026, 3, 10, 10), annual.StartsAt);
        Assert.Equal(Mx(2027, 3, 10, 10), annual.EndsAt);
        Assert.Equal(SubscriptionPackage.Annual, subscription.LastPackage);
        Assert.Equal(annual.EndsAt, subscription.ProUntil);
    }

    [Theory]
    [InlineData(SubscriptionPackage.ThreeMonths, 6)]
    [InlineData(SubscriptionPackage.SixMonths, 9)]
    public void Los_paquetes_cubren_sus_meses(SubscriptionPackage package, int endMonth)
    {
        var subscription = PaidUntilMarch10();

        var paid = subscription.AddPaidPeriod(package, Mx(2026, 3, 1, 9), GraceDays);

        Assert.Equal(Mx(2026, endMonth, 10, 10), paid.EndsAt);
    }

    [Fact]
    public void Los_premios_se_encadenan()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 19, 15), trialDays: 30);
        var now = Mx(2027, 1, 5, 8); // ya en el gratuito

        var first = subscription.AddDaysPeriod(SubscriptionPeriodKind.ReferralReward, 10, now, GraceDays);
        var second = subscription.AddDaysPeriod(SubscriptionPeriodKind.ReferralReward, 10, now, GraceDays);

        Assert.Equal(now, first.StartsAt);
        Assert.Equal(first.EndsAt, second.StartsAt);
        Assert.Equal(Mx(2027, 1, 25, 8), second.EndsAt);
        Assert.Equal(SubscriptionPeriodKind.ReferralReward, subscription.LastPeriodKind);
        Assert.Null(subscription.LastPackage); // un premio no cambia el paquete de renovación
    }

    [Fact]
    public void Un_mes_desde_el_31_de_enero_llega_al_ultimo_dia_de_febrero()
    {
        var subscription = Subscription.StartTrial(1, Mx(2025, 12, 1, 12), trialDays: 30);

        var paid = subscription.AddPaidPeriod(SubscriptionPackage.OneMonth, Mx(2026, 1, 31, 12), GraceDays);

        Assert.Equal(Mx(2026, 2, 28, 12), paid.EndsAt);
    }

    [Fact]
    public void En_anio_bisiesto_el_mes_desde_el_31_de_enero_llega_al_29_de_febrero()
    {
        var subscription = Subscription.StartTrial(1, Mx(2027, 12, 1, 12), trialDays: 30);

        var paid = subscription.AddPaidPeriod(SubscriptionPackage.OneMonth, Mx(2028, 1, 31, 12), GraceDays);

        Assert.Equal(Mx(2028, 2, 29, 12), paid.EndsAt);
    }

    [Fact]
    public void Un_mes_pagado_a_las_11_pm_de_Mexico_se_cuenta_en_el_calendario_de_Mexico()
    {
        // 31/01 a las 23:00 en México = 01/02 a las 05:00 en UTC: en UTC el mes llegaría al 1 de marzo.
        var subscription = Subscription.StartTrial(1, Mx(2025, 12, 1, 12), trialDays: 30);

        var paid = subscription.AddPaidPeriod(SubscriptionPackage.OneMonth, Mx(2026, 1, 31, 23), GraceDays);

        Assert.Equal(Mx(2026, 2, 28, 23), paid.EndsAt);
    }

    [Theory]
    [InlineData(SubscriptionPeriodKind.Trial)]
    [InlineData(SubscriptionPeriodKind.Paid)]
    public void Por_dias_solo_se_dan_premios_y_regalos(SubscriptionPeriodKind kind)
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 19, 15), trialDays: 30);

        Assert.Throws<ArgumentOutOfRangeException>(() => subscription.AddDaysPeriod(kind, 10, Mx(2026, 11, 20, 0), GraceDays));
    }

    [Fact]
    public void Una_prueba_o_un_premio_sin_dias_se_rechaza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Subscription.StartTrial(1, Mx(2026, 11, 19, 15), trialDays: 0));
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 19, 15), trialDays: 30);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            subscription.AddDaysPeriod(SubscriptionPeriodKind.AdminGrant, 0, Mx(2026, 11, 20, 0), GraceDays));
    }

    /// <summary>Prueba del 8/01 al 7/02 y un mes pagado el 10/02 a las 10:00 (ya en el gratuito) ⇒ vence el 10/03.</summary>
    private static Subscription PaidUntilMarch10()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 1, 8, 10), trialDays: 30);
        var paid = subscription.AddPaidPeriod(SubscriptionPackage.OneMonth, Mx(2026, 2, 10, 10), GraceDays);
        Assert.Equal(Mx(2026, 2, 10, 10), paid.StartsAt);
        Assert.Equal(Mx(2026, 3, 10, 10), paid.EndsAt);
        return subscription;
    }
}
