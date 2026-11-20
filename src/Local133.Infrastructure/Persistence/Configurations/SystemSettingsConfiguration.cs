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
                "[MonthlyPrice] > 0 AND [AnnualPrice] > 0 AND [FounderMonthlyPrice] > 0 AND [FounderAnnualPrice] > 0");
            t.HasCheckConstraint("CK_SystemSettings_TrialDays", "[TrialDays] > 0");
            t.HasCheckConstraint("CK_SystemSettings_Limits",
                "[FounderSlots] >= 0 AND [FreeMonthlyPaidLimit] >= 0 AND [FreeMonthlyGeneratedLimit] >= 0 " +
                "AND [FreeHistoryDays] >= 0 AND [ProFairUseMonthlyPaid] >= 0 AND [ReferralRewardDays] >= 0 AND [ReferralRewardsPerYear] >= 0");
            t.HasCheckConstraint("CK_SystemSettings_Rates",
                "[ReferralWelcomeDiscountRate] >= 0 AND [ReferralWelcomeDiscountRate] < 1 " +
                "AND [ReferenceTerminalFeeRate] >= 0 AND [ReferenceTerminalFeeRate] < 1");
        });

        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.VatRate).HasPrecision(5, 4);
        builder.Property(s => s.MonthlyPrice).HasPrecision(18, 2);
        builder.Property(s => s.AnnualPrice).HasPrecision(18, 2);
        builder.Property(s => s.FounderMonthlyPrice).HasPrecision(18, 2);
        builder.Property(s => s.FounderAnnualPrice).HasPrecision(18, 2);
        builder.Property(s => s.ReferralWelcomeDiscountRate).HasPrecision(5, 4);
        builder.Property(s => s.ReferenceTerminalFeeRate).HasPrecision(5, 4);

        builder.HasData(new SystemSettings
        {
            Id = SystemSettings.SingletonId,
            VatRate = SystemSettings.DefaultVatRate,
            GraceDays = SystemSettings.DefaultGraceDays,
            SaleQrExpirationSeconds = SystemSettings.DefaultSaleQrExpirationSeconds,
            MonthlyPrice = SystemSettings.DefaultMonthlyPrice,
            AnnualPrice = SystemSettings.DefaultAnnualPrice,
            FounderMonthlyPrice = SystemSettings.DefaultFounderMonthlyPrice,
            FounderAnnualPrice = SystemSettings.DefaultFounderAnnualPrice,
            FounderSlots = SystemSettings.DefaultFounderSlots,
            TrialDays = SystemSettings.DefaultTrialDays,
            FreeMonthlyPaidLimit = SystemSettings.DefaultFreeMonthlyPaidLimit,
            FreeMonthlyGeneratedLimit = SystemSettings.DefaultFreeMonthlyGeneratedLimit,
            FreeHistoryDays = SystemSettings.DefaultFreeHistoryDays,
            ProFairUseMonthlyPaid = SystemSettings.DefaultProFairUseMonthlyPaid,
            ReferralRewardDays = SystemSettings.DefaultReferralRewardDays,
            ReferralRewardsPerYear = SystemSettings.DefaultReferralRewardsPerYear,
            ReferralWelcomeDiscountRate = SystemSettings.DefaultReferralWelcomeDiscountRate,
            ReferenceTerminalFeeRate = SystemSettings.DefaultReferenceTerminalFeeRate,
        });
    }
}
