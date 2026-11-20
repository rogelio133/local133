using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>Dónde va el vendedor del plan gratuito con sus cobros del mes (§5 del diseño de suscripciones).</summary>
public enum ChargeAllowanceState
{
    /// <summary>Por debajo del aviso (80 %).</summary>
    Normal,

    /// <summary>Llegó al aviso (80 % del límite) y aún no al límite.</summary>
    NearLimit,

    /// <summary>Llegó al límite del mes: no genera cobros nuevos hasta el día 1 o hasta que pague.</summary>
    Exhausted,

    /// <summary>Llegó al tope técnico de cobros generados (aunque no se paguen): tampoco genera cobros nuevos.</summary>
    GenerationCapReached,
}

/// <summary>
/// Límite mensual del plan gratuito: <c>FreeMonthlyPaidLimit</c> cobros concretados (ventas con <c>PaidAt</c> en el
/// mes calendario de México) y un tope técnico de <c>FreeMonthlyGeneratedLimit</c> cobros generados. Al llegar al
/// límite ya no se generan cobros; no hay cobros de cortesía (se quitaron el 2026-11-20). Solo aplica cuando el plan
/// tiene <see cref="PlanEntitlements.HasMonthlyChargeLimit"/>; lo ya generado se puede pagar siempre.
/// </summary>
public sealed record MonthlyChargeAllowance(
    int Paid,
    int Generated,
    int PaidLimit,
    int GeneratedLimit)
{
    /// <summary>Porcentaje del límite en que se avisa.</summary>
    public const int WarningPercent = 80;

    /// <summary>Concretados con los que se avisa (80 % del límite, redondeado hacia arriba).</summary>
    public int WarningThreshold => WarningThresholdFor(PaidLimit);

    /// <summary>Cobros que le quedan en el mes (0 al llegar al límite).</summary>
    public int Remaining => Math.Max(PaidLimit - Paid, 0);

    public ChargeAllowanceState State =>
        Paid >= PaidLimit ? ChargeAllowanceState.Exhausted
        : Generated >= GeneratedLimit ? ChargeAllowanceState.GenerationCapReached
        : Paid >= WarningThreshold ? ChargeAllowanceState.NearLimit
        : ChargeAllowanceState.Normal;

    /// <summary>Puede generar un cobro nuevo (QR o celular).</summary>
    public bool CanGenerate => State is not (ChargeAllowanceState.Exhausted or ChargeAllowanceState.GenerationCapReached);

    public static MonthlyChargeAllowance For(int paid, int generated, SystemSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegative(paid);
        ArgumentOutOfRangeException.ThrowIfNegative(generated);
        return new(paid, generated, settings.FreeMonthlyPaidLimit, settings.FreeMonthlyGeneratedLimit);
    }

    /// <summary>
    /// Umbrales del gratuito (aviso y límite) que se cruzaron al pasar de <paramref name="previousPaid"/> a
    /// <paramref name="currentPaid"/> concretados: los que caen en <c>(previousPaid, currentPaid]</c>, de menor a mayor.
    /// </summary>
    public static IReadOnlyList<int> ThresholdsCrossed(int previousPaid, int currentPaid, SystemSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var limit = settings.FreeMonthlyPaidLimit;
        int[] thresholds = [WarningThresholdFor(limit), limit];
        return thresholds.Distinct().Where(t => t > previousPaid && t <= currentPaid).Order().ToList();
    }

    /// <summary>Se cruzó el uso justo de Pro (<c>ProFairUseMonthlyPaid</c>): se alerta al admin, sin bloquear.</summary>
    public static bool ProFairUseCrossed(int previousPaid, int currentPaid, SystemSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return previousPaid < settings.ProFairUseMonthlyPaid && currentPaid >= settings.ProFairUseMonthlyPaid;
    }

    private static int WarningThresholdFor(int limit) => (int)Math.Ceiling(limit * WarningPercent / 100m);
}
