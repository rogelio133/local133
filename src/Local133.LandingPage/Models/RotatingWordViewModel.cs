namespace Local133.LandingPage.Models;

/// <summary>Parcial <c>_RotatingWord</c>: la palabra que rota en el hero y en el cierre (<c>rotating-word.js</c>).</summary>
/// <param name="Word">Primera palabra, pintada por el servidor (el título queda completo sin JS).</param>
/// <param name="WordsJson">Lista para <c>data-rotating-words</c>; nula con una sola palabra.</param>
/// <param name="CssClass">Clase extra de cada lugar.</param>
public sealed record RotatingWordViewModel(string Word, string? WordsJson, string CssClass)
{
    public static RotatingWordViewModel For(LandingViewModel landing, string cssClass) =>
        new(landing.Words[0], landing.RotatingWordsJson, cssClass);
}
