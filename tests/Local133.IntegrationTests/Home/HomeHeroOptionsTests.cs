using Local133.Web.Hosting;

namespace Local133.IntegrationTests.Home;

/// <summary>Limpieza de <c>Home:RotatingWords</c>. Sin SQL Server: corren en CI.</summary>
public class HomeHeroOptionsTests
{
    private static IReadOnlyList<string> WordsOf(params string[] words) =>
        new HomeHeroOptions { RotatingWords = words }.GetWords();

    [Fact]
    public void Conserva_el_orden_y_recorta_espacios() =>
        Assert.Equal(["local", "puesto", "tienda"], WordsOf(" local ", "puesto", "tienda  "));

    [Fact]
    public void Descarta_vacias_largas_y_repetidas() =>
        Assert.Equal(["local", "consultorio"], WordsOf("local", "", "   ", "consultorio", "Local", new string('a', HomeHeroOptions.MaxWordLength + 1)));

    [Fact]
    public void Acepta_palabras_del_largo_maximo() =>
        Assert.Equal([new string('a', HomeHeroOptions.MaxWordLength)], WordsOf(new string('a', HomeHeroOptions.MaxWordLength)));

    [Fact]
    public void Toma_como_maximo_diez_palabras()
    {
        var words = WordsOf(Enumerable.Range(1, 15).Select(i => $"p{i}").ToArray());

        Assert.Equal(HomeHeroOptions.MaxWords, words.Count);
        Assert.Equal("p1", words[0]);
        Assert.Equal("p10", words[^1]);
    }

    [Fact]
    public void Sin_palabras_validas_regresa_local()
    {
        Assert.Equal([HomeHeroOptions.DefaultWord], WordsOf());
        Assert.Equal([HomeHeroOptions.DefaultWord], WordsOf("", " "));
        Assert.Equal(["local"], new HomeHeroOptions { RotatingWords = null! }.GetWords());
    }
}
