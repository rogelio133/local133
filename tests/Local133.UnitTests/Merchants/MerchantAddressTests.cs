using Local133.Domain.Merchants;

namespace Local133.UnitTests.Merchants;

/// <summary>4.1: dirección o ubicación del puesto, en texto libre de hasta 3 renglones.</summary>
public class MerchantAddressTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Tianguis de los martes, pasillo 4")]
    [InlineData("Av. Juárez #12, local 3\nCol. Centro\nPuebla, Pue. C.P. 72000")]
    [InlineData("Mercado Sur 🌮")]
    [InlineData("Calle 5 de Mayo s/n\r\n\r\n\r\nEsquina con Hidalgo")] // renglones vacíos no cuentan
    public void Acepta_vacio_o_texto_libre_de_hasta_3_renglones(string? address) =>
        Assert.True(MerchantAddress.IsValid(address));

    [Theory]
    [InlineData("Uno\nDos\nTres\nCuatro")]
    [InlineData("Pasillo\t4\u0007")]
    [InlineData("Local\u00003")]
    public void Rechaza_mas_de_3_renglones_o_caracteres_de_control(string address) =>
        Assert.False(MerchantAddress.IsValid(address));

    [Fact]
    public void Acepta_hasta_200_caracteres_ya_normalizada()
    {
        Assert.True(MerchantAddress.IsValid(new string('a', MerchantAddress.MaxLength)));
        Assert.False(MerchantAddress.IsValid(new string('a', MerchantAddress.MaxLength + 1)));
        // Espacios repetidos y saltos \r\n no cuentan de más.
        Assert.True(MerchantAddress.IsValid(new string('a', 99) + "  \r\n  " + new string('b', 100)));
    }

    [Theory]
    [InlineData("  Av.   Juárez  #12 \r\n\r\n  Col. Centro  ", "Av. Juárez #12\nCol. Centro")]
    [InlineData("Uno\rDos", "Uno\nDos")]
    [InlineData("Pasillo\t4", "Pasillo 4")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normaliza_renglones_y_espacios(string? raw, string expected) =>
        Assert.Equal(expected, MerchantAddress.Normalize(raw));
}
