namespace Local133.LandingPage.Tests;

/// <summary>
/// Logo, íconos y <c>rotating-word.js</c> de la portada son copias de <c>Local133.Web/wwwroot</c> (los static web
/// assets no sirven archivos enlazados). Si se cambia el original, hay que copiarlo también a la portada.
/// </summary>
public class SharedAssetsTests
{
    [Theory]
    [InlineData("local133.svg")]
    [InlineData("favicon.ico")]
    [InlineData("apple-touch-icon.png")]
    [InlineData("js/rotating-word.js")]
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
