using Local133.Domain.Merchants;
using Local133.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("Merchants");

        builder.Property(m => m.UserId).HasMaxLength(450);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.BusinessName).HasMaxLength(Merchant.BusinessNameMaxLength);
        builder.Property(m => m.OwnerName).HasMaxLength(PersonName.MaxLength);
        // «Mi información» (4.1).
        builder.Property(m => m.Address).HasMaxLength(MerchantAddress.MaxLength);
        // Concepto por defecto (ajustes 2026-11-26).
        builder.Property(m => m.DefaultConcept).HasMaxLength(DefaultConcept.MaxLength);
        builder.Property(m => m.LogoKey).HasMaxLength(Merchant.ImageKeyMaxLength).IsUnicode(false);
        builder.Property(m => m.ShopPhotoKey).HasMaxLength(Merchant.ImageKeyMaxLength).IsUnicode(false);
        // Código de invitación (5.8): único; la migración lo genera para los vendedores anteriores.
        builder.Property(m => m.ReferralCode).HasMaxLength(ReferralCode.Length).IsUnicode(false).IsFixedLength();
        builder.HasIndex(m => m.ReferralCode).IsUnique();
        // Catálogo público (5.10): slug único entre los que tienen uno; se conserva al dejar de publicar.
        builder.Property(m => m.PublicSlug).HasMaxLength(PublicSlug.MaxLength).IsUnicode(false);
        builder.HasIndex(m => m.PublicSlug).IsUnique().HasFilter("[PublicSlug] IS NOT NULL");

        // 1:1 con el usuario. Restrict: borrar un usuario no debe llevarse el historial financiero del vendedor.
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<Merchant>(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.UserId).IsUnique();
    }
}
