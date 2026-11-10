using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Local133.Web.Identity;

/// <summary>Mensajes de model binding en español (valores inválidos, campos requeridos sin atributo).</summary>
public static class ModelBindingMessages
{
    public static void UseSpanishMessages(this DefaultModelBindingMessageProvider provider)
    {
        provider.SetAttemptedValueIsInvalidAccessor((value, field) => $"El valor '{value}' no es válido para {field}.");
        provider.SetMissingBindRequiredValueAccessor(field => $"Falta el valor de {field}.");
        provider.SetMissingKeyOrValueAccessor(() => "El valor es obligatorio.");
        provider.SetMissingRequestBodyRequiredValueAccessor(() => "El cuerpo de la petición es obligatorio.");
        provider.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"El valor '{value}' no es válido.");
        provider.SetNonPropertyUnknownValueIsInvalidAccessor(() => "El valor no es válido.");
        provider.SetNonPropertyValueMustBeANumberAccessor(() => "El campo debe ser un número.");
        provider.SetUnknownValueIsInvalidAccessor(field => $"El valor no es válido para {field}.");
        provider.SetValueIsInvalidAccessor(value => $"El valor '{value}' no es válido.");
        provider.SetValueMustBeANumberAccessor(field => $"El campo {field} debe ser un número.");
        provider.SetValueMustNotBeNullAccessor(value => $"El valor '{value}' no es válido.");
    }
}
