using Local133.Domain.Merchants;
using Local133.Domain.Payments;
using Local133.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", t => t.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0"));

        builder.Property(p => p.PeyTransactionId).HasMaxLength(Payment.PeyIdMaxLength).IsUnicode(false);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.TrackingKey).HasMaxLength(Payment.TrackingKeyMaxLength).IsUnicode(false);

        // Restrict: el historial financiero no se borra en cascada.
        builder.HasOne<Merchant>().WithMany().HasForeignKey(p => p.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Sale>().WithMany().HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Restrict);

        // Idempotencia por transacción: el mismo pago (webhook repetido, conciliación) nunca se registra dos veces.
        builder.HasIndex(p => p.PeyTransactionId).IsUnique();
        // Historial y resumen del día: pagos del vendedor por fecha.
        builder.HasIndex(p => new { p.MerchantId, p.PaidAt });

        // GMV global del tablero del admin (6.4).
        builder.HasIndex(p => p.PaidAt).IncludeProperties(p => new { p.Amount, p.SaleId });
    }
}
