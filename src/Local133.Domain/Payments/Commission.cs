using Local133.Domain.Pey;

namespace Local133.Domain.Payments;

/// <summary>
/// Comisión de la plataforma sobre cada pago recibido. La tasa se copia en el <see cref="Payment"/> al cobrarse:
/// cambiar la tasa del vendedor o la global nunca altera pagos existentes. El IVA se calcula en el estado de cuenta.
/// </summary>
public static class Commission
{
    /// <summary>Tasa que aplica al vendedor: la suya si tiene, si no la global de <c>SystemSettings</c>.</summary>
    public static decimal EffectiveRate(decimal? merchantOverride, decimal globalRate)
    {
        var rate = merchantOverride ?? globalRate;
        EnsureValidRate(rate, nameof(merchantOverride));
        return rate;
    }

    /// <summary>
    /// Comisión en MXN redondeada a centavos (punto medio hacia arriba): $150 × 3 % = $4.50; $0.01 × 3 % = $0.00.
    /// </summary>
    public static decimal Calculate(decimal amount, decimal rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        EnsureValidRate(rate, nameof(rate));
        return Math.Round(amount * rate, PeyLimits.AmountDecimals, MidpointRounding.AwayFromZero);
    }

    private static void EnsureValidRate(decimal rate, string paramName)
    {
        if (rate is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(paramName, rate, "La tasa de comisión debe estar entre 0 y 1.");
        }
    }
}
