using Local133.Domain.Merchants;
using Local133.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(40);
        builder.Property(n => n.Title).HasMaxLength(Notification.TitleMaxLength);
        builder.Property(n => n.Body).HasMaxLength(Notification.BodyMaxLength);
        builder.Property(n => n.Url).HasMaxLength(Notification.UrlMaxLength);
        builder.Property(n => n.DedupKey).HasMaxLength(Notification.DedupKeyMaxLength).IsUnicode(false);

        // Restrict: los avisos no se borran en cascada con el vendedor.
        builder.HasOne<Merchant>().WithMany().HasForeignKey(n => n.MerchantId).OnDelete(DeleteBehavior.Restrict);

        // Idempotencia de jobs y webhooks: una llave por vendedor.
        builder.HasIndex(n => new { n.MerchantId, n.DedupKey }).IsUnique();
        // Lista de avisos del vendedor (5.4).
        builder.HasIndex(n => new { n.MerchantId, n.CreatedAt });
    }
}
