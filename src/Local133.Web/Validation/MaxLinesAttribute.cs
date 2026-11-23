using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Local133.Web.Validation;

/// <summary>
/// Texto de varios renglones con a lo más <see cref="Max"/> renglones con texto (los vacíos no cuentan, igual que
/// <c>MerchantAddress.Normalize</c>). En cliente emite <c>data-val-maxlines</c>, que valida el adaptador de
/// <c>wwwroot/js/validation.js</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxLinesAttribute(int max) : ValidationAttribute, IClientModelValidator
{
    public int Max { get; } = max;

    public override bool IsValid(object? value) =>
        value is not string text || CountLines(text) <= Max;

    public void AddValidation(ClientModelValidationContext context)
    {
        context.Attributes.TryAdd("data-val", "true");
        context.Attributes.TryAdd("data-val-maxlines", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        context.Attributes.TryAdd("data-val-maxlines-max", Max.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static int CountLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Count(line => !string.IsNullOrWhiteSpace(line));
}
