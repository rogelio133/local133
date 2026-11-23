using Local133.Application.Auth;
using Local133.Domain.Legal;
using Local133.Domain.Merchants;
using Local133.Infrastructure.Identity;
using Local133.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace Local133.Web.Identity;

/// <summary>
/// Alta de vendedores: usuario + rol <c>Merchant</c> + (cuenta externa) + <see cref="Merchant"/> en
/// <see cref="MerchantStatus.PendingBankAccount"/>, todo en una transacción. Toda alta de vendedor pasa por aquí
/// (Register, ExternalLogin, pruebas) para que nunca quede un usuario sin su Merchant.
/// </summary>
public class AccountRegistration(UserManager<ApplicationUser> userManager, AppDbContext db, TimeProvider clock)
{
    /// <param name="ownerName">Nombre de la persona (<see cref="PersonName"/>); se guarda normalizado en el Merchant.</param>
    /// <param name="password">Contraseña, o <c>null</c> si entra solo con proveedor externo.</param>
    /// <param name="login">Cuenta externa a ligar, o <c>null</c>.</param>
    public async Task<IdentityResult> CreateMerchantAsync(
        ApplicationUser user, string? ownerName, string? password, UserLoginInfo? login = null)
    {
        // El formulario ya lo valida; esto cubre a cualquier otro que llame sin pasar por él, antes de crear nada.
        if (!PersonName.IsValid(ownerName))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidOwnerName",
                Description = string.IsNullOrWhiteSpace(ownerName) ? OwnerNameMessages.Required : OwnerNameMessages.Invalid,
            });
        }

        // UserManager guarda en cada llamada sobre el mismo DbContext (scoped): la transacción las abarca todas.
        await using var transaction = await db.Database.BeginTransactionAsync();

        var result = password is null ? await userManager.CreateAsync(user) : await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            result = await userManager.AddToRoleAsync(user, Roles.Merchant);
        }
        if (result.Succeeded && login is not null)
        {
            result = await userManager.AddLoginAsync(user, login);
        }
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            return result;
        }

        db.Merchants.Add(Merchant.Register(user.Id, clock.GetUtcNow(), ownerName!));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return result;
    }
}

public static class LegalAcceptanceExtensions
{
    /// <summary>Marca que el usuario aceptó las versiones vigentes de Términos y Aviso de privacidad (una sola casilla).</summary>
    public static ApplicationUser AcceptLegalDocuments(this ApplicationUser user, DateTimeOffset now)
    {
        user.TermsAcceptedAt = now;
        user.TermsVersion = LegalDocuments.CurrentTermsVersion;
        user.PrivacyNoticeAcceptedAt = now;
        user.PrivacyNoticeVersion = LegalDocuments.CurrentPrivacyNoticeVersion;
        return user;
    }
}
