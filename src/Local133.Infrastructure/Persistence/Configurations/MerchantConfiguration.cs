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

        // 1:1 con el usuario. Restrict: borrar un usuario no debe llevarse el historial financiero del vendedor.
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<Merchant>(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.UserId).IsUnique();
    }
}
