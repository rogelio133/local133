using Local133.Domain.Subscriptions;

namespace Local133.UnitTests.Subscriptions;

/// <summary>Estado del plan calculado con los periodos (5.3). México va en UTC−6, sin horario de verano.</summary>
public class PlanStatusTests
{
    private const int GraceDays = 5;

    /// <summary>19 de noviembre de 2026, 15:00 en México.</summary>
    private static readonly DateTimeOffset Activated = Mx(2026, 11, 19, 15);

    [Fact]
    public void Sin_periodos_esta_en_onboarding()
    {
        var status = PlanStatus.For([], Activated, GraceDays);

        Assert.Equal(PlanTier.Onboarding, status.Tier);
        Assert.Null(status.ProUntil);
        Assert.Null(status.GraceEndsAt);
        Assert.Null(status.DaysLeft(Activated));
        Assert.False(status.IsProLike);
    }

    [Fact]
    public void La_prueba_dura_30_dias_al_instante_y_cuenta_los_dias_que_quedan()
    {
        var subscription = Subscription.StartTrial(1, Activated, trialDays: 30);
        var now = Mx(2026, 11, 26, 9);

        var status = subscription.StatusAt(now, GraceDays);

        Assert.Equal(PlanTier.Trial, status.Tier);
        Assert.True(status.IsProLike);
        Assert.Equal(Mx(2026, 12, 19, 15), status.ProUntil);
        Assert.Equal(new DateOnly(2026, 12, 19), status.LastProDay);
        Assert.Equal(23, status.DaysLeft(now));
        Assert.Null(status.GraceEndsAt); // la prueba no tiene gracia
    }

    [Fact]
    public void El_ultimo_dia_de_la_prueba_quedan_cero_dias_y_al_terminar_baja_al_gratuito_sin_gracia()
    {
        var subscription = Subscription.StartTrial(1, Activated, trialDays: 30);

        var lastDay = subscription.StatusAt(Mx(2026, 12, 19, 14, 59), GraceDays);
        Assert.Equal(PlanTier.Trial, lastDay.Tier);
        Assert.Equal(0, lastDay.DaysLeft(Mx(2026, 12, 19, 14, 59)));

        var ended = subscription.StatusAt(Mx(2026, 12, 19, 15), GraceDays);
        Assert.Equal(PlanTier.Free, ended.Tier);
        Assert.Null(ended.GraceEndsAt);
        Assert.Equal(0, ended.DaysLeft(Mx(2026, 12, 20, 10)));
    }

    [Fact]
    public void Un_periodo_pagado_vigente_es_Pro()
    {
        var subscription = Subscription.StartTrial(1, Activated, trialDays: 30);
        subscription.AddPaidPeriod(SubscriptionPackage.OneMonth, Mx(2026, 12, 1, 10), GraceDays);

        var status = subscription.StatusAt(Mx(2026, 12, 25, 10), GraceDays);

        Assert.Equal(PlanTier.Pro, status.Tier);
        Assert.Equal(Mx(2027, 1, 19, 15), status.ProUntil);
        Assert.Equal(Mx(2027, 1, 24, 15), status.GraceEndsAt);
    }

    [Fact]
    public void La_gracia_dura_cinco_dias_despues_de_un_periodo_pagado()
    {
        var subscription = Subscription.StartTrial(1, Activated, trialDays: 30);
        subscription.AddPaidPeriod(SubscriptionPackage.OneMonth, Mx(2026, 12, 1, 10), GraceDays);
        var paidEnd = Mx(2027, 1, 19, 15);

        Assert.Equal(PlanTier.Grace, subscription.StatusAt(paidEnd, GraceDays).Tier);
        // Día 5 de la gracia: todavía tiene Pro.
        Assert.Equal(PlanTier.Grace, subscription.StatusAt(Mx(2027, 1, 24, 14, 59), GraceDays).Tier);
        Assert.Equal(new DateOnly(2027, 1, 24), subscription.StatusAt(paidEnd, GraceDays).LastGraceDay);
        // Día 6: ya está en el gratuito.
        Assert.Equal(PlanTier.Free, subscription.StatusAt(Mx(2027, 1, 24, 15), GraceDays).Tier);
    }

    [Fact]
    public void Sin_dias_de_gracia_baja_al_gratuito_en_cuanto_vence()
    {
        var subscription = Subscription.StartTrial(1, Activated, trialDays: 30);
        subscription.AddPaidPeriod(SubscriptionPackage.OneMonth, Mx(2026, 12, 1, 10), graceDays: 0);

        Assert.Equal(PlanTier.Free, subscription.StatusAt(Mx(2027, 1, 19, 15), graceDays: 0).Tier);
    }

    [Theory]
    [InlineData(SubscriptionPeriodKind.ReferralReward)]
    [InlineData(SubscriptionPeriodKind.AdminGrant)]
    public void Al_terminar_un_premio_o_un_regalo_baja_al_gratuito_sin_gracia(SubscriptionPeriodKind kind)
    {
        var subscription = Subscription.StartTrial(1, Activated, trialDays: 30);
        var now = Mx(2027, 2, 1, 10); // ya en el gratuito
        subscription.AddDaysPeriod(kind, 10, now, GraceDays);

        Assert.Equal(PlanTier.Pro, subscription.StatusAt(Mx(2027, 2, 11, 9, 59), GraceDays).Tier);
        var ended = subscription.StatusAt(Mx(2027, 2, 11, 10), GraceDays);
        Assert.Equal(PlanTier.Free, ended.Tier);
        Assert.Null(ended.GraceEndsAt);
    }

    [Fact]
    public void Un_premio_despues_de_un_pago_quita_la_gracia_de_ese_pago()
    {
        var subscription = Subscription.StartTrial(1, Activated, trialDays: 30);
        subscription.AddPaidPeriod(SubscriptionPackage.OneMonth, Mx(2026, 12, 1, 10), GraceDays);
        subscription.AddDaysPeriod(SubscriptionPeriodKind.ReferralReward, 10, Mx(2027, 1, 5, 10), GraceDays);

        // El premio empieza al terminar el mes pagado (19/01) y termina el 29/01; después no hay gracia.
        Assert.Equal(PlanTier.Free, subscription.StatusAt(Mx(2027, 1, 29, 15), GraceDays).Tier);
    }

    [Fact]
    public void Los_dias_se_cuentan_en_hora_de_Mexico_y_no_en_UTC()
    {
        // La prueba empieza a la medianoche de México (06:00 UTC) y termina el 20/12 a las 00:00 de México.
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 20, 0), trialDays: 30);
        // 19/12 a las 21:00 en México = 20/12 a las 03:00 en UTC.
        var now = new DateTimeOffset(2026, 12, 20, 3, 0, 0, TimeSpan.Zero);

        var status = subscription.StatusAt(now, GraceDays);

        Assert.Equal(new DateTimeOffset(2026, 12, 20, 6, 0, 0, TimeSpan.Zero), status.ProUntil);
        Assert.Equal(PlanTier.Trial, status.Tier);
        Assert.Equal(new DateOnly(2026, 12, 19), status.LastProDay); // no el 20: a esa medianoche ya terminó
        Assert.Equal(0, status.DaysLeft(now));
    }

    [Fact]
    public void Gracia_negativa_se_rechaza() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanStatus.For([], Activated, graceDays: -1));

    /// <summary>Instante en hora del centro de México (UTC−6).</summary>
    internal static DateTimeOffset Mx(int year, int month, int day, int hour, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(-6)).ToUniversalTime();
}
