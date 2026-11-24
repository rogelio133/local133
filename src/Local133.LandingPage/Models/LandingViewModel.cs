namespace Local133.LandingPage.Models;

/// <param name="Words">Palabras que rotan en el título (nunca vacía; el servidor pinta la primera).</param>
/// <param name="AppUrl">Destino del botón «Empieza a cobrar»: el portal, o su liga <c>/r/{código}</c> si llegó un código (5.10).</param>
public sealed record LandingViewModel(IReadOnlyList<string> Words, string AppUrl);
