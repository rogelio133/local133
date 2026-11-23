using System.Text.RegularExpressions;
using Local133.Domain.Merchants;

namespace Local133.UnitTests.Merchants;

/// <summary>4.0: nombre de la persona en el alta (reglas, normalización y saludo).</summary>
public class PersonNameTests
{
    [Theory]
    [InlineData("María")]
    [InlineData("José Ángel")]
    [InlineData("O'Brien")]
    [InlineData("O’Brien")]
    [InlineData("Ana-Lucía")]
    [InlineData("Ma. Luisa")]
    [InlineData("Françoise Müller")]
    [InlineData("Peña Nieto")]
    [InlineData("  María   López  ")]
    public void Acepta_letras_con_acentos_espacios_punto_guion_y_apostrofo(string name) =>
        Assert.True(PersonName.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("-'")]
    [InlineData("María 2")]
    [InlineData("María 🙂")]
    [InlineData("<b>Ana</b>")]
    [InlineData("Ana_López")]
    [InlineData("ana@correo.mx")]
    public void Rechaza_vacio_sin_letras_o_con_otros_caracteres(string? name) =>
        Assert.False(PersonName.IsValid(name));

    [Fact]
    public void Acepta_hasta_100_caracteres()
    {
        Assert.True(PersonName.IsValid(new string('a', PersonName.MaxLength)));
        Assert.False(PersonName.IsValid(new string('a', PersonName.MaxLength + 1)));
        // Los espacios de los extremos no cuentan.
        Assert.True(PersonName.IsValid("  " + new string('a', PersonName.MaxLength) + "  "));
    }

    [Theory]
    [InlineData("  María   José  López ", "María José López")]
    [InlineData("O’Brien", "O'Brien")]
    [InlineData("Ana\tLucía", "Ana Lucía")]
    [InlineData(null, "")]
    public void Normaliza_espacios_y_apostrofo(string? raw, string expected) =>
        Assert.Equal(expected, PersonName.Normalize(raw));

    [Theory]
    [InlineData("María José López", "María")]
    [InlineData("  Rosa ", "Rosa")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void El_primer_nombre_es_la_primera_palabra(string? name, string? expected) =>
        Assert.Equal(expected, PersonName.FirstName(name));

    [Fact]
    public void El_patron_compila_igual_y_exige_toda_la_cadena()
    {
        // jQuery Validation lo usa tal cual (sin bandera u) y exige que la coincidencia cubra todo el valor.
        var regex = new Regex(PersonName.Pattern);
        Assert.Matches(regex, "María O'Brien");
        Assert.DoesNotMatch(regex, "María1");
    }

    [Fact]
    public void Register_guarda_el_nombre_normalizado()
    {
        var merchant = Merchant.Register("user-1", DateTimeOffset.UtcNow, "  Ana   O’Brien ");

        Assert.Equal("Ana O'Brien", merchant.OwnerName);
        Assert.Equal(MerchantStatus.PendingBankAccount, merchant.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    public void Register_rechaza_un_nombre_invalido(string name) =>
        Assert.Throws<ArgumentException>(() => Merchant.Register("user-1", DateTimeOffset.UtcNow, name));

    [Theory]
    [InlineData("María López", "Tacos Don Ramón", "María")]
    [InlineData(null, "Tacos Don Ramón", "Tacos Don Ramón")]
    [InlineData("  ", " Jugos Lupita ", "Jugos Lupita")]
    [InlineData(null, null, null)]
    [InlineData(null, "  ", null)]
    public void El_saludo_usa_el_nombre_luego_el_negocio(string? owner, string? business, string? expected)
    {
        Assert.Equal(expected, Merchant.GreetingNameFor(owner, business));
        Assert.Equal(expected, new Merchant { OwnerName = owner, BusinessName = business }.GreetingName);
    }
}
