using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("BankAccounts");

        builder.Property(b => b.HolderName).HasMaxLength(BankAccount.HolderNameMaxLength);
        builder.Property(b => b.Clabe).HasMaxLength(Clabe.Length).IsUnicode(false).IsFixedLength();
        builder.Property(b => b.Alias).HasMaxLength(BankAccount.AliasMaxLength);
        builder.Property(b => b.Entity).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.PeyBankAccountId).HasMaxLength(BankAccount.PeyIdMaxLength).IsUnicode(false);
        builder.Property(b => b.CodiStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.ErrorMessage).HasMaxLength(BankAccount.ErrorMessageMaxLength);

        // Restrict: el historial financiero del vendedor no se borra en cascada.
        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(b => b.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Una cuenta por vendedor (el cambio de cuenta con historial llega en la Fase 4). También frena un doble envío.
        builder.HasIndex(b => b.MerchantId).IsUnique();
        // El webhook y el sondeo buscan por el id de Pey.
        builder.HasIndex(b => b.PeyBankAccountId).IsUnique();
    }
}
