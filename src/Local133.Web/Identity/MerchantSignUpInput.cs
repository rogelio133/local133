using System.ComponentModel.DataAnnotations;
using Local133.Domain.Merchants;

namespace Local133.Web.Identity;

/// <summary>
/// Base de los formularios de alta de vendedor (Register y ExternalLogin): el nombre de la persona (4.0) y la casilla
/// legal. El nombre lo pinta el parcial <c>_OwnerNameField</c> con <c>&lt;partial for="Input" /&gt;</c>.
/// </summary>
public abstract class MerchantSignUpInput : LegalAcceptanceInput
{
    [Required(ErrorMessage = OwnerNameMessages.Required)]
    [StringLength(PersonName.MaxLength, ErrorMessage = OwnerNameMessages.TooLong)]
    [RegularExpression(PersonName.Pattern, ErrorMessage = OwnerNameMessages.Invalid)]
    [Display(Name = "Tu nombre")]
    public string? OwnerName { get; set; }
}

public static class OwnerNameMessages
{
    public const string Required = "Escribe tu nombre.";
    public const string TooLong = "Tu nombre puede tener hasta {1} caracteres.";
    public const string Invalid = "Usa solo letras, espacios, punto, guion y apóstrofo.";
}
