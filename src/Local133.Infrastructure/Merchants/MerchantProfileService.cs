using Local133.Application.Merchants;
using Local133.Application.Storage;
using Local133.Domain.Merchants;
using Local133.Domain.Sales;
using Local133.Infrastructure.Identity;
using Local133.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Merchants;

/// <summary>
/// «Mi información» (4.1). Los datos del vendedor y el celular de su cuenta (con <see cref="UserManager{TUser}"/>,
/// que también renueva el security stamp) se guardan en una transacción. Las imágenes siguen el patrón de las fotos de
/// producto: se procesan y se guardan en <see cref="IFileStorage"/> antes de guardar el vendedor; si la base falla,
/// los archivos nuevos se borran, y los de la imagen anterior se borran después de guardar.
/// </summary>
internal sealed class MerchantProfileService(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    IFileStorage storage,
    IMerchantImageProcessor imageProcessor,
    ILogger<MerchantProfileService> logger) : IMerchantProfile
{
    public async Task<MerchantProfileData?> GetAsync(int merchantId, CancellationToken cancellationToken = default)
    {
        var row = await db.Merchants.AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.Id == merchantId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { Merchant = m, u.Email, u.PhoneNumber })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var merchant = row.Merchant;
        return new MerchantProfileData(merchant.Id, merchant.OwnerName, merchant.BusinessName, merchant.Address,
            row.PhoneNumber, row.Email, merchant.LogoKey, merchant.ShopPhotoKey, merchant.CanEditProfile,
            merchant.DefaultConcept);
    }

    public async Task<SaveProfileResult> SaveAsync(SaveProfileCommand command, CancellationToken cancellationToken = default)
    {
        var errors = Validate(command);
        if (errors.Count > 0)
        {
            return new SaveProfileResult.Invalid(errors.AsReadOnly());
        }

        var merchant = await FindAsync(command.MerchantId, cancellationToken);
        if (merchant is null)
        {
            return new SaveProfileResult.NotFound();
        }

        if (!merchant.CanEditProfile)
        {
            return new SaveProfileResult.ReadOnly();
        }

        var user = await users.FindByIdAsync(merchant.UserId)
            ?? throw new InvalidOperationException($"El vendedor {merchant.Id} no tiene usuario.");
        var phone = string.IsNullOrWhiteSpace(command.PhoneNumber) ? null : PhoneNumber.Normalize(command.PhoneNumber);
        var phoneChanged = !string.Equals(phone, user.PhoneNumber, StringComparison.Ordinal);

        merchant.UpdateProfile(command.OwnerName!, command.BusinessName!, command.Address, command.DefaultConcept);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (phoneChanged)
        {
            var result = await users.SetPhoneNumberAsync(user, phone);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"No se pudo guardar el celular del vendedor {merchant.Id}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
            }
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Vendedor {MerchantId} guardó «Mi información» (celular: {PhoneChange})",
            merchant.Id, phoneChanged ? "cambió" : "sin cambio");
        return new SaveProfileResult.Saved(phoneChanged);
    }

    public async Task<SaveMerchantImageResult> SaveImageAsync(
        int merchantId, MerchantImageKind kind, MerchantImageUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var merchant = await FindAsync(merchantId, cancellationToken);
        if (merchant is null)
        {
            return new SaveMerchantImageResult.NotFound();
        }

        if (!merchant.CanEditProfile)
        {
            return new SaveMerchantImageResult.ReadOnly();
        }

        if (upload.Length > MerchantImageRules.MaxUploadBytes)
        {
            return new SaveMerchantImageResult.Invalid(MerchantImageMessages.TooLarge);
        }

        byte[] content;
        switch (await imageProcessor.ProcessAsync(kind, upload.Content, cancellationToken))
        {
            case MerchantImageProcessingResult.Rejected rejected:
                return new SaveMerchantImageResult.Invalid(rejected.Message);
            case MerchantImageProcessingResult.Processed processed:
                content = processed.Content;
                break;
            default:
                throw new InvalidOperationException("Resultado de imagen desconocido.");
        }

        var newKey = MerchantImageRules.NewKey(merchant.Id, kind);
        using (var stream = new MemoryStream(content, writable: false))
        {
            await storage.SaveAsync(MerchantImageRules.FileKey(kind, newKey), stream, MerchantImageRules.ContentType(kind), cancellationToken);
        }

        var previousKey = Replace(merchant, kind, newKey);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await DeleteFileAsync(kind, newKey);
            throw;
        }

        if (previousKey is not null)
        {
            await DeleteFileAsync(kind, previousKey);
        }

        logger.LogInformation("Vendedor {MerchantId} guardó su {Image}", merchant.Id, kind);
        return new SaveMerchantImageResult.Saved();
    }

    public async Task<SaveMerchantImageResult> RemoveImageAsync(
        int merchantId, MerchantImageKind kind, CancellationToken cancellationToken = default)
    {
        var merchant = await FindAsync(merchantId, cancellationToken);
        if (merchant is null)
        {
            return new SaveMerchantImageResult.NotFound();
        }

        if (!merchant.CanEditProfile)
        {
            return new SaveMerchantImageResult.ReadOnly();
        }

        var previousKey = Replace(merchant, kind, null);
        if (previousKey is null)
        {
            return new SaveMerchantImageResult.Saved();
        }

        await db.SaveChangesAsync(cancellationToken);
        await DeleteFileAsync(kind, previousKey);
        logger.LogInformation("Vendedor {MerchantId} quitó su {Image}", merchant.Id, kind);
        return new SaveMerchantImageResult.Saved();
    }

    /// <summary>Errores por campo, con las mismas reglas del dominio que <see cref="Merchant.UpdateProfile"/>.</summary>
    private static Dictionary<string, string[]> Validate(SaveProfileCommand command)
    {
        var errors = new Dictionary<string, string[]>();
        var ownerName = PersonName.Normalize(command.OwnerName);
        if (ownerName.Length == 0)
        {
            errors[nameof(command.OwnerName)] = [ProfileMessages.OwnerNameRequired];
        }
        else if (ownerName.Length > PersonName.MaxLength)
        {
            errors[nameof(command.OwnerName)] = [ProfileMessages.OwnerNameTooLong];
        }
        else if (!PersonName.IsValid(ownerName))
        {
            errors[nameof(command.OwnerName)] = [ProfileMessages.OwnerNameInvalid];
        }

        var businessName = BusinessName.Normalize(command.BusinessName);
        if (businessName.Length == 0)
        {
            errors[nameof(command.BusinessName)] = [ProfileMessages.BusinessNameRequired];
        }
        else if (businessName.Length > BusinessName.MaxLength)
        {
            errors[nameof(command.BusinessName)] = [ProfileMessages.BusinessNameTooLong];
        }
        else if (!BusinessName.IsValid(businessName))
        {
            errors[nameof(command.BusinessName)] = [ProfileMessages.BusinessNameInvalid];
        }

        var address = MerchantAddress.Normalize(command.Address);
        if (address.Length > MerchantAddress.MaxLength)
        {
            errors[nameof(command.Address)] = [ProfileMessages.AddressTooLong];
        }
        else if (address.Count(c => c == '\n') >= MerchantAddress.MaxLines)
        {
            errors[nameof(command.Address)] = [ProfileMessages.AddressTooManyLines];
        }
        else if (!MerchantAddress.IsValid(address))
        {
            errors[nameof(command.Address)] = [ProfileMessages.AddressInvalid];
        }

        if (!string.IsNullOrWhiteSpace(command.PhoneNumber) && !PhoneNumber.IsValid(PhoneNumber.Normalize(command.PhoneNumber)))
        {
            errors[nameof(command.PhoneNumber)] = [ProfileMessages.PhoneInvalid];
        }

        if (DefaultConcept.Normalize(command.DefaultConcept) is { } defaultConcept)
        {
            if (defaultConcept.Length > DefaultConcept.MaxLength)
            {
                errors[nameof(command.DefaultConcept)] = [ProfileMessages.DefaultConceptTooLong];
            }
            else if (!DefaultConcept.IsValid(defaultConcept))
            {
                errors[nameof(command.DefaultConcept)] = [ProfileMessages.DefaultConceptInvalid];
            }
        }

        return errors;
    }

    /// <summary>El vendedor con seguimiento de cambios y <c>Id</c> explícito (no depende del claim de la sesión).</summary>
    private Task<Merchant?> FindAsync(int merchantId, CancellationToken cancellationToken) =>
        db.Merchants.IgnoreQueryFilters().SingleOrDefaultAsync(m => m.Id == merchantId, cancellationToken);

    private static string? Replace(Merchant merchant, MerchantImageKind kind, string? key) =>
        kind == MerchantImageKind.Logo ? merchant.ReplaceLogo(key) : merchant.ReplaceShopPhoto(key);

    /// <summary>Borra el archivo; si falla solo se registra (el vendedor ya quedó bien).</summary>
    private async Task DeleteFileAsync(MerchantImageKind kind, string key)
    {
        var fileKey = MerchantImageRules.FileKey(kind, key);
        try
        {
            await storage.DeleteAsync(fileKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo borrar el archivo {Key}", fileKey);
        }
    }
}
