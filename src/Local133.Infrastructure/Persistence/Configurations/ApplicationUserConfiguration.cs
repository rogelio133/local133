using Local133.Domain.Legal;
using Local133.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

/// <summary>Columnas propias de <c>AspNetUsers</c> (aceptación de documentos legales).</summary>
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.TermsVersion).HasMaxLength(LegalDocuments.VersionMaxLength);
        builder.Property(u => u.PrivacyNoticeVersion).HasMaxLength(LegalDocuments.VersionMaxLength);
    }
}
