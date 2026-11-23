namespace Local133.Application.Merchants;

/// <summary>
/// «Mi información» (4.1): datos generales del vendedor (su nombre, el del negocio, la dirección y el teléfono), el
/// logo y la foto del local. El teléfono es el celular de su cuenta de Identity (<c>AspNetUsers.PhoneNumber</c>) y el
/// correo se cambia por el flujo de Identity, con confirmación.
/// </summary>
public interface IMerchantProfile
{
    /// <summary>Datos del vendedor, o <c>null</c> si no existe.</summary>
    Task<MerchantProfileData?> GetAsync(int merchantId, CancellationToken cancellationToken = default);

    Task<SaveProfileResult> SaveAsync(SaveProfileCommand command, CancellationToken cancellationToken = default);

    /// <summary>Procesa y guarda el logo o la foto del local; los archivos de la imagen anterior se borran después.</summary>
    Task<SaveMerchantImageResult> SaveImageAsync(
        int merchantId, MerchantImageKind kind, MerchantImageUpload upload, CancellationToken cancellationToken = default);

    /// <summary>Quita el logo o la foto del local (idempotente: sin imagen también es <see cref="SaveMerchantImageResult.Saved"/>).</summary>
    Task<SaveMerchantImageResult> RemoveImageAsync(int merchantId, MerchantImageKind kind, CancellationToken cancellationToken = default);
}

/// <param name="Email">Correo de la cuenta (solo lectura en «Mi información»).</param>
/// <param name="CanEdit"><c>false</c> para un vendedor dado de baja (<c>Merchant.CanEditProfile</c>).</param>
public sealed record MerchantProfileData(
    int MerchantId,
    string? OwnerName,
    string? BusinessName,
    string? Address,
    string? PhoneNumber,
    string? Email,
    string? LogoKey,
    string? ShopPhotoKey,
    bool CanEdit);

/// <summary>Lo que escribió el vendedor; el servicio lo valida y lo normaliza (el teléfono puede venir con espacios o +52).</summary>
public sealed record SaveProfileCommand(int MerchantId, string? OwnerName, string? BusinessName, string? Address, string? PhoneNumber);

/// <summary>Textos de los datos generales de «Mi información».</summary>
public static class ProfileMessages
{
    public const string OwnerNameRequired = "Escribe tu nombre.";

    public const string OwnerNameTooLong = "Tu nombre puede tener hasta 100 caracteres.";

    public const string OwnerNameInvalid = "Usa solo letras, espacios, punto, guion y apóstrofo.";

    public const string BusinessNameRequired = "Escribe el nombre de tu negocio.";

    public const string BusinessNameTooLong = "El nombre del negocio puede tener hasta 120 caracteres.";

    /// <summary>Mismos caracteres que el concepto del cobro (el nombre va al inicio de cada concepto).</summary>
    public const string BusinessNameInvalid = "Usa solo letras, números, espacios y . , - # / ( )";

    public const string AddressTooLong = "La dirección puede tener hasta 200 caracteres.";

    public const string AddressTooManyLines = "Escribe la dirección en 3 renglones como máximo.";

    public const string AddressInvalid = "La dirección tiene caracteres que no se permiten.";

    public const string PhoneInvalid = "Escribe un teléfono de 10 dígitos, por ejemplo 55 1234 5678.";

    public const string ReadOnly = "Tu cuenta está dada de baja: no puedes modificar tu información.";

    public const string Saved = "Tu información se guardó.";

    public const string LogoSaved = "Guardamos tu logo.";

    public const string LogoRemoved = "Quitamos tu logo.";

    public const string ShopPhotoSaved = "Guardamos la foto de tu local.";

    public const string ShopPhotoRemoved = "Quitamos la foto de tu local.";
}

public abstract record SaveProfileResult
{
    private SaveProfileResult()
    {
    }

    /// <param name="PhoneChanged">Cambió el celular de la cuenta (y su security stamp): hay que refrescar la cookie.</param>
    public sealed record Saved(bool PhoneChanged) : SaveProfileResult;

    /// <summary>Datos inválidos; llave = propiedad del comando (<c>OwnerName</c>, <c>BusinessName</c>, <c>Address</c>, <c>PhoneNumber</c>).</summary>
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : SaveProfileResult;

    /// <summary>Vendedor dado de baja: solo lectura.</summary>
    public sealed record ReadOnly : SaveProfileResult;

    public sealed record NotFound : SaveProfileResult;
}

public abstract record SaveMerchantImageResult
{
    private SaveMerchantImageResult()
    {
    }

    public sealed record Saved : SaveMerchantImageResult;

    /// <summary>No es una imagen aceptada o pesa demasiado; <see cref="Message"/> se muestra al vendedor.</summary>
    public sealed record Invalid(string Message) : SaveMerchantImageResult;

    public sealed record ReadOnly : SaveMerchantImageResult;

    public sealed record NotFound : SaveMerchantImageResult;
}
