using System.Text.RegularExpressions;

namespace Local133.Domain.Merchants;

/// <summary>
/// Nombre de la persona que registra el negocio (<see cref="Merchant.OwnerName"/>), capturado en el alta (4.0): solo
/// letras (con acentos, ñ, ü) y espacios, hasta <see cref="MaxLength"/> caracteres (ajustes 2026-11-26: antes también
/// aceptaba punto, guion y apóstrofo).
/// </summary>
public static partial class PersonName
{
    public const int MaxLength = 100;

    /// <summary>
    /// Mismo patrón en .NET, en el cliente y en <c>wwwroot/js/letters-input.js</c> (jQuery Validation arma el
    /// <c>RegExp</c> sin la bandera <c>u</c>, por eso los rangos explícitos y no <c>\p{L}</c>). Exige al menos una letra.
    /// </summary>
    public const string Pattern = @"^(?=.*[A-Za-zÀ-ÖØ-öø-ÿ])[A-Za-zÀ-ÖØ-öø-ÿ ]+$";

    /// <summary>Quita espacios en los extremos y junta los repetidos. No valida.</summary>
    public static string Normalize(string? value) =>
        value is null ? string.Empty : Spaces().Replace(value.Trim(), " ");

    /// <summary>
    /// Cambia por espacio todo lo que no sea letra o espacio y normaliza («Ma. José» ⇒ «Ma José», «O'Brien» ⇒
    /// «O Brien»). Sirve para proponer un nombre guardado antes de la regla de solo letras, o el del proveedor externo;
    /// no valida.
    /// </summary>
    public static string Clean(string? value) =>
        value is null ? string.Empty : Normalize(NotAllowed().Replace(value, " "));

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

    [GeneratedRegex(@"[^A-Za-zÀ-ÖØ-öø-ÿ\s]")]
    private static partial Regex NotAllowed();
}
