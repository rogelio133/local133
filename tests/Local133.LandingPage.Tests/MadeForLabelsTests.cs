using Local133.LandingPage.Models;

namespace Local133.LandingPage.Tests;

public class MadeForLabelsTests
{
    [Theory]
    [InlineData("local", "Locales")]
    [InlineData("puesto", "Puestos")]
    [InlineData("tienda", "Tiendas")]
    [InlineData("kiosko", "Kioskos")]
    [InlineData("negocio", "Negocios")]
    [InlineData("consultorio", "Consultorios")]
    [InlineData("café", "Cafés")]
    [InlineData("taller", "Talleres")]
    [InlineData(" ñandú ", "Ñandús")]
    public void Plural_con_mayuscula_inicial(string word, string expected)
    {
        Assert.Equal(expected, MadeForLabels.Plural(word));
    }
}
