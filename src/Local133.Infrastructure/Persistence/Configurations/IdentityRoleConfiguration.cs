using Local133.Application.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Local133.Infrastructure.Persistence.Configurations;

/// <summary>Siembra los roles con valores fijos para que la migración sea determinista.</summary>
public class IdentityRoleConfiguration : IEntityTypeConfiguration<IdentityRole>
{
    public const string MerchantRoleId = "6b0c3f0e-4f7e-4a5b-9a51-0d1f6f3e2a01";
    public const string AdminRoleId = "6b0c3f0e-4f7e-4a5b-9a51-0d1f6f3e2a02";

    public void Configure(EntityTypeBuilder<IdentityRole> builder) =>
        builder.HasData(
            new IdentityRole
            {
                Id = MerchantRoleId,
                Name = Roles.Merchant,
                NormalizedName = Roles.Merchant.ToUpperInvariant(),
                ConcurrencyStamp = "b3f6a9d2-1c11-4a1e-8d0a-merchant0001",
            },
            new IdentityRole
            {
                Id = AdminRoleId,
                Name = Roles.Admin,
                NormalizedName = Roles.Admin.ToUpperInvariant(),
                ConcurrencyStamp = "b3f6a9d2-1c11-4a1e-8d0a-admin0000001",
            });
}
