using Local133.Domain.Subscriptions;
using static Local133.UnitTests.Subscriptions.PlanStatusTests;

namespace Local133.UnitTests.Subscriptions;

/// <summary>
/// Estados con los planes Básico y Pro (5.12): Básico, gracia según el plan pagado, vencido sin gracia y el tramo del
/// plan que sigue («Pro hasta el …, después Básico»). México va en UTC−6, sin horario de verano.
/// </summary>
public class PlanStatusPlansTests
{
    private const int GraceDays = 5;

    /// <summary>Prueba de 15 días del 1 al 16 de noviembre de 2026, a las 10:00.</summary>
    private static Subscription Trial() => Subscription.StartTrial(1, Mx(2026, 11, 1, 10), trialDays: 15);

    [Fact]
    public void La_prueba_es_de_Pro_y_al_terminar_vence_sin_gracia()
    {
        var subscription = Trial();

        var trial = subscription.StatusAt(Mx(2026, 11, 10, 10), GraceDays);
        Assert.Equal(PlanTier.Trial, trial.Tier);
        Assert.Equal(SubscriptionPlan.Pro, trial.Plan);
        Assert.Equal(SubscriptionPlan.Pro, subscription.Periods.Single().Plan);
        Assert.True(trial.IsCurrent);

        var expired = subscription.StatusAt(Mx(2026, 11, 16, 10), GraceDays);
        Assert.Equal(PlanTier.Expired, expired.Tier);
        Assert.Null(expired.Plan);
        Assert.Null(expired.GraceEndsAt);
        Assert.False(expired.IsCurrent);
        Assert.Equal(new DateOnly(2026, 11, 16), expired.LastActiveDay);
    }

    [Fact]
    public void Basico_pagado_es_Basico_con_su_gracia_y_luego_vence()
    {
        var subscription = Trial();
        subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Mx(2026, 11, 20, 10), GraceDays);

        var basic = subscription.StatusAt(Mx(2026, 12, 1, 10), GraceDays);
        Assert.Equal(PlanTier.Basic, basic.Tier);
        Assert.Equal(SubscriptionPlan.Basic, basic.Plan);
        Assert.Equal(Mx(2026, 12, 20, 10), basic.PlanEndsAt);
        Assert.Equal(new DateOnly(2026, 12, 20), basic.LastPlanDay);
        Assert.Null(basic.NextPlan);
        Assert.Equal(Mx(2026, 12, 25, 10), basic.GraceEndsAt);

        var grace = subscription.StatusAt(Mx(2026, 12, 22, 10), GraceDays);
        Assert.Equal(PlanTier.Grace, grace.Tier);
        Assert.Equal(SubscriptionPlan.Basic, grace.Plan);
        Assert.Null(grace.PlanEndsAt);
        Assert.False(PlanEntitlements.For(grace).CanChargeWithCart);
        Assert.True(PlanEntitlements.For(grace).CanCharge);

