using Local133.Domain.Merchants;

namespace Local133.UnitTests.Merchants;

/// <summary>5.10: nombre del catálogo público en <c>/tienda/{slug}</c>.</summary>
public class PublicSlugTests
{
    [Theory]
    [InlineData("Tacos Don Ramón #2", "tacos-don-ramon-2")]
    [InlineData("  Doña Pepa  ", "dona-pepa")]
    [InlineData("Pingüino---Feliz", "pinguino-feliz")]
    [InlineData("--abc--", "abc")]
    [InlineData("ÁÉÍÓÚ ÜÑ", "aeiou-un")]
    [InlineData("tacos.el/güero", "tacos-el-guero")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("###", "")]
    public void Se_normaliza(string? input, string expected) => Assert.Equal(expected, PublicSlug.Normalize(input));

    [Theory]
    [InlineData("abc", true)]
    [InlineData("tacos-don-ramon", true)]
    [InlineData("a1-b2", true)]
    [InlineData("ab", false)]
    [InlineData("Tacos", false)]
    [InlineData("tacos--ramon", false)]
    [InlineData("-tacos", false)]
    [InlineData("tacos-", false)]
    [InlineData("tacos ramon", false)]
    [InlineData("ramón", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Se_valida(string? slug, bool expected) => Assert.Equal(expected, PublicSlug.IsValid(slug));

    [Fact]
    public void El_largo_maximo_es_40()
    {
        Assert.True(PublicSlug.IsValid(new string('a', 40)));
        Assert.False(PublicSlug.IsValid(new string('a', 41)));
    }

    [Fact]
    public void La_propuesta_recorta_sin_dejar_guion_al_final()
    {
        // 39 letras + espacio + más texto: el corte a 40 cae en el guion, que se quita.
        var name = new string('a', 39) + " bcd";

        var slug = PublicSlug.Suggest(name);

        Assert.Equal(new string('a', 39), slug);
    }

    [Theory]
    [InlineData("Tacos Don Ramón", "tacos-don-ramon")]
    [InlineData("AB", null)]
    [InlineData(null, null)]
    [InlineData("¿¿??", null)]
    public void Propone_desde_el_negocio(string? business, string? expected) =>
        Assert.Equal(expected, PublicSlug.Suggest(business));

    [Fact]
    public void La_ruta_es_tienda() => Assert.Equal("/tienda/tacos", PublicSlug.PathFor("tacos"));
}
