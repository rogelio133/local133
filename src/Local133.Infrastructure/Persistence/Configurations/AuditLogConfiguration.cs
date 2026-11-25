using Local133.Domain.Audit;
using Local133.Domain.Merchants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.Property(a => a.ActorUserId).HasMaxLength(AuditLog.ActorUserIdMaxLength);
        builder.Property(a => a.ActorEmail).HasMaxLength(AuditLog.ActorEmailMaxLength);
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(40);
        builder.Property(a => a.Summary).HasMaxLength(AuditLog.SummaryMaxLength);
        builder.Property(a => a.Note).HasMaxLength(AuditLog.NoteMaxLength);
        builder.Property(a => a.Details);

        // Restrict: la bitácora no se borra en cascada con el vendedor.
        builder.HasOne<Merchant>().WithMany().HasForeignKey(a => a.MerchantId).OnDelete(DeleteBehavior.Restrict);

        // Historial de cambios de un vendedor (pestaña Plan) y de los parámetros globales.
        builder.HasIndex(a => new { a.MerchantId, a.OccurredAt });
        builder.HasIndex(a => new { a.Action, a.OccurredAt });
    }
}
