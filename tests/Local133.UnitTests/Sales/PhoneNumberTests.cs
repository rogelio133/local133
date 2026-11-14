using Local133.Domain.Sales;

namespace Local133.UnitTests.Sales;

/// <summary>Celular del pagador para el cobro por notificación push.</summary>
public class PhoneNumberTests
{
    [Theory]
    [InlineData("5512345678", "5512345678")]
    [InlineData("55 1234 5678", "5512345678")]
    [InlineData("55-1234-5678", "5512345678")]
    [InlineData("(55) 1234-5678", "5512345678")]
    [InlineData("55.1234.5678", "5512345678")]
    [InlineData("+52 55 1234 5678", "5512345678")]
    [InlineData("+525512345678", "5512345678")]
    [InlineData(" 5512345678 ", "5512345678")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Normalize_quita_separadores_y_la_lada_de_Mexico(string? input, string expected) =>
        Assert.Equal(expected, PhoneNumber.Normalize(input));

    [Fact]
    public void Normalize_no_quita_un_52_sin_signo_de_mas()
    {
        // "52" sin "+" puede ser el inicio de un número local; se deja y la validación lo rechaza si no son 10.
        Assert.Equal("525512345678", PhoneNumber.Normalize("52 55 1234 5678"));
        Assert.False(PhoneNumber.IsValid(PhoneNumber.Normalize("52 55 1234 5678")));
    }

    [Theory]
    [InlineData("5512345678")]
    [InlineData("8112345678")]
    [InlineData("0000000000")]
    public void IsValid_acepta_10_digitos(string phone) => Assert.True(PhoneNumber.IsValid(phone));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("551234567")]
    [InlineData("55123456789")]
    [InlineData("55 1234 5678")]
    [InlineData("55123456a8")]
    [InlineData("５５１２３４５６７８")] // dígitos de ancho completo: no son ASCII
    public void IsValid_rechaza_lo_que_no_son_10_digitos_ascii(string? phone) => Assert.False(PhoneNumber.IsValid(phone));

    [Theory]
    [InlineData("5512345678", "•••• 5678")]
    [InlineData("123", "•••• 123")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Mask_deja_solo_los_ultimos_4(string? phone, string expected) => Assert.Equal(expected, PhoneNumber.Mask(phone));
}
