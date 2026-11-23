using System.ComponentModel.DataAnnotations;
using Local133.Application.Merchants;
using Local133.Domain.Merchants;
using Local133.Web.Identity;
using Local133.Web.Validation;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Datos generales de «Mi información» (4.1). El servidor vuelve a validar con las reglas del dominio.</summary>
public sealed class ProfileInput
{
    [Required(ErrorMessage = OwnerNameMessages.Required)]
    [StringLength(PersonName.MaxLength, ErrorMessage = OwnerNameMessages.TooLong)]
    [RegularExpression(PersonName.Pattern, ErrorMessage = OwnerNameMessages.Invalid)]
    [Display(Name = "Tu nombre")]
    public string? OwnerName { get; set; }

    [Required(ErrorMessage = ProfileMessages.BusinessNameRequired)]
    [StringLength(Domain.Merchants.BusinessName.MaxLength, ErrorMessage = ProfileMessages.BusinessNameTooLong)]
    [RegularExpression(Domain.Merchants.BusinessName.Pattern, ErrorMessage = ProfileMessages.BusinessNameInvalid)]
    [Display(Name = "Nombre del negocio")]
    public string? BusinessName { get; set; }

    [MexicanPhone(ErrorMessage = ProfileMessages.PhoneInvalid)]
    [Display(Name = "Teléfono (celular)")]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Sin <c>[StringLength]</c>: el navegador manda los saltos como <c>\r\n</c> y el largo se mide ya normalizado
    /// (en el servidor, <see cref="MerchantAddress"/>; en el cliente, <c>maxlength</c>).
    /// </summary>
    [MaxLines(MerchantAddress.MaxLines, ErrorMessage = ProfileMessages.AddressTooManyLines)]
    [Display(Name = "Dirección o ubicación del puesto")]
    public string? Address { get; set; }
}

public sealed record ProfileViewModel
{
    public ProfileInput Input { get; init; } = new();

    public string? Email { get; init; }

    public bool CanEdit { get; init; }

    public string? LogoUrl { get; init; }

    public string? ShopPhotoUrl { get; init; }

    /// <summary>Error de la última imagen subida (lo pinta su tarjeta).</summary>
    public string? LogoError { get; init; }

    public string? ShopPhotoError { get; init; }

    public static ProfileViewModel From(MerchantProfileData data, ProfileInput? input = null) => new()
    {
        Input = input ?? new ProfileInput
        {
            OwnerName = data.OwnerName,
            BusinessName = data.BusinessName,
            PhoneNumber = data.PhoneNumber,
            Address = data.Address,
        },
        Email = data.Email,
        CanEdit = data.CanEdit,
        LogoUrl = data.LogoKey is { } logo ? ProfileUrls.Logo(logo) : null,
        ShopPhotoUrl = data.ShopPhotoKey is { } photo ? ProfileUrls.ShopPhoto(photo) : null,
    };
}

/// <summary>URLs de «Mi información» (rutas de <c>ProfileController</c>).</summary>
public static class ProfileUrls
{
    public const string Index = "/app/mi-informacion";

    public const string LogoUpload = "/app/mi-informacion/logo";

    public const string LogoRemove = "/app/mi-informacion/logo/quitar";

    public const string ShopPhotoUpload = "/app/mi-informacion/foto";

    public const string ShopPhotoRemove = "/app/mi-informacion/foto/quitar";

    /// <summary>Cambio de correo con confirmación (Identity).</summary>
    public const string ChangeEmail = "/Identity/Account/Manage/Email";

    /// <summary>Logo; <c>v</c> cambia con cada imagen nueva (la respuesta se guarda en caché para siempre).</summary>
    public static string Logo(string key) => $"{LogoUpload}?v={MerchantImageRules.Version(key)}";

    public static string ShopPhoto(string key) => $"{ShopPhotoUpload}?v={MerchantImageRules.Version(key)}";
}

/// <summary>Tarjeta de una imagen de «Mi información» (parcial <c>_ProfileImageCard</c>): su propio formulario multipart.</summary>
public sealed record ProfileImageCardViewModel(
    MerchantImageKind Kind,
    string Title,
    string? ImageUrl,
    string? Error,
    bool CanEdit,
    string UploadUrl,
    string RemoveUrl,
    string Hint)
{
    public bool IsLogo => Kind == MerchantImageKind.Logo;

    /// <summary>Prefijo de los ids y atributos <c>data-</c> (<c>logo</c> o <c>shop-photo</c>).</summary>
    public string Slug => IsLogo ? "logo" : "shop-photo";

    /// <summary>Tipo en que el navegador reduce las imágenes grandes: PNG en el logo, para conservar la transparencia.</summary>
    public string OutputType => MerchantImageRules.ContentType(Kind);

    public static ProfileImageCardViewModel Logo(ProfileViewModel model) => new(
        MerchantImageKind.Logo, "Logo", model.LogoUrl, model.LogoError, model.CanEdit,
        ProfileUrls.LogoUpload, ProfileUrls.LogoRemove,
        "JPG, PNG o WebP de hasta 10 MB. Un PNG con fondo transparente se ve mejor en tus recibos.");

    public static ProfileImageCardViewModel ShopPhoto(ProfileViewModel model) => new(
        MerchantImageKind.ShopPhoto, "Foto del local", model.ShopPhotoUrl, model.ShopPhotoError, model.CanEdit,
        ProfileUrls.ShopPhotoUpload, ProfileUrls.ShopPhotoRemove,
        "JPG, PNG o WebP de hasta 10 MB. Se guarda reducida y sin la ubicación de tu celular.");
}
