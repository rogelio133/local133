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
        builder.Ignore(s => s.NumRef);

        // Restrict: el historial financiero del vendedor no se borra en cascada.
        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(s => s.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Folio consecutivo por vendedor; el índice frena dos ventas simultáneas con el mismo folio.
        builder.HasIndex(s => new { s.MerchantId, s.Folio }).IsUnique();
        // El webhook codi.qr.payment_received y la conciliación buscan por el IDC del QR.
        builder.HasIndex(s => s.PeyQrId).IsUnique().HasFilter("[PeyQrId] IS NOT NULL");
    }
}
