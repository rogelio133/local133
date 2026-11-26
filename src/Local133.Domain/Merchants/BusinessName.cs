using System.Text.RegularExpressions;
using Local133.Domain.Sales;

namespace Local133.Domain.Merchants;

/// <summary>
/// Nombre del negocio (<see cref="Merchant.BusinessName"/>), editable en «Mi información» (4.1). Aparece en el recibo,
/// el catálogo y el alias de la cuenta bancaria. Usa los mismos caracteres que el concepto
/// (<see cref="Sale.ConceptTextPattern"/>), con al menos una letra o número. Hasta el 2026-11-26 iba al inicio del
/// concepto de cada cobro; como nunca lleva el separador <c>·</c>, <see cref="Sale.ConceptTextOf"/> lo quita de esas
/// ventas viejas aunque el negocio cambie de nombre.
/// </summary>
public static partial class BusinessName
{
    public const int MaxLength = Merchant.BusinessNameMaxLength;

    /// <summary>Mismo patrón en .NET y en el cliente (rangos explícitos: jQuery Validation no usa la bandera <c>u</c>).</summary>
    public const string Pattern = @"^(?=.*[A-Za-z0-9ÁÉÍÓÚÜÑáéíóúüñ])[A-Za-z0-9ÁÉÍÓÚÜÑáéíóúüñ .,\-#/()]+$";

    /// <summary>Quita espacios en los extremos y junta los repetidos. No valida.</summary>
    public static string Normalize(string? value) =>
        value is null ? string.Empty : Spaces().Replace(value.Trim(), " ");

    /// <summary>Válido una vez normalizado: no vacío, hasta <see cref="MaxLength"/> y con los caracteres de <see cref="Pattern"/>.</summary>
    public static bool IsValid(string? value)
    {
        var name = Normalize(value);
        return name.Length is > 0 and <= MaxLength && Allowed().IsMatch(name);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(Pattern)]
    private static partial Regex Allowed();
}
