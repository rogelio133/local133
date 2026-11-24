using System.Security.Cryptography;

namespace Local133.Domain.Merchants;

/// <summary>
/// Código de invitación del vendedor (5.8, §6 del diseño): 6 caracteres sin letras confusas (sin 0, O, 1 ni I), único.
/// Se genera al registrarse y no cambia. La liga <c>/r/{código}</c> lleva al registro con el código ya escrito.
/// </summary>
public static class ReferralCode
{
    public const int Length = 6;

    /// <summary>Caracteres permitidos: dígitos 2–9 y letras mayúsculas sin O ni I.</summary>
    public const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>
    /// Lo que se acepta al escribirlo (cliente y servidor): 6 caracteres del alfabeto, también en minúsculas, con
    /// espacios en los extremos. <see cref="Normalize"/> lo deja en mayúsculas.
    /// </summary>
    public const string InputPattern = @"^\s*[2-9A-HJ-NP-Za-hj-np-z]{6}\s*$";

    /// <summary>Largo máximo que se acepta en el campo (antes de normalizar).</summary>
    public const int InputMaxLength = 12;

    /// <summary>Prefijo de la liga de invitación.</summary>
    public const string PathPrefix = "/r/";

    /// <summary>Ruta de la liga de invitación (relativa a la raíz del sitio).</summary>
    public static string PathFor(string code) => PathPrefix + code;

    /// <summary>Código nuevo al azar (criptográfico). La unicidad la revisa quien lo guarda.</summary>
    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>Quita espacios y guiones y pasa a mayúsculas: « abc-234 » ⇒ «ABC234». Vacío si no hay nada.</summary>
    public static string Normalize(string? value) =>
        value is null
            ? string.Empty
            : new string(value.Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).ToArray());

    /// <summary>Tiene la forma de un código (ya normalizado): 6 caracteres del <see cref="Alphabet"/>.</summary>
    public static bool IsValid(string? code) => code is { Length: Length } && code.All(c => Alphabet.Contains(c));
}
