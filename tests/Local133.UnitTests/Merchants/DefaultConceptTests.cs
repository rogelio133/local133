using Local133.Domain.Merchants;
using Local133.Domain.Sales;

namespace Local133.UnitTests.Merchants;

/// <summary>Ajustes 2026-11-26: concepto por defecto de «Mi información» (mismas reglas que el concepto del cobro).</summary>
public class DefaultConceptTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Consumo  ", "Consumo")]
    public void Normaliza_recortando_y_vacio_es_nulo(string? raw, string? expected) =>
        Assert.Equal(expected, DefaultConcept.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("Consumo")]
    [InlineData("Pedido #12, mesa 3/4 (para llevar)")]
    public void Acepta_vacio_o_un_concepto_valido(string? value) =>
        Assert.True(DefaultConcept.IsValid(value));

    [Theory]
    [InlineData("Tacos · Consumo")]
    [InlineData("Taco 🌮")]
    [InlineData("<b>Hola</b>")]
    public void Rechaza_caracteres_que_no_acepta_el_concepto(string value) =>
        Assert.False(DefaultConcept.IsValid(value));

    [Fact]
    public void Usa_el_largo_y_el_patron_del_concepto_del_cobro()
    {
        Assert.Equal(Sale.ConceptTextMaxLength, DefaultConcept.MaxLength);
        Assert.Equal(Sale.ConceptTextPattern, DefaultConcept.Pattern);
        Assert.True(DefaultConcept.IsValid(new string('a', DefaultConcept.MaxLength)));
        Assert.False(DefaultConcept.IsValid(new string('a', DefaultConcept.MaxLength + 1)));
    }
}
