using Local133.Domain.Audit;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using static Local133.UnitTests.Subscriptions.PlanStatusTests;

namespace Local133.UnitTests.Subscriptions;

/// <summary>
/// 6.2: anular un premio de referido (quitar los días que no se usan y recorrer lo que sigue), anular el referido,
/// abono manual y reglas de los parámetros globales.
/// </summary>
public class AdminPlanRulesTests
{
    private const int GraceDays = 5;

    // --- Quitar los días de un premio ------------------------------------------------------------------------------

    [Fact]
    public void Premio_que_no_ha_empezado_se_quita_y_el_pago_siguiente_se_recorre()
    {
        // Prueba del 1 al 31 de noviembre, premio de 10 días después y un mes pagado después del premio.
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 1, 10), trialDays: 30);
        var reward = subscription.AddDaysPeriod(SubscriptionPeriodKind.ReferralReward, SubscriptionPlan.Pro, 10, Mx(2026, 11, 5, 10), GraceDays, referralId: 9);
        var paid = subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Mx(2026, 11, 6, 10), GraceDays);
        var paidLength = paid.EndsAt - paid.StartsAt;
        Assert.Equal(reward.EndsAt, paid.StartsAt);

        var (removed, removePeriod) = subscription.RemoveUnusedDays(reward, Mx(2026, 11, 7, 10));

        Assert.True(removePeriod);
        Assert.Equal(TimeSpan.FromDays(10), removed);
        Assert.DoesNotContain(reward, subscription.Periods);
        Assert.Equal(Mx(2026, 12, 1, 10), paid.StartsAt); // donde terminaba la prueba: sin hueco
        Assert.Equal(paidLength, paid.EndsAt - paid.StartsAt);
        Assert.Equal(paid.EndsAt, subscription.ProUntil);
        Assert.Equal(SubscriptionPeriodKind.Paid, subscription.LastPeriodKind);
        Assert.Equal(PlanTier.Pro, subscription.StatusAt(Mx(2026, 12, 5, 10), GraceDays).Tier);
    }

    [Fact]
    public void Premio_en_curso_se_corta_ahora_y_lo_que_sigue_se_recorre()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 1, 10), trialDays: 30);
        var reward = subscription.AddDaysPeriod(SubscriptionPeriodKind.ReferralReward, SubscriptionPlan.Pro, 10, Mx(2026, 11, 5, 10), GraceDays, referralId: 9);
        var grant = subscription.AddDaysPeriod(SubscriptionPeriodKind.AdminGrant, SubscriptionPlan.Pro, 5, Mx(2026, 11, 5, 10), GraceDays);
        var now = Mx(2026, 12, 4, 10); // van 3 de los 10 días del premio

        var (removed, removePeriod) = subscription.RemoveUnusedDays(reward, now);

        Assert.False(removePeriod);
        Assert.Equal(TimeSpan.FromDays(7), removed);
        Assert.Equal(now, reward.EndsAt);
        Assert.Equal(now, grant.StartsAt);
        Assert.Equal(now.AddDays(5), grant.EndsAt);
        Assert.Equal(grant.EndsAt, subscription.ProUntil);
        Assert.Equal(SubscriptionPeriodKind.AdminGrant, subscription.LastPeriodKind);
    }

    [Fact]
    public void Premio_ya_usado_no_cambia_nada()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 1, 10), trialDays: 30);
        var reward = subscription.AddDaysPeriod(SubscriptionPeriodKind.ReferralReward, SubscriptionPlan.Pro, 10, Mx(2026, 11, 5, 10), GraceDays, referralId: 9);
        var proUntil = subscription.ProUntil;

        var (removed, removePeriod) = subscription.RemoveUnusedDays(reward, Mx(2026, 12, 20, 10));

        Assert.Equal(TimeSpan.Zero, removed);
        Assert.False(removePeriod);
        Assert.Contains(reward, subscription.Periods);
        Assert.Equal(proUntil, subscription.ProUntil);
    }

    [Fact]
    public void Al_quitar_el_ultimo_premio_el_plan_vuelve_a_terminar_con_la_prueba()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 1, 10), trialDays: 30);
        var trialEnd = subscription.ProUntil;
        var reward = subscription.AddDaysPeriod(SubscriptionPeriodKind.ReferralReward, SubscriptionPlan.Pro, 10, Mx(2026, 11, 5, 10), GraceDays, referralId: 9);

        subscription.RemoveUnusedDays(reward, Mx(2026, 11, 10, 10));

        Assert.Equal(trialEnd, subscription.ProUntil);
        Assert.Equal(SubscriptionPeriodKind.Trial, subscription.LastPeriodKind);
        Assert.Equal(PlanTier.Expired, subscription.StatusAt(trialEnd.AddHours(1), GraceDays).Tier);
    }

    [Fact]
    public void Solo_se_quitan_dias_de_un_premio_de_referido()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 1, 10), trialDays: 30);
        var grant = subscription.AddDaysPeriod(SubscriptionPeriodKind.AdminGrant, SubscriptionPlan.Pro, 10, Mx(2026, 11, 5, 10), GraceDays);

        Assert.Throws<ArgumentException>(() => subscription.RemoveUnusedDays(grant, Mx(2026, 11, 5, 10)));
        Assert.Throws<ArgumentException>(() => subscription.RemoveUnusedDays(subscription.Periods[0], Mx(2026, 11, 5, 10)));
    }

    // --- Anular el referido ----------------------------------------------------------------------------------------

    [Fact]
    public void Un_premio_pendiente_anulado_ya_no_esta_pendiente()
    {
        var referral = Referral.Create(1, 2, "ABC234", Mx(2026, 11, 1, 10));
        referral.Qualify(Mx(2026, 11, 20, 10));
        Assert.True(referral.CanBeRevoked);

        referral.Revoke(Mx(2026, 11, 21, 10));

        Assert.False(referral.IsPendingReward);
        Assert.False(referral.CanBeRevoked);
        Assert.Throws<InvalidOperationException>(() => referral.Revoke(Mx(2026, 11, 22, 10)));
        Assert.Throws<InvalidOperationException>(() => referral.MarkRewarded(Mx(2026, 11, 22, 10)));
    }

    [Fact]
    public void Un_premio_otorgado_se_anula_una_vez()
    {
        var referral = Referral.Create(1, 2, "ABC234", Mx(2026, 11, 1, 10));
        referral.Qualify(Mx(2026, 11, 20, 10));
        referral.MarkRewarded(Mx(2026, 11, 20, 10));
        Assert.True(referral.IsRewardActive);

        referral.Revoke(Mx(2026, 11, 21, 10));

        Assert.False(referral.IsRewardActive);
        Assert.Equal(Mx(2026, 11, 21, 10), referral.RevokedAt);
        Assert.Throws<InvalidOperationException>(() => referral.Revoke(Mx(2026, 11, 22, 10)));
    }

    [Fact]
    public void Sin_pago_o_sin_premio_no_hay_que_anular()
    {
        var notPaid = Referral.Create(1, 2, "ABC234", Mx(2026, 11, 1, 10));
        var skipped = Referral.Create(1, 3, "ABC234", Mx(2026, 11, 1, 10));
        skipped.Qualify(Mx(2026, 11, 20, 10));
        skipped.Skip(ReferralSkipReason.RewardCap);

        Assert.Throws<InvalidOperationException>(() => notPaid.Revoke(Mx(2026, 11, 21, 10)));
        Assert.Throws<InvalidOperationException>(() => skipped.Revoke(Mx(2026, 11, 21, 10)));
    }

    // --- Abono manual ----------------------------------------------------------------------------------------------

    [Fact]
    public void El_abono_manual_sella_cuando_lo_abono_el_admin()
    {
        var charge = SubscriptionCharge.Create(
            1, SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, new SystemSettings(), false, null), false,
            SubscriptionChargeOrigin.Merchant, Mx(2026, 11, 1, 10));
        charge.MarkPartial();

        charge.MarkPaid(Mx(2026, 11, 2, 10), creditedManuallyAt: Mx(2026, 11, 3, 10));

        Assert.Equal(SubscriptionChargeStatus.Paid, charge.Status);
        Assert.Equal(Mx(2026, 11, 3, 10), charge.CreditedManuallyAt);
    }

    // --- Parámetros globales ---------------------------------------------------------------------------------------

    [Fact]
    public void Los_parametros_por_defecto_son_validos()
    {
        Assert.Empty(SystemSettingsRules.Validate(new SystemSettings(), assignedFounders: 0));
    }

    [Theory]
    [InlineData(nameof(SystemSettings.ProMonthlyPrice))]
    [InlineData(nameof(SystemSettings.ProFounderMonthlyPrice))]
    [InlineData(nameof(SystemSettings.BasicMonthlyPrice))]
    [InlineData(nameof(SystemSettings.BasicFounderAnnualPrice))]
    [InlineData(nameof(SystemSettings.FounderSlots))]
    [InlineData(nameof(SystemSettings.TrialDays))]
    [InlineData(nameof(SystemSettings.GraceDays))]
    [InlineData(nameof(SystemSettings.SaleQrExpirationSeconds))]
    [InlineData(nameof(SystemSettings.ReferralWelcomeDiscountRate))]
    [InlineData(nameof(SystemSettings.VatRate))]
    public void Cada_regla_marca_su_campo(string field)
    {
        var settings = new SystemSettings();
        var assigned = 0;
        switch (field)
        {
            case nameof(SystemSettings.ProMonthlyPrice): settings.ProMonthlyPrice = 199.999m; break;
            case nameof(SystemSettings.ProFounderMonthlyPrice): settings.ProFounderMonthlyPrice = 250m; break;
            case nameof(SystemSettings.BasicMonthlyPrice): settings.BasicMonthlyPrice = 199m; break;
            case nameof(SystemSettings.BasicFounderAnnualPrice): settings.BasicFounderAnnualPrice = 1000m; break;
            case nameof(SystemSettings.FounderSlots): settings.FounderSlots = 10; assigned = 11; break;
            case nameof(SystemSettings.TrialDays): settings.TrialDays = 0; break;
            case nameof(SystemSettings.GraceDays): settings.GraceDays = 31; break;
            case nameof(SystemSettings.SaleQrExpirationSeconds): settings.SaleQrExpirationSeconds = 359; break;
            case nameof(SystemSettings.ReferralWelcomeDiscountRate): settings.ReferralWelcomeDiscountRate = 0.95m; break;
            case nameof(SystemSettings.VatRate): settings.VatRate = 0.16005m; break;
        }

        var errors = SystemSettingsRules.Validate(settings, assigned);

        var error = Assert.Single(errors);
        Assert.Equal(field, error.Field);
    }

    [Fact]
    public void Bordes_validos()
    {
        var settings = new SystemSettings
        {
            BasicMonthlyPrice = 0.01m,
            BasicFounderMonthlyPrice = 0.01m,
            ProMonthlyPrice = 0.02m,
            ProFounderMonthlyPrice = 0.02m,
            BasicAnnualPrice = 99_999.98m,
            BasicFounderAnnualPrice = 99_999.98m,
            ProAnnualPrice = 99_999.99m,
            ProFounderAnnualPrice = 99_999.99m,
            FounderSlots = 3,
            GraceDays = 0,
            ReferralRewardsPerYear = 0,
            SaleQrExpirationSeconds = 360,
            VatRate = 0.5m,
        };

        Assert.Empty(SystemSettingsRules.Validate(settings, assignedFounders: 3));
    }

    [Theory]
    [InlineData(99, 99, 69, 149, nameof(SystemSettings.BasicMonthlyPrice))]
    [InlineData(99, 199, 149, 149, nameof(SystemSettings.BasicFounderMonthlyPrice))]
    [InlineData(99, 199, 160, 149, nameof(SystemSettings.BasicFounderMonthlyPrice))]
    public void Basico_cuesta_menos_que_Pro_tambien_de_fundador(
        int basic, int pro, int basicFounder, int proFounder, string field)
    {
        var settings = new SystemSettings
        {
            BasicMonthlyPrice = basic,
            ProMonthlyPrice = pro,
            BasicFounderMonthlyPrice = basicFounder,
            ProFounderMonthlyPrice = proFounder,
        };

        var errors = SystemSettingsRules.Validate(settings, assignedFounders: 0);

        Assert.Contains(errors, e => e.Field == field && e.Message == "Básico debe costar menos que Pro.");
    }

    [Fact]
    public void El_anual_de_Basico_tambien_cuesta_menos_que_el_de_Pro()
    {
        var settings = new SystemSettings { BasicAnnualPrice = 1990m, BasicFounderAnnualPrice = 1490m };

        var errors = SystemSettingsRules.Validate(settings, assignedFounders: 0);

        Assert.Equal(
            [nameof(SystemSettings.BasicAnnualPrice), nameof(SystemSettings.BasicFounderAnnualPrice)],
            errors.Select(e => e.Field).Order());
    }

    [Fact]
    public void Los_cambios_listan_solo_lo_que_cambio_y_cuales_estan_en_los_terminos()
    {
        var before = new SystemSettings();
        var after = new SystemSettings();
        SystemSettingsRules.CopyValues(before, after);
        after.BasicFounderAnnualPrice = 650m;
        after.ProMonthlyPrice = 249m;
        after.VatRate = 0.08m;

        var changes = SystemSettingsRules.Changes(before, after);

        Assert.Equal(
            [
                new SettingsChange(nameof(SystemSettings.BasicFounderAnnualPrice), "690", "650"),
                new SettingsChange(nameof(SystemSettings.ProMonthlyPrice), "199", "249"),
                new SettingsChange(nameof(SystemSettings.VatRate), "0.16", "0.08"),
            ],
            changes);
        Assert.Contains(nameof(SystemSettings.ProMonthlyPrice), SystemSettingsRules.InTerms);
        Assert.Contains(nameof(SystemSettings.BasicMonthlyPrice), SystemSettingsRules.InTerms);
        Assert.Equal(15, SystemSettingsRules.InTerms.Count);
        Assert.DoesNotContain(nameof(SystemSettings.VatRate), SystemSettingsRules.InTerms);
        Assert.All(SystemSettingsRules.Fields, f => SystemSettingsRules.ValueOf(before, f));
    }

    [Fact]
    public void La_bitacora_exige_actor_y_resumen_y_limita_el_motivo()
    {
        var now = Mx(2026, 11, 24, 10);
        var entry = AuditLog.Create("user-1", "admin@prueba.mx", AuditAction.ProDaysGranted, 7, "Regaló 10 días", "  Compensación ", null, now);

        Assert.Equal("Compensación", entry.Note);
        Assert.Throws<ArgumentException>(() => AuditLog.Create(" ", "a@b.mx", AuditAction.ProDaysGranted, 7, "x", null, null, now));
        Assert.Throws<ArgumentException>(() =>
            AuditLog.Create("user-1", "a@b.mx", AuditAction.ProDaysGranted, 7, "x", new string('a', AuditLog.NoteMaxLength + 1), null, now));
    }
}
