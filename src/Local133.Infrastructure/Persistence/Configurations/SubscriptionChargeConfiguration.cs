using Local133.Domain.Merchants;
using Local133.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class SubscriptionChargeConfiguration : IEntityTypeConfiguration<SubscriptionCharge>
{
    public const int PeyIdMaxLength = 64;
    public const int UrlMaxLength = 2048;

    public void Configure(EntityTypeBuilder<SubscriptionCharge> builder)
    {
        builder.ToTable("SubscriptionCharges", t =>
        {
            t.HasCheckConstraint("CK_SubscriptionCharges_Amount", "[Amount] > 0 AND [Amount] = [ListPrice] - [DiscountAmount]");
            t.HasCheckConstraint("CK_SubscriptionCharges_Months", "[Months] > 0");
        });

        builder.Property(c => c.Package).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.DiscountReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.Origin).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.ListPrice).HasPrecision(18, 2);
        builder.Property(c => c.DiscountAmount).HasPrecision(18, 2);
        builder.Property(c => c.Amount).HasPrecision(18, 2);
        builder.Property(c => c.PeyPaymentLinkId).HasMaxLength(PeyIdMaxLength).IsUnicode(false);
        builder.Property(c => c.Url).HasMaxLength(UrlMaxLength);
        builder.Property(c => c.FailureReason).HasMaxLength(SubscriptionCharge.FailureReasonMaxLength);
        builder.Ignore(c => c.ClientReferenceId);

        // Restrict: el historial de cobros de la suscripción no se borra en cascada.
        builder.HasOne<Merchant>().WithMany().HasForeignKey(c => c.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(c => c.Payments).WithOne().HasForeignKey(p => p.SubscriptionChargeId).OnDelete(DeleteBehavior.Restrict);

        // El webhook y la conciliación buscan el cargo por el id del link.
        builder.HasIndex(c => c.PeyPaymentLinkId).IsUnique().HasFilter("[PeyPaymentLinkId] IS NOT NULL");
        // «Mi plan»: cargos del vendedor por fecha.
        builder.HasIndex(c => new { c.MerchantId, c.CreatedAt });
        // Conciliación: cargos sin abonar.
        builder.HasIndex(c => new { c.Status, c.CreatedAt });

        // Ingresos por suscripción del tablero del admin (6.4).
        builder.HasIndex(c => new { c.Status, c.PaidAt });
    }
}

public class SubscriptionPaymentConfiguration : IEntityTypeConfiguration<SubscriptionPayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.ToTable("SubscriptionPayments");

        builder.Property(p => p.PeyTransactionId).HasMaxLength(SubscriptionChargeConfiguration.PeyIdMaxLength).IsUnicode(false);
        builder.Property(p => p.PaymentMethod).HasMaxLength(SubscriptionPayment.PaymentMethodMaxLength).IsUnicode(false);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Base).HasPrecision(18, 2);
        builder.Property(p => p.Fee).HasPrecision(18, 2);
        builder.Property(p => p.ApplicationFee).HasPrecision(18, 2);

        builder.HasOne<Merchant>().WithMany().HasForeignKey(p => p.MerchantId).OnDelete(DeleteBehavior.Restrict);

        // Idempotencia: la misma transacción (webhook repetido, conciliación) nunca se registra dos veces.
        builder.HasIndex(p => p.PeyTransactionId).IsUnique();

        // Pagos de la suscripción en las transacciones globales del admin (6.4).
        builder.HasIndex(p => p.PaidAt);
    }
}
