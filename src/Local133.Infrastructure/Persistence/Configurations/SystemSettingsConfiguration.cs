using Local133.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class SystemSettingsConfiguration : IEntityTypeConfiguration<SystemSettings>
{
    public void Configure(EntityTypeBuilder<SystemSettings> builder)
    {
        builder.ToTable("SystemSettings", t =>
        {
            t.HasCheckConstraint("CK_SystemSettings_VatRate", "[VatRate] >= 0 AND [VatRate] < 1");
            t.HasCheckConstraint("CK_SystemSettings_GraceDays", "[GraceDays] >= 0");
            t.HasCheckConstraint("CK_SystemSettings_SaleQrExpirationSeconds", "[SaleQrExpirationSeconds] = 0 OR [SaleQrExpirationSeconds] >= 360");
            t.HasCheckConstraint("CK_SystemSettings_Prices",
                "[BasicMonthlyPrice] > 0 AND [BasicAnnualPrice] > 0 AND [BasicFounderMonthlyPrice] > 0 AND [BasicFounderAnnualPrice] > 0 " +
                "AND [ProMonthlyPrice] > 0 AND [ProAnnualPrice] > 0 AND [ProFounderMonthlyPrice] > 0 AND [ProFounderAnnualPrice] > 0");
            t.HasCheckConstraint("CK_SystemSettings_TrialDays", "[TrialDays] > 0");
            t.HasCheckConstraint("CK_SystemSettings_Limits",
                "[FounderSlots] >= 0 AND [FairUseMonthlyPaid] >= 0 AND [ReferralRewardDays] >= 0 AND [ReferralRewardsPerYear] >= 0");
            t.HasCheckConstraint("CK_SystemSettings_Rates",
                "[ReferralWelcomeDiscountRate] >= 0 AND [ReferralWelcomeDiscountRate] < 1 " +
                "AND [ReferenceTerminalFeeRate] >= 0 AND [ReferenceTerminalFeeRate] < 1");
        });

        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.VatRate).HasPrecision(5, 4);
        builder.Property(s => s.BasicMonthlyPrice).HasPrecision(18, 2);
        builder.Property(s => s.BasicAnnualPrice).HasPrecision(18, 2);
        builder.Property(s => s.BasicFounderMonthlyPrice).HasPrecision(18, 2);
        builder.Property(s => s.BasicFounderAnnualPrice).HasPrecision(18, 2);
        builder.Property(s => s.ProMonthlyPrice).HasPrecision(18, 2);
        builder.Property(s => s.ProAnnualPrice).HasPrecision(18, 2);
        builder.Property(s => s.ProFounderMonthlyPrice).HasPrecision(18, 2);
        builder.Property(s => s.ProFounderAnnualPrice).HasPrecision(18, 2);
        builder.Property(s => s.ReferralWelcomeDiscountRate).HasPrecision(5, 4);
        builder.Property(s => s.ReferenceTerminalFeeRate).HasPrecision(5, 4);

        builder.HasData(new SystemSettings
        {
            Id = SystemSettings.SingletonId,
            VatRate = SystemSettings.DefaultVatRate,
            GraceDays = SystemSettings.DefaultGraceDays,
            SaleQrExpirationSeconds = SystemSettings.DefaultSaleQrExpirationSeconds,
            BasicMonthlyPrice = SystemSettings.DefaultBasicMonthlyPrice,
            BasicAnnualPrice = SystemSettings.DefaultBasicAnnualPrice,
            BasicFounderMonthlyPrice = SystemSettings.DefaultBasicFounderMonthlyPrice,
            BasicFounderAnnualPrice = SystemSettings.DefaultBasicFounderAnnualPrice,
            ProMonthlyPrice = SystemSettings.DefaultProMonthlyPrice,
            ProAnnualPrice = SystemSettings.DefaultProAnnualPrice,
            ProFounderMonthlyPrice = SystemSettings.DefaultProFounderMonthlyPrice,
            ProFounderAnnualPrice = SystemSettings.DefaultProFounderAnnualPrice,
            FounderSlots = SystemSettings.DefaultFounderSlots,
            TrialDays = SystemSettings.DefaultTrialDays,
            FairUseMonthlyPaid = SystemSettings.DefaultFairUseMonthlyPaid,
            ReferralRewardDays = SystemSettings.DefaultReferralRewardDays,
            ReferralRewardsPerYear = SystemSettings.DefaultReferralRewardsPerYear,
            ReferralWelcomeDiscountRate = SystemSettings.DefaultReferralWelcomeDiscountRate,
            ReferenceTerminalFeeRate = SystemSettings.DefaultReferenceTerminalFeeRate,
        });
    }
}
