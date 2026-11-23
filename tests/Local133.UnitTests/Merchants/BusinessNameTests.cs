using System.Text.RegularExpressions;
using Local133.Domain.Merchants;
using Local133.Domain.Sales;

namespace Local133.UnitTests.Merchants;

/// <summary>4.1: nombre del negocio en «Mi información» (los mismos caracteres que el concepto del cobro).</summary>
public class BusinessNameTests
{
    [Theory]
    [InlineData("Tacos Don Ramón")]
    [InlineData("Taquería El Güero")]
    [InlineData("Puesto #4")]
    [InlineData("Jugos y licuados (Mercado Sur)")]
    [InlineData("Ropa 2x1, S.A. de C.V.")]
    [InlineData("Frutas/Verduras - Doña Ñeca")]
    [InlineData("7 Mares")]
    [InlineData("  Tacos   Don Ramón  ")]
    public void Acepta_letras_numeros_espacios_y_signos_del_concepto(string name) =>
        Assert.True(BusinessName.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("#/()")]
    [InlineData("Tacos · Don Ramón")]
    [InlineData("<b>Tacos</b>")]
    [InlineData("Tacos 🌮")]
    [InlineData("Mary's")]
    [InlineData("Tacos & Tortas")]
    [InlineData("Françoise")]
    public void Rechaza_vacio_sin_letras_con_separador_o_con_otros_caracteres(string? name) =>
        Assert.False(BusinessName.IsValid(name));

    [Fact]
    public void Acepta_hasta_120_caracteres()
    {
        Assert.True(BusinessName.IsValid(new string('a', BusinessName.MaxLength)));
        Assert.False(BusinessName.IsValid(new string('a', BusinessName.MaxLength + 1)));
        Assert.True(BusinessName.IsValid("  " + new string('a', BusinessName.MaxLength) + "  "));
    }

    [Theory]
    [InlineData("  Tacos   Don\tRamón ", "Tacos Don Ramón")]
    [InlineData(null, "")]
    public void Normaliza_espacios(string? raw, string expected) =>
        Assert.Equal(expected, BusinessName.Normalize(raw));

    [Fact]
    public void Un_nombre_valido_nunca_lleva_el_separador_del_concepto()
    {
        // ConceptTextOf corta en el último separador: un nombre válido no puede traerlo.
        Assert.DoesNotMatch(new Regex(BusinessName.Pattern), Sale.ConceptSeparator.Trim());
        Assert.Equal("2 tacos", Sale.ConceptTextOf(Sale.BuildConcept("Tacos Don Ramón", "2 tacos")));
    }
}
