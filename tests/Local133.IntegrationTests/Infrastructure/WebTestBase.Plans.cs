using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>Planes de prueba: suscripciones con los periodos que se quieren probar.</summary>
public abstract partial class WebTestBase
{
    /// <summary>
    /// Suscripción del vendedor con los periodos indicados, sin las reglas de encadenamiento (es el estado a probar).
    /// </summary>
    protected Task SeedPlanAsync(
        string email, params (SubscriptionPeriodKind Kind, DateTimeOffset Start, DateTimeOffset End)[] periods) =>
        SeedPlanAsync(email, periods.Select(p => (p.Kind, SubscriptionPlan.Pro, p.Start, p.End)).ToArray());

    /// <summary>
    /// Suscripción del vendedor con los periodos indicados y el plan de cada uno, sin las reglas de encadenamiento. Si ya
    /// tenía una (p. ej. la prueba que empezó un cobro pagado, 5.13), la reemplaza.
    /// </summary>
    protected async Task SeedPlanAsync(
        string email,
        params (SubscriptionPeriodKind Kind, SubscriptionPlan Plan, DateTimeOffset Start, DateTimeOffset End)[] periods)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var merchantId = await db.Merchants.Where(m => m.UserId == userId).Select(m => m.Id).SingleAsync();
        await db.SubscriptionPeriods.IgnoreQueryFilters().Where(p => p.MerchantId == merchantId).ExecuteDeleteAsync();
        await db.Subscriptions.IgnoreQueryFilters().Where(s => s.MerchantId == merchantId).ExecuteDeleteAsync();
        var last = periods.MaxBy(p => p.End);
        db.Subscriptions.Add(new Subscription
        {
            MerchantId = merchantId,
            TrialStartedAt = periods[0].Start,
            ProUntil = last.End,
            LastPeriodKind = last.Kind,
            LastPlan = periods.Where(p => p.Kind == SubscriptionPeriodKind.Paid)
                .OrderBy(p => p.End).Select(p => (SubscriptionPlan?)p.Plan).LastOrDefault(),
            Periods = periods.Select(p => new SubscriptionPeriod
            {
                MerchantId = merchantId, Kind = p.Kind, Plan = p.Plan, StartsAt = p.Start, EndsAt = p.End, CreatedAt = p.Start,
            }).ToList(),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Plan vencido: la prueba terminó hace 10 días (sin gracia, porque no fue un periodo pagado). No genera cobros.
    /// </summary>
    protected Task SeedExpiredPlanAsync(string email)
    {
        var now = DateTimeOffset.UtcNow;
        return SeedPlanAsync(email, (SubscriptionPeriodKind.Trial, now.AddDays(-40), now.AddDays(-10)));
    }

    /// <summary>Básico pagado vigente: la prueba terminó hace 10 días y el Básico va de hace 5 días a dentro de 25.</summary>
    protected Task SeedBasicPlanAsync(string email)
    {
        var now = DateTimeOffset.UtcNow;
        return SeedPlanAsync(email,
            (SubscriptionPeriodKind.Trial, SubscriptionPlan.Pro, now.AddDays(-25), now.AddDays(-10)),
            (SubscriptionPeriodKind.Paid, SubscriptionPlan.Basic, now.AddDays(-5), now.AddDays(25)));
    }

    /// <summary>Prueba de Pro vigente (empezó hace 7 días).</summary>
    protected Task SeedTrialAsync(string email)
    {
        var now = DateTimeOffset.UtcNow;
        return SeedPlanAsync(email, (SubscriptionPeriodKind.Trial, now.AddDays(-7), now.AddDays(23)));
    }
}
