using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Local133.Domain.Merchants;

/// <summary>
/// Nombre del catálogo público en la liga <c>/tienda/{slug}</c> (5.10, §7 del diseño de suscripciones): de 3 a 40
/// caracteres, minúsculas sin acentos, dígitos y guiones sueltos entre ellos. Único entre vendedores (índice único
/// filtrado); lo elige el vendedor en Pro y se conserva aunque deje de publicar.
/// </summary>
public static partial class PublicSlug
{
    public const int MinLength = 3;
    public const int MaxLength = 40;

    /// <summary>Mismo patrón en .NET y en el cliente (ya normalizado).</summary>
    public const string Pattern = @"^[a-z0-9]+(-[a-z0-9]+)*$";

    /// <summary>Prefijo de la liga pública.</summary>
    public const string PathPrefix = "/tienda/";

    /// <summary>Ruta del catálogo (relativa a la raíz del sitio).</summary>
    public static string PathFor(string slug) => PathPrefix + slug;

    /// <summary>
    /// Minúsculas sin acentos (ñ ⇒ n); cualquier otro carácter se vuelve guion, sin guiones repetidos ni en los
    /// extremos. «Tacos Don Ramón #2» ⇒ «tacos-don-ramon-2». No valida el largo.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? c : '-');
        }

        return Dashes().Replace(builder.ToString(), "-").Trim('-');
    }

    /// <summary>Válido tal como viene (ya normalizado): largo de 3 a 40 y <see cref="Pattern"/>.</summary>
    public static bool IsValid(string? slug) =>
        slug is { Length: >= MinLength and <= MaxLength } && Allowed().IsMatch(slug);

    /// <summary>
    /// Propuesta a partir del nombre del negocio: normalizado y recortado a <see cref="MaxLength"/> (sin dejar un
    /// guion al final). <c>null</c> si no queda un slug válido.
    /// </summary>
    public static string? Suggest(string? businessName)
    {
        var slug = Normalize(businessName);
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return IsValid(slug) ? slug : null;
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex Dashes();

    [GeneratedRegex(Pattern)]
    private static partial Regex Allowed();
}
