using System.Text.RegularExpressions;
using Local133.Domain.Merchants;

namespace Local133.UnitTests.Merchants;

/// <summary>5.8: código de invitación (6 caracteres sin 0, O, 1 ni I).</summary>
public class ReferralCodeTests
{
    [Fact]
    public void Se_genera_con_el_alfabeto_y_el_largo()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = ReferralCode.Generate();
            Assert.Equal(ReferralCode.Length, code.Length);
            Assert.All(code, c => Assert.Contains(c, ReferralCode.Alphabet));
            Assert.True(ReferralCode.IsValid(code));
        }
    }

    [Fact]
    public void El_alfabeto_no_tiene_caracteres_confusos()
    {
        Assert.Equal(32, ReferralCode.Alphabet.Length);
        Assert.Equal(32, ReferralCode.Alphabet.Distinct().Count());
        Assert.DoesNotContain('0', ReferralCode.Alphabet);
        Assert.DoesNotContain('O', ReferralCode.Alphabet);
        Assert.DoesNotContain('1', ReferralCode.Alphabet);
        Assert.DoesNotContain('I', ReferralCode.Alphabet);
    }

    [Theory]
    [InlineData(" abc234 ", "ABC234")]
    [InlineData("ABC-234", "ABC234")]
    [InlineData("a b c 2 3 4", "ABC234")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Se_normaliza(string? input, string expected) => Assert.Equal(expected, ReferralCode.Normalize(input));

    [Theory]
    [InlineData("ABC234", true)]
    [InlineData("ZZ9988", true)]
    [InlineData("abc234", false)] // sin normalizar
    [InlineData("ABC23", false)]
    [InlineData("ABC2345", false)]
    [InlineData("ABC10O", false)] // 1, 0 y O no existen
    [InlineData("ABCI34", false)]
    [InlineData(null, false)]
    public void Valida_la_forma(string? code, bool valid) => Assert.Equal(valid, ReferralCode.IsValid(code));

    [Theory]
    [InlineData("abc234", true)]
    [InlineData("  ABC234 ", true)]
    [InlineData("ABC10O", false)]
    [InlineData("ABC-234", false)]
    [InlineData("ABC23", false)]
    public void El_patron_del_campo_acepta_minusculas_y_espacios(string input, bool matches) =>
        Assert.Equal(matches, Regex.IsMatch(input, ReferralCode.InputPattern));

    [Fact]
    public void El_vendedor_nace_con_codigo()
    {
        var generated = Merchant.Register("user-1", DateTimeOffset.UtcNow, "María López");
        var given = Merchant.Register("user-2", DateTimeOffset.UtcNow, "María López", "XYZ789");

        Assert.True(ReferralCode.IsValid(generated.ReferralCode));
        Assert.Equal("XYZ789", given.ReferralCode);
        Assert.Throws<ArgumentException>(() => Merchant.Register("user-3", DateTimeOffset.UtcNow, "María López", "abc"));
    }

    [Fact]
    public void La_liga_lleva_el_codigo() => Assert.Equal("/r/ABC234", ReferralCode.PathFor("ABC234"));
}
