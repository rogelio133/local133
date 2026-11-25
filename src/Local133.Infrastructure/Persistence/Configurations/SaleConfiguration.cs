using Local133.Domain.Merchants;
using Local133.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("Sales", t => t.HasCheckConstraint("CK_Sales_Amount", "[Amount] >= 0"));

        builder.Property(s => s.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Amount).HasPrecision(18, 2);
        builder.Property(s => s.Concept).HasMaxLength(Sale.ConceptMaxLength);
        builder.Property(s => s.PeyQrId).HasMaxLength(Sale.PeyIdMaxLength).IsUnicode(false);
        builder.Property(s => s.PeyTransactionId).HasMaxLength(Sale.PeyIdMaxLength).IsUnicode(false);
        builder.Property(s => s.PeyCodiId).HasMaxLength(Sale.PeyCodiIdMaxLength).IsUnicode(false);
        builder.Property(s => s.PhoneNumber).HasMaxLength(PhoneNumber.Length).IsUnicode(false);
        builder.Property(s => s.ReceiptToken).HasMaxLength(Sale.ReceiptTokenLength).IsUnicode(false).IsFixedLength();
        builder.Ignore(s => s.NumRef);
        builder.Ignore(s => s.HasPaymentRequest);
        builder.Ignore(s => s.HasReceipt);

        // Restrict: el historial financiero del vendedor no se borra en cascada.
        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(s => s.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Folio consecutivo por vendedor; el índice frena dos ventas simultáneas con el mismo folio.
        builder.HasIndex(s => new { s.MerchantId, s.Folio }).IsUnique();
        // El webhook codi.qr.payment_received y la conciliación buscan por el IDC del QR.
        builder.HasIndex(s => s.PeyQrId).IsUnique().HasFilter("[PeyQrId] IS NOT NULL");
        // El webhook codi.pn.payment_received busca el cobro por celular por la transacción de Pey.
        builder.HasIndex(s => s.PeyTransactionId).IsUnique().HasFilter("[PeyTransactionId] IS NOT NULL");
        // Recibo público: se busca por su token (secreto, único).
        builder.HasIndex(s => s.ReceiptToken).IsUnique();
        // Historial del vendedor: sus ventas de la más reciente a la más antigua, filtradas por fecha.
        builder.HasIndex(s => new { s.MerchantId, s.CreatedAt });

        // Transacciones globales del admin (6.4).
        builder.HasIndex(s => s.CreatedAt);
        // Límite del gratuito (5.5): cobros concretados del mes (ventas con PaidAt en el mes).
        builder.HasIndex(s => new { s.MerchantId, s.PaidAt });
        // Job de expiración (pendientes con el QR vencido) y conciliación (sin pagar, por vencimiento).
        builder.HasIndex(s => new { s.Status, s.ExpiresAt });
    }
}
