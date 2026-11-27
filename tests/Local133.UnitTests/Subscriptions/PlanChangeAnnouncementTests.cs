using Local133.Domain.Subscriptions;
using static Local133.UnitTests.Subscriptions.PlanStatusTests;

namespace Local133.UnitTests.Subscriptions;

/// <summary>5.16: anuncio del cambio de planes (§10): cuándo se manda, a quién y qué situación explica.</summary>
public class PlanChangeAnnouncementTests
{
    private const int GraceDays = 5;
    private static readonly DateTimeOffset Cutoff = Mx(2026, 12, 1, 3);

    [Fact]
    public void Se_manda_desde_el_corte_y_durante_30_dias()
    {
        Assert.False(PlanChangeAnnouncement.IsDue(Cutoff, Cutoff.AddTicks(-1)));
        Assert.True(PlanChangeAnnouncement.IsDue(Cutoff, Cutoff));
        Assert.True(PlanChangeAnnouncement.IsDue(Cutoff, Cutoff.AddDays(30).AddTicks(-1)));
        Assert.False(PlanChangeAnnouncement.IsDue(Cutoff, Cutoff.AddDays(30)));
    }

    [Fact]
    public void Solo_a_los_registrados_antes_del_corte()
    {
        Assert.True(PlanChangeAnnouncement.Applies(Cutoff.AddTicks(-1), Cutoff));
        Assert.False(PlanChangeAnnouncement.Applies(Cutoff, Cutoff));
    }

    [Fact]
    public void Sin_suscripcion_es_el_onboarding()
    {
        Assert.Equal(PlanChangeSituationKind.Onboarding, PlanChangeSituation.For(null, Cutoff, GraceDays).Kind);
    }

    [Fact]
    public void Con_la_transicion_dice_hasta_cuando_tiene_Basico()
    {
        var situation = PlanChangeSituation.For(Subscription(
            (SubscriptionPeriodKind.Trial, Mx(2026, 10, 1, 10), Mx(2026, 10, 31, 10), SubscriptionPlan.Pro),
            (SubscriptionPeriodKind.Transition, Cutoff, Cutoff.AddDays(15), SubscriptionPlan.Basic)), Cutoff.AddHours(1), GraceDays);

        Assert.Equal(PlanChangeSituationKind.Transition, situation.Kind);
        Assert.Equal(SubscriptionPlan.Basic, situation.Plan);
        Assert.Equal(new DateOnly(2026, 12, 16), situation.LastDay); // termina el 16 a las 3:00 de México
    }

    [Fact]
    public void En_la_prueba_la_transicion_viene_despues()
    {
        var trialEnd = Mx(2026, 12, 10, 10);
        var situation = PlanChangeSituation.For(Subscription(
            (SubscriptionPeriodKind.Trial, Mx(2026, 11, 10, 10), trialEnd, SubscriptionPlan.Pro),
            (SubscriptionPeriodKind.Transition, trialEnd, trialEnd.AddDays(15), SubscriptionPlan.Basic)), Cutoff, GraceDays);

        Assert.Equal(PlanChangeSituationKind.TrialThenTransition, situation.Kind);
        Assert.Equal(new DateOnly(2026, 12, 25), situation.LastDay);
    }

    [Fact]
    public void Con_Pro_pagado_sigue_igual()
    {
        var situation = PlanChangeSituation.For(Subscription(
            (SubscriptionPeriodKind.Trial, Mx(2026, 10, 1, 10), Mx(2026, 10, 31, 10), SubscriptionPlan.Pro),
            (SubscriptionPeriodKind.Paid, Mx(2026, 10, 31, 10), Mx(2026, 12, 31, 10), SubscriptionPlan.Pro)), Cutoff, GraceDays);

        Assert.Equal(PlanChangeSituationKind.Current, situation.Kind);
        Assert.Equal(SubscriptionPlan.Pro, situation.Plan);
        Assert.Equal(new DateOnly(2026, 12, 31), situation.LastDay);
    }

    [Fact]
    public void Una_transicion_que_ya_termino_no_cuenta()
    {
        var situation = PlanChangeSituation.For(Subscription(
            (SubscriptionPeriodKind.Trial, Mx(2026, 10, 1, 10), Mx(2026, 10, 31, 10), SubscriptionPlan.Pro),
            (SubscriptionPeriodKind.Transition, Cutoff, Cutoff.AddDays(15), SubscriptionPlan.Basic)), Cutoff.AddDays(20), GraceDays);

        Assert.Equal(PlanChangeSituationKind.Expired, situation.Kind);
    }

    private static Subscription Subscription(
        params (SubscriptionPeriodKind Kind, DateTimeOffset Start, DateTimeOffset End, SubscriptionPlan Plan)[] periods) => new()
    {
        MerchantId = 1,
        Periods = periods
            .Select(p => new SubscriptionPeriod { MerchantId = 1, Kind = p.Kind, Plan = p.Plan, StartsAt = p.Start, EndsAt = p.End })
            .ToList(),
    };
}
