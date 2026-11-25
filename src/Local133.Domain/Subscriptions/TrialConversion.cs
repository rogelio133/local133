namespace Local133.Domain.Subscriptions;

/// <summary>
/// Conversión de prueba a pago (tablero del admin, 6.4): de los vendedores cuya prueba ya terminó, cuántos pagaron al
/// menos un cargo (también antes de terminarla, o abonado a mano).
/// </summary>
public readonly record struct TrialConversion(int Ended, int Paid)
{
    /// <summary>Porcentaje entero (redondeo al más cercano); <c>null</c> si ninguna prueba ha terminado.</summary>
    public int? Percent => Ended <= 0 ? null : (int)Math.Round(100m * Paid / Ended, MidpointRounding.AwayFromZero);
}
