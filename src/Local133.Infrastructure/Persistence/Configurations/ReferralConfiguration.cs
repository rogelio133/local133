using Local133.Domain.Merchants;
using Local133.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class ReferralConfiguration : IEntityTypeConfiguration<Referral>
{
    public void Configure(EntityTypeBuilder<Referral> builder)
    {
        builder.ToTable("Referrals", t =>
            t.HasCheckConstraint("CK_Referrals_NotSelf", "[ReferrerMerchantId] <> [ReferredMerchantId]"));

        builder.Property(r => r.Code).HasMaxLength(ReferralCode.Length).IsUnicode(false).IsFixedLength();
        builder.Property(r => r.SkippedReason).HasConversion<string>().HasMaxLength(30);

        // Restrict en los dos lados: el historial de referidos no se borra en cascada.
        builder.HasOne<Merchant>().WithMany().HasForeignKey(r => r.ReferrerMerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(r => r.ReferredMerchantId).OnDelete(DeleteBehavior.Restrict);

        // Cada vendedor solo puede ser referido una vez.
        builder.HasIndex(r => r.ReferredMerchantId).IsUnique();

        // «Invita y gana» y el tope de premios en 12 meses.
        builder.HasIndex(r => new { r.ReferrerMerchantId, r.RewardedAt });
    }
}
