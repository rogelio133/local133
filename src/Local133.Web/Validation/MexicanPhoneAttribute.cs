using System.ComponentModel.DataAnnotations;
using Local133.Application.Sales;
using Local133.Domain.Sales;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Local133.Web.Validation;

/// <summary>
/// Celular de 10 dígitos (acepta espacios, guiones, paréntesis, puntos y la lada <c>+52</c>). En cliente emite
/// <c>data-val-phone</c>, que valida el adaptador de <c>wwwroot/js/validation.js</c> con la misma regla que
/// <see cref="PhoneNumber.Normalize"/>. Un valor vacío lo resuelve la regla de obligatorio.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MexicanPhoneAttribute : ValidationAttribute, IClientModelValidator
{
    public MexicanPhoneAttribute() => ErrorMessage = SalePhoneMessages.Invalid;

    public override bool IsValid(object? value) =>
        value is not string text || string.IsNullOrWhiteSpace(text) || PhoneNumber.IsValid(PhoneNumber.Normalize(text));

    public void AddValidation(ClientModelValidationContext context)
    {
        context.Attributes.TryAdd("data-val", "true");
        context.Attributes.TryAdd("data-val-phone", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
    }
}
