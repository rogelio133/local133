using Local133.Domain.Pey;

namespace Local133.Domain.BankAccounts;

/// <summary>
/// CLABE interbancaria (18 dígitos: banco 3, plaza 3, cuenta 11, dígito verificador 1). Se valida aquí antes de
/// enviarla a Pey y nunca se muestra completa: en UI y logs va <see cref="Mask"/>.
/// </summary>
public static class Clabe
{
    public const int Length = PeyLimits.ClabeLength;

    private static readonly int[] Weights = [3, 7, 1];

    /// <summary>Quita espacios y guiones (el vendedor suele copiarla agrupada: <c>012 180 0158…</c>).</summary>
    public static string Normalize(string? value) =>
        value is null ? string.Empty : new string(value.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    /// <summary>18 dígitos y dígito verificador correcto (ya normalizada).</summary>
    public static bool IsValid(string? clabe)
    {
        if (clabe is null || clabe.Length != Length || !clabe.All(char.IsAsciiDigit))
        {
            return false;
        }

        return clabe[Length - 1] - '0' == CheckDigit(clabe);
    }

    /// <summary>
    /// Dígito verificador de Banxico: cada uno de los primeros 17 dígitos se multiplica por 3, 7, 1 (en ciclo), se
    /// suma cada producto módulo 10 y el dígito es <c>(10 − suma mod 10) mod 10</c>.
    /// </summary>
    public static int CheckDigit(string clabe)
    {
        ArgumentNullException.ThrowIfNull(clabe);
        if (clabe.Length < Length - 1)
        {
            throw new ArgumentException($"Se necesitan al menos {Length - 1} dígitos.", nameof(clabe));
        }

        var sum = 0;
        for (var i = 0; i < Length - 1; i++)
        {
            sum += (clabe[i] - '0') * Weights[i % Weights.Length] % 10;
        }

        return (10 - sum % 10) % 10;
    }

    /// <summary>Solo los últimos 4 dígitos: <c>•••• 5713</c>.</summary>
    public static string Mask(string? clabe) =>
        string.IsNullOrEmpty(clabe) ? string.Empty : "•••• " + (clabe.Length <= 4 ? clabe : clabe[^4..]);
}
