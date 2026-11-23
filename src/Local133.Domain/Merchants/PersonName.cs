using System.Text.RegularExpressions;

namespace Local133.Domain.Merchants;

/// <summary>
/// Nombre de la persona que registra el negocio (<see cref="Merchant.OwnerName"/>), capturado en el alta (4.0): letras
/// (con acentos), espacios, punto, guion y apóstrofo, hasta <see cref="MaxLength"/> caracteres.
/// </summary>
public static partial class PersonName
{
    public const int MaxLength = 100;

    /// <summary>
    /// Mismo patrón en .NET y en el cliente (jQuery Validation arma el <c>RegExp</c> sin la bandera <c>u</c>, por eso
    /// los rangos explícitos y no <c>\p{L}</c>). Exige al menos una letra. Acepta <c>’</c> porque el teclado del iPhone
    /// convierte <c>'</c> en <c>’</c>; <see cref="Normalize"/> lo deja como <c>'</c>.
    /// </summary>
    public const string Pattern = @"^(?=.*[A-Za-zÀ-ÖØ-öø-ÿ])[A-Za-zÀ-ÖØ-öø-ÿ .'’\-]+$";

    /// <summary>Quita espacios en los extremos, junta los repetidos y cambia <c>’</c> por <c>'</c>. No valida.</summary>
    public static string Normalize(string? value) =>
        value is null ? string.Empty : Spaces().Replace(value.Trim(), " ").Replace('’', '\'');

    /// <summary>Válido una vez normalizado: no vacío, hasta <see cref="MaxLength"/> y con los caracteres de <see cref="Pattern"/>.</summary>
    public static bool IsValid(string? value)
    {
        var name = Normalize(value);
        return name.Length is > 0 and <= MaxLength && Allowed().IsMatch(name);
    }

    /// <summary>Primera palabra, para el saludo («María José López» ⇒ «María»); <c>null</c> si no hay nombre.</summary>
    public static string? FirstName(string? value)
    {
        var name = Normalize(value);
        return name.Length == 0 ? null : name.Split(' ')[0];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(Pattern)]
    private static partial Regex Allowed();
}