        var expired = subscription.StatusAt(Mx(2026, 12, 25, 10), GraceDays);
        Assert.Equal(PlanTier.Expired, expired.Tier);
        Assert.False(PlanEntitlements.For(expired).CanCharge);
    }

    [Fact]
    public void La_gracia_de_Pro_conserva_las_funciones_de_Pro()
    {
        var subscription = Trial();
        subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Mx(2026, 11, 20, 10), GraceDays);

        var grace = subscription.StatusAt(Mx(2026, 12, 22, 10), GraceDays);

        Assert.Equal(PlanTier.Grace, grace.Tier);
        Assert.Equal(SubscriptionPlan.Pro, grace.Plan);
        Assert.True(PlanEntitlements.For(grace).CanChargeWithCart);
    }

    [Fact]
    public void Con_Basico_pagado_durante_la_prueba_sigue_en_la_prueba_y_dice_cuando_empieza_Basico()
    {
        var subscription = Trial();
        var basic = subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Mx(2026, 11, 5, 10), GraceDays);

        var status = subscription.StatusAt(Mx(2026, 11, 10, 10), GraceDays);

        Assert.Equal(Mx(2026, 11, 16, 10), basic.StartsAt);
        Assert.Equal(PlanTier.Trial, status.Tier);
        Assert.Equal(SubscriptionPlan.Pro, status.Plan);
        Assert.Equal(Mx(2026, 11, 16, 10), status.PlanEndsAt);
        Assert.Equal(SubscriptionPlan.Basic, status.NextPlan);
        Assert.Equal(new DateOnly(2026, 11, 16), status.NextPlanStartDay);
        Assert.Equal(new DateOnly(2026, 12, 16), status.NextPlanLastDay);
        Assert.Equal(Mx(2026, 12, 16, 10), status.ProUntil);
    }

    [Fact]
    public void Con_Pro_pagado_durante_la_prueba_ya_es_Pro_y_los_tramos_se_juntan()
    {
        var subscription = Trial();
        subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Mx(2026, 11, 5, 10), GraceDays);

        var status = subscription.StatusAt(Mx(2026, 11, 10, 10), GraceDays);

        Assert.Equal(PlanTier.Pro, status.Tier);
        Assert.Equal(Mx(2026, 11, 16, 10), status.TrialEndsAt);
        Assert.Equal(Mx(2026, 12, 16, 10), status.PlanEndsAt);
        Assert.Null(status.NextPlan);
    }

    [Fact]
    public void Pro_despues_Basico()
    {
        var subscription = Trial();
        subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Mx(2026, 11, 20, 10), GraceDays);
        subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Mx(2026, 11, 25, 10), GraceDays);

        var status = subscription.StatusAt(Mx(2026, 11, 25, 10), GraceDays);

        Assert.Equal(PlanTier.Pro, status.Tier);
        Assert.Equal(new DateOnly(2026, 12, 20), status.LastPlanDay);
        Assert.Equal(SubscriptionPlan.Basic, status.NextPlan);
        Assert.Equal(new DateOnly(2027, 1, 20), status.NextPlanLastDay);
        Assert.Equal(Mx(2027, 1, 25, 10), status.GraceEndsAt);

        var later = subscription.StatusAt(Mx(2026, 12, 21, 10), GraceDays);
        Assert.Equal(PlanTier.Basic, later.Tier);
        Assert.Null(later.NextPlan);

        var grace = subscription.StatusAt(Mx(2027, 1, 22, 10), GraceDays);
        Assert.Equal(PlanTier.Grace, grace.Tier);
        Assert.Equal(SubscriptionPlan.Basic, grace.Plan); // la gracia es del último periodo pagado
    }

    [Theory]
    [InlineData(SubscriptionPeriodKind.ReferralReward)]
    [InlineData(SubscriptionPeriodKind.AdminGrant)]
    [InlineData(SubscriptionPeriodKind.Transition)]
    public void Los_dias_de_Basico_regalados_vencen_sin_gracia(SubscriptionPeriodKind kind)
    {
        var subscription = Trial();
        var period = subscription.AddDaysPeriod(kind, SubscriptionPlan.Basic, 10, Mx(2026, 11, 10, 10), GraceDays);

        Assert.Equal(Mx(2026, 11, 16, 10), period.StartsAt);
        Assert.Equal(SubscriptionPlan.Basic, subscription.ChainEndPlan);
        Assert.Equal(PlanTier.Basic, subscription.StatusAt(Mx(2026, 11, 20, 10), GraceDays).Tier);

        var expired = subscription.StatusAt(Mx(2026, 11, 26, 10), GraceDays);
        Assert.Equal(PlanTier.Expired, expired.Tier);
        Assert.Null(expired.GraceEndsAt);
    }

    [Fact]
    public void Despues_de_una_conversion_vence_sin_gracia()
    {
        var subscription = Trial();
        subscription.AddDaysPeriod(SubscriptionPeriodKind.AdminGrant, SubscriptionPlan.Basic, 10, Mx(2026, 11, 10, 10), GraceDays);
        subscription.ConvertBasicToPro(Mx(2026, 11, 20, 10), 0.5m);

        Assert.Equal(PlanTier.Pro, subscription.StatusAt(Mx(2026, 11, 21, 10), GraceDays).Tier);
        var expired = subscription.StatusAt(Mx(2026, 11, 23, 10), GraceDays);
        Assert.Equal(PlanTier.Expired, expired.Tier);
        Assert.Null(expired.GraceEndsAt);
        Assert.Equal(SubscriptionPeriodKind.Conversion, subscription.LastPeriodKind);
    }

    [Fact]
    public void Pagar_Basico_vencido_empieza_en_ese_momento_y_en_la_gracia_donde_vencio()
    {
        var subscription = Trial();
        var first = subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Mx(2026, 11, 20, 10), GraceDays);
        var second = subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Mx(2026, 12, 22, 10), GraceDays);

        Assert.Equal(Mx(2026, 11, 20, 10), first.StartsAt); // ya había vencido la prueba
        Assert.Equal(Mx(2026, 12, 20, 10), second.StartsAt); // en la gracia de Básico: sin conversión, donde venció
        Assert.Equal(SubscriptionPlan.Pro, subscription.LastPlan);
    }

    [Fact]
    public void El_premio_sigue_el_plan_del_final_de_la_cadena()
    {
        var subscription = Trial();
        Assert.Equal(SubscriptionPlan.Pro, subscription.ChainEndPlan);

        subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Mx(2026, 11, 5, 10), GraceDays);
        Assert.Equal(SubscriptionPlan.Basic, subscription.ChainEndPlan);
    }

    [Fact]
    public void La_conversion_no_se_da_por_dias() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Trial().AddDaysPeriod(SubscriptionPeriodKind.Conversion, SubscriptionPlan.Pro, 10, Mx(2026, 11, 10, 10), GraceDays));
}
