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
    protected async Task SeedPlanAsync(
        string email, params (SubscriptionPeriodKind Kind, DateTimeOffset Start, DateTimeOffset End)[] periods)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var merchantId = await db.Merchants.Where(m => m.UserId == userId).Select(m => m.Id).SingleAsync();
        var last = periods.MaxBy(p => p.End);
        db.Subscriptions.Add(new Subscription
        {
            MerchantId = merchantId,
            TrialStartedAt = periods[0].Start,
            ProUntil = last.End,
            LastPeriodKind = last.Kind,
            Periods = periods.Select(p => new SubscriptionPeriod
            {
                MerchantId = merchantId, Kind = p.Kind, StartsAt = p.Start, EndsAt = p.End, CreatedAt = p.Start,
            }).ToList(),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Plan gratuito: la prueba terminó hace 10 días (sin gracia, porque no fue un periodo pagado).</summary>
    protected Task SeedFreePlanAsync(string email)
    {
        var now = DateTimeOffset.UtcNow;
        return SeedPlanAsync(email, (SubscriptionPeriodKind.Trial, now.AddDays(-40), now.AddDays(-10)));
    }

    /// <summary>Prueba de Pro vigente (empezó hace 7 días).</summary>
    protected Task SeedTrialAsync(string email)
    {
        var now = DateTimeOffset.UtcNow;
        return SeedPlanAsync(email, (SubscriptionPeriodKind.Trial, now.AddDays(-7), now.AddDays(23)));
    }
}
