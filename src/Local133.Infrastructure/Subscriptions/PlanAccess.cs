using Local133.Application.Subscriptions;
using Local133.Application.Tenancy;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>Scoped: el plan de cada vendedor se calcula una vez por petición.</summary>
internal sealed class PlanAccess(AppDbContext db, ITenantContext tenant, TimeProvider clock) : IPlanAccess
{
    private readonly Dictionary<int, MerchantPlan> _cache = [];
    private SystemSettings? _settings;

    public Task<MerchantPlan> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!tenant.IsMerchantScoped || tenant.MerchantId is not { } merchantId)
        {
            throw new InvalidOperationException("La petición no es de un vendedor: no hay plan que consultar.");
        }

        return GetForMerchantAsync(merchantId, cancellationToken);
    }

    public async Task<MerchantPlan> GetForMerchantAsync(int merchantId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(merchantId, out var cached))
        {
            return cached;
        }

        _settings ??= await db.SystemSettings.AsNoTracking()
            .SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);
        // Por llave explícita y sin filtro: el resultado no depende del claim de la sesión.
        var periods = await db.SubscriptionPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.MerchantId == merchantId)
            .ToListAsync(cancellationToken);

        var status = PlanStatus.For(periods, clock.GetUtcNow(), _settings);
        var plan = new MerchantPlan(status, PlanEntitlements.For(status.Tier), _settings.TrialDays, _settings.FreeHistoryDays);
        _cache[merchantId] = plan;
        return plan;
    }
}
