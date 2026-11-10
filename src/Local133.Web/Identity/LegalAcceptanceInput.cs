using System.ComponentModel.DataAnnotations;

namespace Local133.Web.Identity;

/// <summary>
/// Base de los formularios de alta (Register y ExternalLogin): una sola casilla para Términos y condiciones y
/// Aviso de privacidad. La pinta el parcial <c>_AcceptLegalCheckbox</c> con <c>&lt;partial for="Input" /&gt;</c>.
/// </summary>
public abstract class LegalAcceptanceInput
{
    public const string NotAcceptedMessage = "Debes aceptar los Términos y condiciones y el Aviso de privacidad.";

    // [Required] explícito solo para que el data-val-required implícito de un bool no salga en inglés
    // (unobtrusive lo ignora en checkboxes; la regla real es MustBeTrue).
    [Required(ErrorMessage = NotAcceptedMessage)]
    [MustBeTrue(ErrorMessage = NotAcceptedMessage)]
    [Display(Name = "Acepto los Términos y condiciones y el Aviso de privacidad")]
    public bool AcceptLegal { get; set; }
}
