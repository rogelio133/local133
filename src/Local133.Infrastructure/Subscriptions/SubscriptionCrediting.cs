using System.Globalization;
using Local133.Application.Subscriptions;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>Lo que dejó un abono, para avisar después del commit.</summary>
internal sealed record SubscriptionCredit(
    SubscriptionPaymentResult.Credited Result,
    SubscriptionPaymentCredited Payment,
    ReferralQualification? Referral);

/// <summary>
/// Abono de un cargo de la suscripción, compartido por el webhook y la conciliación
/// (<see cref="SubscriptionPaymentRecorder"/>) y por el abono manual del admin (6.2): marca el cargo pagado, encadena el
/// periodo desde el momento del pago, congela el anual, asigna el lugar de fundador si quedan y califica al referido.
/// No guarda ni hace commit: quien llama abre la transacción y toma el candado (<see cref="LockAsync"/>) antes de leer
/// el cargo. Después del commit, <see cref="NotifyAsync"/> manda el correo y el aviso del referido.
/// </summary>
internal sealed class SubscriptionCrediting(
    AppDbContext db,
    ISubscriptionPaymentNotifier notifier,
    ReferralRewardNotifier referralNotifier,
    ILogger<SubscriptionCrediting> logger)
{
    /// <summary>Candado de la suscripción del vendedor: serializa sus abonos (y su encadenamiento).</summary>
    public Task LockAsync(int merchantId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM [Subscriptions] WITH (UPDLOCK, HOLDLOCK) WHERE [MerchantId] = {merchantId}",
            cancellationToken);

    /// <param name="charge">Cargo sin pagar, con sus <c>Payments</c> cargados.</param>
    /// <param name="manual">Lo abona el admin a mano: se sella <c>CreditedManuallyAt</c>.</param>
    public async Task<SubscriptionCredit> CreditAsync(
        SubscriptionCharge charge, DateTimeOffset now, bool manual, CancellationToken cancellationToken)
    {
        var subscription = await db.Subscriptions.IgnoreQueryFilters()
            .Include(s => s.Periods)
            .SingleOrDefaultAsync(s => s.MerchantId == charge.MerchantId, cancellationToken)
            ?? throw new InvalidOperationException($"El vendedor {charge.MerchantId} tiene el cargo {charge.Id} pero no tiene suscripción.");
        var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);

        // El periodo se encadena desde el momento del pago (no desde que nos enteramos), sin pasar de ahora.
        var paidAt = charge.Payments.Max(p => (DateTimeOffset?)p.PaidAt) is { } last && last < now ? last : now;
        var extendsPaidPro = subscription.Periods.Any(p =>
            p.Kind == SubscriptionPeriodKind.Paid && p.Plan == SubscriptionPlan.Pro && p.EndsAt > paidAt);

        charge.MarkPaid(paidAt, manual ? now : null);
        var period = subscription.AddPaidPeriod(charge.Plan, charge.Package, paidAt, settings.GraceDays, charge.Id);
        if (charge.Package == SubscriptionPackage.Annual)
        {
            subscription.LockAnnualPrice(charge.Plan, charge.ListPrice);
        }

        int? founderNumber = null;
        if (charge.IsFounderPrice && !subscription.IsFounder)
        {
            founderNumber = await NextFounderNumberAsync(settings, cancellationToken);
            if (founderNumber is { } number)
            {
                subscription.AssignFounder(number);
            }
            else
            {
                logger.LogWarning("El cargo {ChargeId} del vendedor {MerchantId} se cotizó como fundador pero ya no quedan lugares; se abona sin número",
                    charge.Id, charge.MerchantId);
            }
        }

        // Primer pago de un referido (5.8): califica y quien lo invitó gana sus días (o quedan pendientes, o se omiten).
        var referral = await ReferralRewarder.QualifyAsync(db, charge.MerchantId, now, settings, cancellationToken);

        await db.SaveChangesAsync(cancellationToken); // para tener el id del periodo

        logger.LogInformation("Cargo {ChargeId} del vendedor {MerchantId} abonado{Manual}: {Package} de {StartsAt:o} a {EndsAt:o}{Founder}",
            charge.Id, charge.MerchantId, manual ? " a mano" : string.Empty, charge.Package, period.StartsAt, period.EndsAt,
            founderNumber is { } n ? $", fundador {n.ToString(CultureInfo.InvariantCulture)}" : string.Empty);
        if (referral is not null)
        {
            logger.LogInformation("Referido {ReferralId} calificó: premio {Decision} ({Reason}) para el vendedor {ReferrerId}",
                referral.ReferralId, referral.Decision, referral.Reason, referral.ReferrerMerchantId);
        }

        return new SubscriptionCredit(
            new SubscriptionPaymentResult.Credited(charge.Id, period.Id, subscription.ProUntil, founderNumber),
            new SubscriptionPaymentCredited(charge.MerchantId, charge.Id, charge.Package, charge.Amount, subscription.ProUntil,
                extendsPaidPro, founderNumber),
            referral);
    }

    /// <summary>Correo «Recibimos tu pago» y aviso a quien invitó. Nunca lanza (los notificadores no lanzan).</summary>
    public async Task NotifyAsync(SubscriptionCredit credit)
    {
        await notifier.PaymentCreditedAsync(credit.Payment, CancellationToken.None);
        if (credit.Referral is not null)
        {
            await referralNotifier.NotifyAsync(credit.Referral, CancellationToken.None);
        }
    }

    /// <summary>Siguiente lugar de fundador, o <c>null</c> si ya no quedan. Candado de rango sobre los asignados.</summary>
    private async Task<int?> NextFounderNumberAsync(SystemSettings settings, CancellationToken cancellationToken)
    {
        var assigned = (await db.Database.SqlQueryRaw<FounderSlots>(
                "SELECT COUNT(*) AS [Count], MAX([FounderNumber]) AS [MaxNumber] FROM [Subscriptions] WITH (UPDLOCK, HOLDLOCK) WHERE [FounderNumber] IS NOT NULL")
            .ToListAsync(cancellationToken)).Single();
        return assigned.Count < settings.FounderSlots ? (assigned.MaxNumber ?? 0) + 1 : null;
    }

    private sealed record FounderSlots(int Count, int? MaxNumber);
}
