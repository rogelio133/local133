using Local133.Domain.Merchants;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using static Local133.UnitTests.Subscriptions.PlanStatusTests;

namespace Local133.UnitTests.Subscriptions;

/// <summary>5.8: referidos, premio de quien invita (tope de 12 en 12 meses) y descuento de bienvenida.</summary>
public class ReferralTests
{
    private const int GraceDays = 5;
    private static readonly SystemSettings Settings = new();

    // --- Referido --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Califica_una_sola_vez()
    {
        var referral = Referral.Create(1, 2, "ABC234", Mx(2026, 11, 1, 10));

        Assert.False(referral.IsPendingReward);
        Assert.True(referral.Qualify(Mx(2026, 11, 20, 10)));
        Assert.False(referral.Qualify(Mx(2026, 12, 20, 10)));
        Assert.Equal(Mx(2026, 11, 20, 10), referral.QualifiedAt);
        Assert.True(referral.IsPendingReward);
    }

    [Fact]
    public void El_premio_o_la_omision_cierran_el_pendiente()
    {
        var rewarded = Referral.Create(1, 2, "ABC234", Mx(2026, 11, 1, 10));
        rewarded.Qualify(Mx(2026, 11, 20, 10));
        rewarded.MarkRewarded(Mx(2026, 11, 20, 10));

        var skipped = Referral.Create(1, 3, "ABC234", Mx(2026, 11, 1, 10));
        skipped.Qualify(Mx(2026, 11, 20, 10));
        skipped.Skip(ReferralSkipReason.RewardCap);

        Assert.False(rewarded.IsPendingReward);
        Assert.False(skipped.IsPendingReward);
        Assert.Throws<InvalidOperationException>(() => rewarded.MarkRewarded(Mx(2026, 11, 21, 10)));
        Assert.Throws<InvalidOperationException>(() => skipped.MarkRewarded(Mx(2026, 11, 21, 10)));
    }

    [Fact]
    public void Sin_calificar_no_se_premia()
    {
        var referral = Referral.Create(1, 2, "ABC234", Mx(2026, 11, 1, 10));

        Assert.Throws<InvalidOperationException>(() => referral.MarkRewarded(Mx(2026, 11, 20, 10)));
    }

    [Fact]
    public void Nadie_se_refiere_a_si_mismo_ni_con_un_codigo_invalido()
    {
        Assert.Throws<ArgumentException>(() => Referral.Create(1, 1, "ABC234", Mx(2026, 11, 1, 10)));
        Assert.Throws<ArgumentException>(() => Referral.Create(1, 2, "abc", Mx(2026, 11, 1, 10)));
    }

    // --- Política del premio --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(MerchantStatus.Active, true, 0, ReferralRewardDecision.Grant, null)]
    [InlineData(MerchantStatus.Active, true, 11, ReferralRewardDecision.Grant, null)]
    [InlineData(MerchantStatus.Active, true, 12, ReferralRewardDecision.Skip, ReferralSkipReason.RewardCap)]
    [InlineData(MerchantStatus.Suspended, true, 3, ReferralRewardDecision.Grant, null)]
    [InlineData(MerchantStatus.Disabled, true, 0, ReferralRewardDecision.Skip, ReferralSkipReason.ReferrerDisabled)]
    [InlineData(MerchantStatus.BankAccountInReview, false, 0, ReferralRewardDecision.Pending, null)]
    [InlineData(MerchantStatus.PendingBankAccount, false, 20, ReferralRewardDecision.Pending, null)]
    public void Decide_el_premio(
        MerchantStatus status, bool hasSubscription, int rewards, ReferralRewardDecision decision, ReferralSkipReason? reason)
    {
        Assert.Equal((decision, reason), ReferralRewardPolicy.Decide(status, hasSubscription, rewards, Settings));
    }

    [Fact]
    public void La_ventana_del_tope_es_de_12_meses_en_hora_de_Mexico()
    {
        Assert.Equal(Mx(2025, 11, 20, 0), ReferralRewardPolicy.WindowStart(Mx(2026, 11, 20, 0)));
        Assert.Equal(Mx(2025, 2, 28, 10), ReferralRewardPolicy.WindowStart(Mx(2026, 2, 28, 10)));
    }

    // --- Premio como periodo --------------------------------------------------------------------------------------------

    [Fact]
    public void El_premio_se_encadena_y_lleva_el_referido()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 1, 10), 30);

        var period = subscription.AddDaysPeriod(SubscriptionPeriodKind.ReferralReward, SubscriptionPlan.Pro, 10, Mx(2026, 11, 20, 10), GraceDays, referralId: 9);

        Assert.Equal(9, period.ReferralId);
        Assert.Equal(Mx(2026, 12, 1, 10), period.StartsAt);
        Assert.Equal(Mx(2026, 12, 11, 10), period.EndsAt);
        Assert.Equal(SubscriptionPeriodKind.ReferralReward, subscription.LastPeriodKind);
    }

    [Fact]
    public void Solo_el_premio_de_referido_lleva_referido()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 1, 10), 30);

        Assert.Throws<ArgumentException>(() =>
            subscription.AddDaysPeriod(SubscriptionPeriodKind.AdminGrant, SubscriptionPlan.Pro, 10, Mx(2026, 11, 20, 10), GraceDays, referralId: 9));
    }

    // --- Descuento de bienvenida ----------------------------------------------------------------------------------------

    [Fact]
    public void El_descuento_de_bienvenida_va_en_cualquier_paquete()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 1, 10), 30);
        var status = subscription.StatusAt(Mx(2026, 11, 20, 10), GraceDays);

        var (month, _) = subscription.QuoteFor(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings, status, false, welcomeDiscount: true);
        var (annual, _) = subscription.QuoteFor(SubscriptionPlan.Pro, SubscriptionPackage.Annual, Settings, status, false, welcomeDiscount: true);
        var (plain, _) = subscription.QuoteFor(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings, status, false);

        Assert.Equal(139.30m, month.Amount);
        Assert.Equal(1930.30m, annual.Amount);
        Assert.Equal(SubscriptionDiscountReason.ReferralWelcome, annual.DiscountReason);
        Assert.Equal(199m, plain.Amount);
        Assert.Null(plain.DiscountReason);
    }

    [Fact]
    public void El_descuento_no_cambia_los_meses_de_regalo_del_anual()
    {
        var subscription = Subscription.StartTrial(1, Mx(2026, 11, 1, 10), 30);

        var offers = SubscriptionOffers.For(subscription, SubscriptionPlan.Pro, Settings, Mx(2026, 11, 20, 10), founderSlotAvailable: false, welcomeDiscount: true);

        var annual = offers.Single(o => o.Package == SubscriptionPackage.Annual);
        Assert.Equal(2, annual.GiftMonths);
        Assert.Equal(59.70m, annual.Quote.DiscountAmount);
        Assert.Equal(796m, offers.Single(o => o.Package == SubscriptionPackage.SixMonths).AnnualUpgradeDifference);
    }
}
