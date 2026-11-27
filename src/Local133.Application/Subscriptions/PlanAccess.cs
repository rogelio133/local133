using Local133.Domain.Common;
using Local133.Domain.Subscriptions;

namespace Local133.Application.Subscriptions;

/// <summary>Plan de un vendedor en este momento: estado y lo que permite.</summary>
/// <param name="FreeHistoryDays">Días de historial que ve el gratuito (<c>SystemSettings.FreeHistoryDays</c>).</param>
public sealed record MerchantPlan(PlanStatus Status, PlanEntitlements Entitlements, int TrialDays, int FreeHistoryDays = 30)
{
    public PlanTier Tier => Status.Tier;

    /// <summary>Plan cuyas funciones aplican ahora (<see cref="PlanStatus.Plan"/>); <c>null</c> en el onboarding y el vencido.</summary>
    public SubscriptionPlan? Plan => Status.Plan;

    /// <summary>
    /// Desde cuándo ve el historial: <c>null</c> con historial completo; en el gratuito, la medianoche (México) de
    /// hace <see cref="FreeHistoryDays"/> días contando hoy. Lo anterior se oculta, no se borra.
    /// </summary>
    public DateTimeOffset? HistoryStartUtc(DateTimeOffset now) =>
        Entitlements.HasFullHistory
            ? null
            : MexicoTime.DayRange(MexicoTime.Today(now).AddDays(-(Math.Max(FreeHistoryDays, 1) - 1))).StartUtc;
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
