namespace Local133.LandingPage.Tests;

/// <summary>
/// Logo, íconos, Bootstrap Icons y <c>rotating-word.js</c> de la portada son copias de <c>Local133.Web/wwwroot</c> (los static web
/// assets no sirven archivos enlazados). El logo y los íconos los escribe <c>tools/brand-assets.ps1</c> en las dos apps;
/// si se cambia otro original, hay que copiarlo también a la portada.
/// </summary>
public class SharedAssetsTests
{
    [Theory]
    [InlineData("favicon.ico")]
    [InlineData("apple-touch-icon.png")]
    [InlineData("icon-192.png")]
    [InlineData("js/rotating-word.js")]
    [InlineData("img/local133-logo-48.png")]
    [InlineData("img/local133-logo-96.png")]
    [InlineData("img/local133-logo-144.png")]
    [InlineData("img/local133-logo-160.png")]
    [InlineData("img/local133-logo-320.png")]
    [InlineData("img/local133-logo-480.png")]
    // Íconos del recibo y del catálogo públicos (2026-11-27); en el portal los pone LibMan.
    [InlineData("lib/bootstrap-icons/font/bootstrap-icons.min.css")]
    [InlineData("lib/bootstrap-icons/font/fonts/bootstrap-icons.woff2")]
    [InlineData("lib/bootstrap-icons/font/fonts/bootstrap-icons.woff")]
    public void La_copia_de_la_portada_es_igual_a_la_de_Local133_Web(string path)
    {
        var root = RepositoryRoot();
        var original = File.ReadAllBytes(Path.Combine(root, "src", "Local133.Web", "wwwroot", path));
        var copy = File.ReadAllBytes(Path.Combine(root, "src", "Local133.LandingPage", "wwwroot", path));

        Assert.True(original.AsSpan().SequenceEqual(copy),
            $"src/Local133.LandingPage/wwwroot/{path} difiere de src/Local133.Web/wwwroot/{path}: cópialo de nuevo.");
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Local133.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("No se encontró Local133.sln arriba de " + AppContext.BaseDirectory);
    }
}
