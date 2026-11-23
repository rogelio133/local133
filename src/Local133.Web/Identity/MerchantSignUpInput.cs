using System.ComponentModel.DataAnnotations;
using Local133.Domain.Merchants;
using Code = Local133.Domain.Merchants.ReferralCode;

namespace Local133.Web.Identity;

/// <summary>
/// Base de los formularios de alta de vendedor (Register y ExternalLogin): el nombre de la persona (4.0), el código de
/// invitación opcional (5.8) y la casilla legal. Los campos los pintan los parciales <c>_OwnerNameField</c> y
/// <c>_ReferralCodeField</c> con <c>&lt;partial for="Input" /&gt;</c>.
/// </summary>
public abstract class MerchantSignUpInput : LegalAcceptanceInput
{
    [Required(ErrorMessage = OwnerNameMessages.Required)]
    [StringLength(PersonName.MaxLength, ErrorMessage = OwnerNameMessages.TooLong)]
    [RegularExpression(PersonName.Pattern, ErrorMessage = OwnerNameMessages.Invalid)]
    [Display(Name = "Tu nombre")]
    public string? OwnerName { get; set; }

    /// <summary>Código de quien lo invitó (opcional). Se normaliza con <see cref="Code.Normalize"/>.</summary>
    [StringLength(Code.InputMaxLength, ErrorMessage = ReferralCodeMessages.Format)]
    [RegularExpression(Code.InputPattern, ErrorMessage = ReferralCodeMessages.Format)]
    [Display(Name = "Código de invitación (opcional)")]
    public string? ReferralCode { get; set; }
}

public static class OwnerNameMessages
{
    public const string Required = "Escribe tu nombre.";
    public const string TooLong = "Tu nombre puede tener hasta {1} caracteres.";
    public const string Invalid = "Usa solo letras, espacios, punto, guion y apóstrofo.";
}

public static class ReferralCodeMessages
{
    /// <summary><c>IdentityError.Code</c> del código que no existe (la página lo pone en el campo).</summary>
    public const string UnknownCodeError = "UnknownReferralCode";

    public const string Format = "El código tiene 6 letras y números.";
    public const string Unknown = "No encontramos ese código de invitación. Revísalo o deja el campo vacío.";
}
