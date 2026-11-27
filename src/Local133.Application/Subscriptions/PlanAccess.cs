using Local133.Domain.Subscriptions;

namespace Local133.Application.Subscriptions;

/// <summary>Plan de un vendedor en este momento: estado y lo que permite.</summary>
/// <param name="LastPlan">Plan de su último pago (5.15: «Renovar igual que la última vez»); <c>null</c> si nunca pagó.</param>
/// <param name="LastPackage">Paquete de su último pago.</param>
public sealed record MerchantPlan(
    PlanStatus Status,
    PlanEntitlements Entitlements,
    int TrialDays,
    SubscriptionPlan? LastPlan = null,
    SubscriptionPackage? LastPackage = null)
{
    public PlanTier Tier => Status.Tier;

    /// <summary>Plan cuyas funciones aplican ahora (<see cref="PlanStatus.Plan"/>); <c>null</c> en el onboarding y el vencido.</summary>
    public SubscriptionPlan? Plan => Status.Plan;
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
