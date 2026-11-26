using System.Text.RegularExpressions;
using Local133.Domain.Merchants;

namespace Local133.UnitTests.Merchants;

/// <summary>4.0 y ajustes 2026-11-26: nombre de la persona, solo letras y espacios (reglas, normalización y saludo).</summary>
public class PersonNameTests
{
    [Theory]
    [InlineData("María")]
    [InlineData("José Ángel")]
    [InlineData("María José Núñez")]
    [InlineData("Françoise Müller")]
    [InlineData("Peña Nieto")]
    [InlineData("Güero")]
    [InlineData("  María   López  ")]
    public void Acepta_letras_con_acentos_y_espacios(string name) =>
        Assert.True(PersonName.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("-'")]
    [InlineData("María 2")]
    [InlineData("Juan2")]
    [InlineData("Ma.")]
    [InlineData("Ma. Luisa")]
    [InlineData("O'Brien")]
    [InlineData("O’Brien")]
    [InlineData("Ana-Luisa")]
    [InlineData("Ana × Luis")]
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
    [InlineData("Ana\tLucía", "Ana Lucía")]
    [InlineData(null, "")]
    public void Normaliza_espacios(string? raw, string expected) =>
        Assert.Equal(expected, PersonName.Normalize(raw));

    [Theory]
    [InlineData("Ma. José", "Ma José")]
    [InlineData("O'Brien", "O Brien")]
    [InlineData("  María  O’Brien ", "María O Brien")]
    [InlineData("Ana-Luisa", "Ana Luisa")]
    [InlineData("Juan 2", "Juan")]
    [InlineData("María José Núñez", "María José Núñez")]
    [InlineData("...", "")]
    [InlineData(null, "")]
    public void Clean_cambia_lo_que_no_es_letra_por_espacio(string? raw, string expected)
    {
        Assert.Equal(expected, PersonName.Clean(raw));
        if (expected.Length > 0)
        {
            Assert.True(PersonName.IsValid(PersonName.Clean(raw)));
        }
    }

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
        Assert.Matches(regex, "María Núñez");
        Assert.DoesNotMatch(regex, "María1");
        Assert.DoesNotMatch(regex, "O'Brien");
    }

    [Fact]
    public void Register_guarda_el_nombre_normalizado()
    {
        var merchant = Merchant.Register("user-1", DateTimeOffset.UtcNow, "  Ana   Núñez ");

        Assert.Equal("Ana Núñez", merchant.OwnerName);
        Assert.Equal(MerchantStatus.PendingBankAccount, merchant.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("O'Brien")]
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
