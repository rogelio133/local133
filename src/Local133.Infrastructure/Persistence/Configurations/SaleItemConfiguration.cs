using Local133.Domain.Products;
using Local133.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("SaleItems", t =>
        {
            t.HasCheckConstraint("CK_SaleItems_Quantity", $"[Quantity] BETWEEN 1 AND {SaleItem.MaxQuantity}");
            t.HasCheckConstraint("CK_SaleItems_UnitPrice", "[UnitPrice] > 0");
        });

        builder.Property(i => i.Name).HasMaxLength(Product.NameMaxLength);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
        builder.Property(i => i.Amount).HasPrecision(18, 2);

        // Cascada: los productos son parte de la venta (si Pey rechaza el cobro, la venta se borra con ellos).
        // Sin FK al vendedor (MerchantId + filtro multi-tenant): la venta ya lo referencia y se evitan dos rutas de cascada.
        builder.HasOne<Sale>()
            .WithMany(s => s.Items)
            .HasForeignKey(i => i.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: los productos solo se dan de baja de forma lógica; la referencia nunca se pierde.
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
