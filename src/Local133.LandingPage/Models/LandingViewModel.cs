namespace Local133.LandingPage.Models;

/// <param name="Words">Palabras que rotan en el título (nunca vacía; el servidor pinta la primera).</param>
/// <param name="AppUrl">Destino del botón «Empieza a cobrar».</param>
public sealed record LandingViewModel(IReadOnlyList<string> Words, string AppUrl);
