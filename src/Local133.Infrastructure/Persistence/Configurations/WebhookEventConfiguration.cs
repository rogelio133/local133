using Local133.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("WebhookEvents");

        builder.Property(e => e.WebhookId).HasMaxLength(WebhookEvent.WebhookIdMaxLength).IsUnicode(false);
        builder.Property(e => e.EventType).HasMaxLength(WebhookEvent.EventTypeMaxLength).IsUnicode(false);
        builder.Property(e => e.Payload);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Error).HasMaxLength(WebhookEvent.ErrorMaxLength);

        // Standard Webhooks: el mismo webhook-id es la misma entrega (Pey lo repite al reintentar).
        builder.HasIndex(e => e.WebhookId).IsUnique();
        // Bitácora del admin (Fase 6): por fecha y estatus.
        builder.HasIndex(e => new { e.Status, e.ReceivedAt });
    }
}
