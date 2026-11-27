using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>Qué pasa con el premio de quien invitó cuando su referido califica.</summary>
public enum ReferralRewardDecision
{
    /// <summary>Se le dan <c>ReferralRewardDays</c> días de Pro.</summary>
    Grant,

    /// <summary>Todavía no tiene suscripción (onboarding): se le dan al empezar su prueba.</summary>
    Pending,

    /// <summary>No se le dan (ver <see cref="ReferralSkipReason"/>).</summary>
    Skip,
}

/// <summary>
/// Reglas del premio de referidos (§6 del diseño): 10 días por referido que paga, hasta 12 premios en 12 meses
/// móviles; los vencidos también ganan; un vendedor dado de baja no gana.
/// </summary>
public static class ReferralRewardPolicy
{
    /// <summary>Inicio (incluido) de la ventana del tope: 12 meses antes, en hora de México.</summary>
    public static DateTimeOffset WindowStart(DateTimeOffset now) => MexicoTime.AddMonths(now, -12);

    /// <param name="rewardsInWindow">Premios ya dados desde <see cref="WindowStart"/>.</param>
    public static (ReferralRewardDecision Decision, ReferralSkipReason? Reason) Decide(
        MerchantStatus referrerStatus, bool referrerHasSubscription, int rewardsInWindow, SystemSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (referrerStatus == MerchantStatus.Disabled)
        {
            return (ReferralRewardDecision.Skip, ReferralSkipReason.ReferrerDisabled);
        }

        if (!referrerHasSubscription)
        {
            return (ReferralRewardDecision.Pending, null);
        }

        if (rewardsInWindow >= settings.ReferralRewardsPerYear || settings.ReferralRewardDays <= 0)
        {
            return (ReferralRewardDecision.Skip, ReferralSkipReason.RewardCap);
        }

        return (ReferralRewardDecision.Grant, null);
    }
}
