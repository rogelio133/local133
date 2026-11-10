using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Local133.Web.Identity;

/// <summary>
/// Casilla obligatoria (p. ej. aceptar términos). <c>[Range]</c> no sirve con checkbox en el cliente; aquí se emite
/// <c>data-val-mustbetrue</c>, que entiende el adaptador de <c>wwwroot/js/validation.js</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MustBeTrueAttribute : ValidationAttribute, IClientModelValidator
{
    public override bool IsValid(object? value) => value is true;

    public void AddValidation(ClientModelValidationContext context)
    {
        context.Attributes.TryAdd("data-val", "true");
        context.Attributes.TryAdd("data-val-mustbetrue", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
    }
}
