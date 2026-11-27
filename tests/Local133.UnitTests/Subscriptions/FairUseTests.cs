using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;

namespace Local133.UnitTests.Subscriptions;

/// <summary>Uso justo (5.14): 5,000 cobros concretados al mes en Básico y Pro; se alerta al cruzarlo, una vez.</summary>
public class FairUseTests
{
    private static readonly SystemSettings Defaults = new();

    [Theory]
    [InlineData(4999, 5000, true)]
    [InlineData(5000, 5001, false)]
    [InlineData(4998, 4999, false)]
    [InlineData(0, 6000, true)]
    public void Se_cruza_solo_al_llegar_al_limite(int previous, int current, bool crossed)
    {
        Assert.Equal(crossed, FairUse.Crossed(previous, current, Defaults));
    }

    [Fact]
    public void Usa_el_parametro_del_admin()
    {
        var settings = new SystemSettings { FairUseMonthlyPaid = 3 };

        Assert.True(FairUse.Crossed(2, 3, settings));
        Assert.False(FairUse.Crossed(3, 4, settings));
    }

    [Fact]
    public void Sin_ajustes_se_rechaza() =>
        Assert.Throws<ArgumentNullException>(() => FairUse.Crossed(0, 1, null!));
}
