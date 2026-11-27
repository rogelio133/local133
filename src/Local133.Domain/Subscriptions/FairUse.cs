using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>
/// Uso justo (§1 del diseño de planes Básico y Pro): <c>FairUseMonthlyPaid</c> cobros concretados al mes (ventas con
/// <c>PaidAt</c> en el mes calendario de México), en los dos planes. Al pasarlos se alerta al admin, sin bloquear.
/// </summary>
public static class FairUse
{
    /// <summary>Se cruzó el uso justo al pasar de <paramref name="previousPaid"/> a <paramref name="currentPaid"/> concretados.</summary>
    public static bool Crossed(int previousPaid, int currentPaid, SystemSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return previousPaid < settings.FairUseMonthlyPaid && currentPaid >= settings.FairUseMonthlyPaid;
    }
}
