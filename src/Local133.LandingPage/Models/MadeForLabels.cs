using System.Globalization;

namespace Local133.LandingPage.Models;

/// <summary>
/// «Hecho para: Locales • Puestos • …»: las palabras de <c>Home:RotatingWords</c> en plural y con mayúscula inicial.
/// </summary>
/// <remarks>
/// Regla simple que cubre las palabras de la portada: termina en vocal (con o sin acento) ⇒ <c>+s</c>; si no ⇒
/// <c>+es</c> («local» ⇒ «Locales»). No resuelve plurales irregulares («lápiz», «camión»): si se configura una palabra
/// así, se ve mal aquí.
/// </remarks>
public static class MadeForLabels
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");

    public static IReadOnlyList<string> For(IEnumerable<string> words) => words.Select(Plural).ToList();

    public static string Plural(string word)
    {
        var trimmed = word.Trim();
        if (trimmed.Length == 0)
            return trimmed;

        var plural = EndsInVowel(trimmed) ? trimmed + "s" : trimmed + "es";
        return char.ToUpper(plural[0], Spanish) + plural[1..];
    }

    private static bool EndsInVowel(string word) =>
        "aeiouáéíóúAEIOUÁÉÍÓÚ".Contains(word[^1]);
}
