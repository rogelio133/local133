using Local133.Domain.Merchants;
using Local133.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", t => t.HasCheckConstraint("CK_Products_Price", "[Price] > 0"));

        builder.Property(p => p.Name).HasMaxLength(Product.NameMaxLength);
        builder.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
        builder.Property(p => p.Price).HasPrecision(18, 2);
        builder.Property(p => p.PhotoKey).HasMaxLength(Product.PhotoKeyMaxLength).IsUnicode(false);

        // Restrict: los productos (y su historial en ventas) no se borran en cascada con el vendedor.
        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(p => p.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Borrar la categoría deja sus productos sin categoría.
        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Catálogo del vendedor: vigentes ordenados por nombre (lista paginada y búsqueda).
        builder.HasIndex(p => new { p.MerchantId, p.IsDeleted, p.Name });
    }
}
