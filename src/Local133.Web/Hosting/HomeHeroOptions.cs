namespace Local133.Web.Hosting;

/// <summary>
/// Portada (sección <c>Home</c>). <see cref="RotatingWords"/> son las palabras que rotan al final de «Cobra con CoDi
/// desde tu …» (<c>wwwroot/js/rotating-word.js</c>). Van por defecto en <c>appsettings.json</c> y se sobrescriben por
/// índice desde user-secrets o App Settings (<c>Home__RotatingWords__0</c>…). No son secretos.
/// </summary>
public sealed class HomeHeroOptions
{
    public const string SectionName = "Home";

    /// <summary>Palabra que se usa si la lista viene vacía o ninguna es válida.</summary>
    public const string DefaultWord = "local";

    public const int MaxWords = 10;

    public const int MaxWordLength = 15;

    public string[] RotatingWords { get; set; } = [];

    /// <summary>
    /// Palabras válidas en su orden: sin espacios en los extremos, sin vacías ni de más de <see cref="MaxWordLength"/>
    /// caracteres, sin repetidas y hasta <see cref="MaxWords"/>. Nunca vacía (<see cref="DefaultWord"/>).
    /// </summary>
    /// <remarks>
    /// Las vacías se ignoran a propósito: el binder de arreglos mezcla por índice las fuentes de configuración, así que
    /// para acortar la lista de <c>appsettings.json</c> los índices sobrantes se ponen en <c>""</c>.
    /// </remarks>
    public IReadOnlyList<string> GetWords()
    {
        var words = (RotatingWords ?? [])
            .Select(word => word?.Trim())
            .Where(word => !string.IsNullOrEmpty(word) && word.Length <= MaxWordLength)
            .Select(word => word!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxWords)
            .ToList();
        return words.Count > 0 ? words : [DefaultWord];
    }
}
