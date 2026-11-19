using System.ComponentModel.DataAnnotations;
using Local133.Application.Payments;
using Local133.Application.Sales;
using Local133.Domain.Sales;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>
/// Formulario de la prueba de concepto del link de pago (/app/link-de-pago). Mismas reglas de monto y concepto que
/// Cobrar.
/// </summary>
public sealed class PaymentLinkPocInput
{
    /// <summary>Con tarjeta, Pey probablemente rechace montos muy bajos: por eso no empieza en un centavo.</summary>
    public const string DefaultAmount = "10.00";

    [Required(ErrorMessage = SaleAmountMessages.Required)]
    [RegularExpression(ChargeInput.AmountPattern, ErrorMessage = SaleAmountMessages.Invalid)]
    [Display(Name = "Monto")]
    public string? Amount { get; set; } = DefaultAmount;

    [Required(ErrorMessage = SaleConceptMessages.Required)]
    [StringLength(Sale.ConceptTextMaxLength, ErrorMessage = SaleConceptMessages.TooLong)]
    [RegularExpression(Sale.ConceptTextPattern, ErrorMessage = SaleConceptMessages.Invalid)]
    [Display(Name = "Concepto")]
    public string? Concept { get; set; }
}

/// <param name="HttpsWarning">Aviso si la base pública no es HTTPS (Pey la exige en <c>successUrl</c>/<c>backUrl</c>).</param>
public sealed record PaymentLinkPocFormViewModel(PaymentLinkPocInput Input, string? HttpsWarning);

/// <param name="Result"><c>pagado</c> o <c>cancelado</c> (lo pone el sitio en <c>successUrl</c>/<c>backUrl</c>).</param>
/// <param name="Link">El link consultado en Pey, si se conoce su id.</param>
/// <param name="Error">Por qué no se pudo consultar.</param>
public sealed record PaymentLinkPocReturnViewModel(string? Result, string? LinkId, GatewayPaymentLink? Link, string? Error);
