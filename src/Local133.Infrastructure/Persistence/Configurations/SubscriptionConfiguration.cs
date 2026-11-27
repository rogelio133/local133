using Local133.Domain.Merchants;
using Local133.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions", t =>
        {
            t.HasCheckConstraint("CK_Subscriptions_ProUntil", "[ProUntil] > [TrialStartedAt]");
            t.HasCheckConstraint("CK_Subscriptions_FounderNumber", "[FounderNumber] IS NULL OR [FounderNumber] > 0");
            t.HasCheckConstraint("CK_Subscriptions_LockedAnnualPrice", "[LockedAnnualPrice] IS NULL OR [LockedAnnualPrice] > 0");
        });

        builder.Property(s => s.LastPeriodKind).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.LastPackage).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.LastPlan).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.LockedAnnualPlan).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.LockedAnnualPrice).HasPrecision(18, 2);

        // Una por vendedor: la llave alterna es también el índice único que impide una segunda prueba (dos
        // activaciones simultáneas: webhook y sondeo). Restrict: el historial no se borra en cascada.
        builder.HasAlternateKey(s => s.MerchantId);
        builder.HasOne<Merchant>().WithOne().HasForeignKey<Subscription>(s => s.MerchantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Periods)
            .WithOne()
            .HasForeignKey(p => p.MerchantId)
            .HasPrincipalKey(s => s.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Lugares de fundador: nunca dos con el mismo número (5.6 lo asigna dentro de una transacción).
        builder.HasIndex(s => s.FounderNumber).IsUnique().HasFilter("[FounderNumber] IS NOT NULL");

        // Job diario de recordatorios (5.7): suscripciones que vencen pronto o vencieron hace poco.
        builder.HasIndex(s => s.ProUntil);
    }
}

public class SubscriptionPeriodConfiguration : IEntityTypeConfiguration<SubscriptionPeriod>
{
    public void Configure(EntityTypeBuilder<SubscriptionPeriod> builder)
    {
        builder.ToTable("SubscriptionPeriods", t => t.HasCheckConstraint("CK_SubscriptionPeriods_Range", "[EndsAt] > [StartsAt]"));

        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Plan).HasConversion<string>().HasMaxLength(30);

        // Periodo pagado ⇒ el cargo que lo pagó (Restrict: el historial no se borra en cascada).
        builder.HasOne<SubscriptionCharge>().WithMany().HasForeignKey(p => p.SubscriptionChargeId).OnDelete(DeleteBehavior.Restrict);

        // Premio de referido ⇒ el referido que lo ganó (5.8): a lo más un premio por referido.
        builder.HasOne<Referral>().WithMany().HasForeignKey(p => p.ReferralId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.ReferralId).IsUnique().HasFilter("[ReferralId] IS NOT NULL");

        // Estado del plan y jobs: periodos del vendedor por fin.
        builder.HasIndex(p => new { p.MerchantId, p.EndsAt });
    }
}
