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
        });

        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.VatRate).HasPrecision(5, 4);

        builder.HasData(new SystemSettings
        {
            Id = SystemSettings.SingletonId,
            VatRate = SystemSettings.DefaultVatRate,
            GraceDays = SystemSettings.DefaultGraceDays,
            SaleQrExpirationSeconds = SystemSettings.DefaultSaleQrExpirationSeconds,
        });
    }
}
