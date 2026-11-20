using Local133.Domain.Subscriptions;

namespace Local133.Application.Subscriptions;

/// <summary>Plan de un vendedor en este momento: estado y lo que permite.</summary>
public sealed record MerchantPlan(PlanStatus Status, PlanEntitlements Entitlements, int TrialDays)
{
    public PlanTier Tier => Status.Tier;
}

/// <summary>
/// Estado del plan del vendedor, calculado con sus periodos (<see cref="PlanStatus.For"/>) y los ajustes. Guarda el
/// resultado durante la petición (o el scope del job): consultarlo varias veces no repite la consulta.
/// </summary>
public interface IPlanAccess
{
    /// <summary>Del vendedor de la sesión. Sin vendedor (admin, job) lanza <see cref="InvalidOperationException"/>.</summary>
    Task<MerchantPlan> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>De un vendedor por id, sin depender de la sesión (jobs, webhook).</summary>
    Task<MerchantPlan> GetForMerchantAsync(int merchantId, CancellationToken cancellationToken = default);
}
