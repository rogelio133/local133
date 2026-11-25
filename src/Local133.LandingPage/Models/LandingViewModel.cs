using System.Text.Json;

namespace Local133.LandingPage.Models;

/// <param name="Words">Palabras que rotan en el hero y en el cierre (nunca vacía; el servidor pinta la primera).</param>
/// <param name="AppUrl">
/// Destino de los botones de registro: el portal, o su liga <c>/r/{código}</c> si llegó un código (5.10).
/// </param>
/// <param name="LoginUrl">«Iniciar sesión» en el portal; nunca lleva el código.</param>
/// <param name="TermsUrl">Términos y condiciones del portal.</param>
/// <param name="PrivacyUrl">Aviso de privacidad del portal.</param>
public sealed record LandingViewModel(
    IReadOnlyList<string> Words,
    string AppUrl,
    string LoginUrl,
    string TermsUrl,
    string PrivacyUrl)
{
    /// <summary>«Hecho para»: las mismas palabras en plural y con mayúscula inicial.</summary>
    public IReadOnlyList<string> MadeFor { get; } = MadeForLabels.For(Words);

    /// <summary>
    /// La palabra más larga: reserva el ancho de la palabra del cierre para que el título no se mueva al rotar.
    /// </summary>
    public string LongestWord { get; } = Words.MaxBy(word => word.Length) ?? string.Empty;

    /// <summary>JSON para <c>data-rotating-words</c>; nulo con una sola palabra (no hay nada que rotar).</summary>
    public string? RotatingWordsJson { get; } = Words.Count > 1 ? JsonSerializer.Serialize(Words) : null;
}
