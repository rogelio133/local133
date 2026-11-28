using Local133.Domain.Merchants;
using Local133.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    /// <summary>
    /// Intercalación del nombre: sin distinguir mayúsculas (sí acentos), explícita para que el índice único no dependa de
    /// la intercalación del servidor.
    /// </summary>
    public const string NameCollation = "Latin1_General_100_CI_AS";

    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("ProductCategories", t => t.HasCheckConstraint("CK_ProductCategories_SortOrder", "[SortOrder] >= 0"));

        builder.Property(c => c.Name).HasMaxLength(ProductCategory.NameMaxLength).UseCollation(NameCollation);

        // Restrict: igual que los productos, no se borran en cascada con el vendedor.
        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(c => c.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nombre único por vendedor sin importar mayúsculas (la intercalación de la columna es CI).
        builder.HasIndex(c => new { c.MerchantId, c.Name }).IsUnique();

        // Lista del vendedor en su orden.
        builder.HasIndex(c => new { c.MerchantId, c.SortOrder });
    }
}
