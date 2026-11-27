using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Números del resumen mensual (5.10, §7 del diseño; por plan desde la 5.16): lo cobrado en el mes, cuánto se habría
/// pagado de comisión con una terminal (estimación con <see cref="SystemSettings.ReferenceTerminalFeeRate"/> más IVA) y
/// el precio mensual del plan con el que se compara. «En noviembre cobraste $18,400 en 212 ventas. Con una terminal
/// habrías pagado unos $747».
/// </summary>
/// <param name="Collected">Suma de los pagos recibidos en el mes.</param>
/// <param name="SalesCount">Ventas distintas con al menos un pago en el mes.</param>
/// <param name="EstimatedTerminalFee">Comisión estimada de una terminal, IVA incluido, redondeada a pesos.</param>
/// <param name="FeeRate">Tasa de referencia sin IVA (0.035 = 3.5 %).</param>
/// <param name="Plan">
/// Plan con el que se compara: el que aplica hoy (la prueba es Pro) o, si venció, Básico («Con Básico pagarías $99»).
/// </param>
/// <param name="PlanMonthlyPrice">Precio de un mes de <paramref name="Plan"/> para este vendedor (de fundador si lo es), IVA incluido.</param>
/// <param name="IsExpired">El plan está vencido: el texto invita a elegir un plan.</param>
public sealed record MonthlySummary(
    decimal Collected,
    int SalesCount,
    decimal EstimatedTerminalFee,
    decimal FeeRate,
    SubscriptionPlan Plan,
    decimal PlanMonthlyPrice,
    bool IsExpired)
{
    /// <param name="currentPlan">
    /// Plan que aplica hoy (<c>PlanStatus.Plan</c>); <c>null</c> en el onboarding (sin vencer: se compara con Pro, el
    /// de la prueba que empieza con su primer cobro).
    /// </param>
    /// <param name="isExpired">El plan venció: se compara con Básico.</param>
    public static MonthlySummary For(
        decimal collected, int salesCount, SystemSettings settings, bool isFounder, SubscriptionPlan? currentPlan, bool isExpired)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegative(collected);
        ArgumentOutOfRangeException.ThrowIfNegative(salesCount);

        var fee = Math.Round(
            collected * settings.ReferenceTerminalFeeRate * (1 + settings.VatRate), 0, MidpointRounding.AwayFromZero);
        var plan = isExpired ? SubscriptionPlan.Basic : currentPlan ?? SubscriptionPlan.Pro;
        return new MonthlySummary(
            collected,
            salesCount,
            fee,
            settings.ReferenceTerminalFeeRate,
            plan,
            SubscriptionPricing.MonthlyPrice(plan, settings, isFounder),
            isExpired);
    }
}
