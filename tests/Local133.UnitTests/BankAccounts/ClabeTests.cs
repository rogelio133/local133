using Local133.Domain.BankAccounts;

namespace Local133.UnitTests.BankAccounts;

public class ClabeTests
{
    /// <summary>CLABE de prueba de Pey (activa de inmediato); dígito verificador calculado a mano: 3.</summary>
    private const string PeyTestClabe = "012180015826225713";

    /// <summary>Ejemplo público de CLABE; dígito verificador calculado a mano: 9.</summary>
    private const string ExampleClabe = "032180000118359719";

    [Theory]
    [InlineData(PeyTestClabe)]
    [InlineData(ExampleClabe)]
    public void Acepta_CLABE_con_digito_verificador_correcto(string clabe) => Assert.True(Clabe.IsValid(clabe));

    [Theory]
    [InlineData(PeyTestClabe, 3)]
    [InlineData(ExampleClabe, 9)]
    public void Calcula_el_digito_verificador(string clabe, int expected) => Assert.Equal(expected, Clabe.CheckDigit(clabe));

    [Theory]
    [InlineData("012180015826225710")]
    [InlineData("012180015826225719")]
    [InlineData("032180000118359710")]
    public void Rechaza_digito_verificador_incorrecto(string clabe) => Assert.False(Clabe.IsValid(clabe));

    [Fact]
    public void Rechaza_un_digito_alterado_en_medio() => Assert.False(Clabe.IsValid("012180015826235713"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01218001582622571")]
    [InlineData("0121800158262257130")]
    [InlineData("01218001582622571A")]
    [InlineData("012 180 015826225713")]
    [InlineData("０１２１８００１５８２６２２５７１３")]
    public void Rechaza_formato_invalido(string? clabe) => Assert.False(Clabe.IsValid(clabe));

    [Theory]
    [InlineData("012 180 01582622571 3", PeyTestClabe)]
    [InlineData("012-180-015826225713", PeyTestClabe)]
    [InlineData("  012180015826225713\t", PeyTestClabe)]
    [InlineData(null, "")]
    public void Normaliza_quitando_espacios_y_guiones(string? input, string expected) => Assert.Equal(expected, Clabe.Normalize(input));

    [Fact]
    public void Normalizada_la_CLABE_agrupada_es_valida() => Assert.True(Clabe.IsValid(Clabe.Normalize("012 180 01582622571 3")));

    [Fact]
    public void Enmascara_mostrando_solo_los_ultimos_cuatro()
    {
        Assert.Equal("•••• 5713", Clabe.Mask(PeyTestClabe));
        Assert.DoesNotContain("0121800158", Clabe.Mask(PeyTestClabe));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("123", "•••• 123")]
    public void Enmascara_valores_cortos_o_vacios(string? input, string expected) => Assert.Equal(expected, Clabe.Mask(input));
}
