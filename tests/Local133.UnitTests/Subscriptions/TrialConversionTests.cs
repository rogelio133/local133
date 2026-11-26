using Local133.Domain.Subscriptions;

namespace Local133.UnitTests.Subscriptions;

/// <summary>6.4: conversión de prueba a pago del tablero.</summary>
public class TrialConversionTests
{
    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 10, 100)]
    [InlineData(3, 1, 33)]
    [InlineData(3, 2, 67)]
    [InlineData(8, 1, 13)] // 12.5 redondea hacia arriba
    public void Porcentaje_redondeado(int ended, int paid, int? expected)
    {
        Assert.Equal(expected, new TrialConversion(ended, paid).Percent);
    }
}
