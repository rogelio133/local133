using System.ComponentModel.DataAnnotations;
using Local133.Application.BankAccounts;
using Local133.Domain.BankAccounts;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Local133.Web.Validation;

/// <summary>
/// CLABE de 18 dígitos con dígito verificador válido (acepta espacios y guiones). En cliente emite
/// <c>data-val-clabe</c>, que valida el adaptador de <c>wwwroot/js/validation.js</c> con la misma regla.
/// Un valor vacío lo resuelve <c>[Required]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ClabeAttribute : ValidationAttribute, IClientModelValidator
{
    public ClabeAttribute() => ErrorMessage = ClabeMessages.Invalid;

    public override bool IsValid(object? value) =>
        value is not string text || string.IsNullOrWhiteSpace(text) || Clabe.IsValid(Clabe.Normalize(text));

    public void AddValidation(ClientModelValidationContext context)
    {
        context.Attributes.TryAdd("data-val", "true");
        context.Attributes.TryAdd("data-val-clabe", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
    }
}
