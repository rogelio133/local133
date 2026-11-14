using Local133.Domain.Pey;

namespace Local133.Domain.Sales;

/// <summary>
/// Celular del pagador para el cobro por notificación push (10 dígitos, sin lada internacional). Es un dato personal:
/// en logs va <see cref="Mask"/>.
/// </summary>
public static class PhoneNumber
{
    public const int Length = PeyLimits.PhoneNumberLength;

    /// <summary>Lada internacional de México, que se quita si viene pegada (<c>+52 55 1234 5678</c>).</summary>
    public const string MexicoCountryPrefix = "+52";

    /// <summary>
    /// Quita espacios, guiones, paréntesis y puntos, y la lada <c>+52</c> al inicio: <c>(55) 1234-5678</c> ⇒
    /// <c>5512345678</c>. No valida.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var clean = new string(value.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '(' or ')' or '.')).ToArray());
        return clean.StartsWith(MexicoCountryPrefix, StringComparison.Ordinal) ? clean[MexicoCountryPrefix.Length..] : clean;
    }

    /// <summary>Exactamente 10 dígitos (ya normalizado).</summary>
    public static bool IsValid(string? phoneNumber) =>
        phoneNumber is { Length: Length } && phoneNumber.All(char.IsAsciiDigit);

    /// <summary>Solo los últimos 4 dígitos: <c>•••• 5678</c>.</summary>
    public static string Mask(string? phoneNumber) =>
        string.IsNullOrEmpty(phoneNumber) ? string.Empty : "•••• " + (phoneNumber.Length <= 4 ? phoneNumber : phoneNumber[^4..]);
}
