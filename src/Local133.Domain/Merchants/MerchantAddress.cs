using System.Text.RegularExpressions;

namespace Local133.Domain.Merchants;

/// <summary>
/// Dirección o ubicación del puesto (<see cref="Merchant.Address"/>), opcional y en texto libre (4.1): muchos
/// vendedores están en un tianguis o un mercado («Tianguis de los martes, pasillo 4»). Hasta <see cref="MaxLength"/>
/// caracteres en <see cref="MaxLines"/> renglones como máximo, sin caracteres de control.
/// </summary>
public static partial class MerchantAddress
{
    public const int MaxLength = 200;

    public const int MaxLines = 3;

    /// <summary>
    /// Renglones con <c>\n</c>, cada uno sin espacios en los extremos ni repetidos, y sin renglones vacíos. Vacío ⇒
    /// cadena vacía. No valida.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(line => Spaces().Replace(line.Trim(), " "))
            .Where(line => line.Length > 0);
        return string.Join('\n', lines);
    }

    /// <summary>Vacío (no hay dirección) o, normalizada, hasta <see cref="MaxLength"/> caracteres y <see cref="MaxLines"/> renglones sin caracteres de control.</summary>
    public static bool IsValid(string? value)
    {
        var address = Normalize(value);
        if (address.Length == 0)
        {
            return true;
        }

        return address.Length <= MaxLength
            && address.Count(c => c == '\n') < MaxLines
            && !address.Any(c => c != '\n' && char.IsControl(c));
    }

    // Espacios y tabuladores, no saltos de línea (ya se partió por renglón).
    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex Spaces();
}
