using Local133.Application.Auth;
using Local133.Domain.Legal;
using Local133.Domain.Merchants;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Identity;
using Local133.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Identity;

/// <summary>
/// Alta de vendedores: usuario + rol <c>Merchant</c> + (cuenta externa) + <see cref="Merchant"/> en
/// <see cref="MerchantStatus.PendingBankAccount"/>, todo en una transacción. Toda alta de vendedor pasa por aquí
/// (Register, ExternalLogin, pruebas) para que nunca quede un usuario sin su Merchant.
/// </summary>
public class AccountRegistration(UserManager<ApplicationUser> userManager, AppDbContext db, TimeProvider clock)
{
    /// <summary>Intentos de generar un código de invitación que no exista (32⁶ ≈ mil millones de códigos).</summary>
    private const int CodeAttempts = 5;

    /// <param name="ownerName">Nombre de la persona (<see cref="PersonName"/>); se guarda normalizado en el Merchant.</param>
    /// <param name="password">Contraseña, o <c>null</c> si entra solo con proveedor externo.</param>
    /// <param name="login">Cuenta externa a ligar, o <c>null</c>.</param>
    /// <param name="referralCode">
    /// Código de quien lo invitó (5.8), opcional. Si no existe, no se crea nada y regresa el error
    /// <see cref="ReferralCodeMessages.UnknownCodeError"/>.
    /// </param>
    public async Task<IdentityResult> CreateMerchantAsync(
        ApplicationUser user, string? ownerName, string? password, UserLoginInfo? login = null, string? referralCode = null)
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

        var code = ReferralCode.Normalize(referralCode);
        Referrer? referrer = null;
        if (code.Length > 0)
        {
            referrer = ReferralCode.IsValid(code)
                ? await db.Merchants.AsNoTracking()
                    .Where(m => m.ReferralCode == code)
                    .Select(m => new Referrer(m.Id, m.ReferralCode))
                    .SingleOrDefaultAsync()
                : null;
            if (referrer is null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = ReferralCodeMessages.UnknownCodeError,
                    Description = ReferralCodeMessages.Unknown,
                });
            }
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

        var now = clock.GetUtcNow();
        var merchant = Merchant.Register(user.Id, now, ownerName!, await NewReferralCodeAsync());
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync();

        if (referrer is not null)
        {
            db.Referrals.Add(Referral.Create(referrer.Id, merchant.Id, referrer.Code, now));
            await db.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return result;
    }

    /// <summary>
    /// Código de invitación que todavía no usa nadie. El índice único de <c>Merchants.ReferralCode</c> es la última
    /// defensa ante dos altas simultáneas con el mismo código (prácticamente imposible).
    /// </summary>
    private async Task<string> NewReferralCodeAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            var code = ReferralCode.Generate();
            if (attempt == CodeAttempts || !await db.Merchants.AnyAsync(m => m.ReferralCode == code))
            {
                return code;
            }
        }
    }

    private sealed record Referrer(int Id, string Code);
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
