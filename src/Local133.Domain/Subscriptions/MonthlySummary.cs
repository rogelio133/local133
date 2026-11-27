using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Números del resumen mensual (5.10, §7 del diseño): lo cobrado en el mes, cuánto se habría pagado de comisión con
/// una terminal (estimación con <see cref="SystemSettings.ReferenceTerminalFeeRate"/> más IVA) y el precio mensual de
/// Pro con el que se compara. «En noviembre cobraste $18,400 en 212 ventas. Con una terminal habrías pagado unos $747».
/// </summary>
/// <param name="Collected">Suma de los pagos recibidos en el mes.</param>
/// <param name="SalesCount">Ventas distintas con al menos un pago en el mes.</param>
/// <param name="EstimatedTerminalFee">Comisión estimada de una terminal, IVA incluido, redondeada a pesos.</param>
/// <param name="FeeRate">Tasa de referencia sin IVA (0.035 = 3.5 %).</param>
/// <param name="ProMonthlyPrice">Precio de un mes de Pro para este vendedor (de fundador si lo es), IVA incluido.</param>
public sealed record MonthlySummary(
    decimal Collected, int SalesCount, decimal EstimatedTerminalFee, decimal FeeRate, decimal ProMonthlyPrice)
{
    public static MonthlySummary For(decimal collected, int salesCount, SystemSettings settings, bool isFounder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegative(collected);
        ArgumentOutOfRangeException.ThrowIfNegative(salesCount);

        var fee = Math.Round(
            collected * settings.ReferenceTerminalFeeRate * (1 + settings.VatRate), 0, MidpointRounding.AwayFromZero);
        return new MonthlySummary(
            collected,
            salesCount,
            fee,
            settings.ReferenceTerminalFeeRate,
            isFounder ? settings.ProFounderMonthlyPrice : settings.ProMonthlyPrice);
    }
}
