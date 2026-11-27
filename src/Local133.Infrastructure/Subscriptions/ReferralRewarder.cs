using Local133.Application.Notifications;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Notifications;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Email;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>Lo que pasó con un referido que calificó (para avisarle a quien invitó después del commit).</summary>
/// <param name="ProUntil">Vigente hasta (de cualquier plan) de quien invitó después del premio (solo con <see cref="ReferralRewardDecision.Grant"/>).</param>
/// <param name="Plan">Plan de los días del premio: el del final de su cadena (§8; la prueba es Pro).</param>
/// <param name="Reactivated">Su plan estaba vencido y el premio lo reactivó: vuelve a cobrar desde ese momento.</param>
internal sealed record ReferralQualification(
    int ReferralId,
    int ReferrerMerchantId,
    int ReferredMerchantId,
    ReferralRewardDecision Decision,
    ReferralSkipReason? Reason,
    DateTimeOffset? ProUntil,
    SubscriptionPlan Plan = SubscriptionPlan.Pro,
    bool Reactivated = false);

/// <summary>
/// Premios de referidos (5.8, §6 del diseño). No guarda: agrega los cambios al contexto, que guarda quien llama dentro
/// de su transacción.
/// <list type="bullet">
/// <item><see cref="QualifyAsync"/>: al abonarse el primer pago del referido (lo llama <c>SubscriptionPaymentRecorder</c>
/// con el candado de la suscripción del referido ya tomado). Toma el candado de la suscripción de quien invitó: el orden
/// no tiene ciclos, porque quien invita siempre se registró antes que su referido.</item>
/// <item><see cref="GrantPendingAsync"/>: al empezar la prueba de quien invitó (<see cref="TrialStarter"/>), los premios
/// que esperaban su suscripción.</item>
/// </list>
/// </summary>
internal static class ReferralRewarder
{
    /// <returns>Lo que pasó, o <c>null</c> si el vendedor no fue referido o ya había calificado.</returns>
    public static async Task<ReferralQualification?> QualifyAsync(
        AppDbContext db, int referredMerchantId, DateTimeOffset now, SystemSettings settings, CancellationToken cancellationToken)
    {
        var referral = await db.Referrals.IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.ReferredMerchantId == referredMerchantId, cancellationToken);
        if (referral is null || !referral.Qualify(now))
        {
            return null;
        }

        var referrerId = referral.ReferrerMerchantId;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM [Subscriptions] WITH (UPDLOCK, HOLDLOCK) WHERE [MerchantId] = {referrerId}",
            cancellationToken);
        var referrerStatus = await db.Merchants.AsNoTracking()
            .Where(m => m.Id == referrerId)
            .Select(m => m.Status)
            .SingleAsync(cancellationToken);
        var subscription = await db.Subscriptions.IgnoreQueryFilters()
            .Include(s => s.Periods)
            .SingleOrDefaultAsync(s => s.MerchantId == referrerId, cancellationToken);
        var rewards = await RewardsInWindowAsync(db, referrerId, now, cancellationToken);
        // Antes del premio: el plan con el que se encadena y si estaba vencido (el premio lo reactiva).
        var plan = subscription?.ChainEndPlan ?? SubscriptionPlan.Pro;
        var wasExpired = subscription?.StatusAt(now, settings.GraceDays).Tier == PlanTier.Expired;

        var (decision, reason) = Apply(referral, referrerStatus, subscription, rewards, now, settings);
        var granted = decision == ReferralRewardDecision.Grant;
        return new ReferralQualification(referral.Id, referrerId, referredMerchantId, decision, reason,
            granted ? subscription!.ProUntil : null, plan, granted && wasExpired);
    }

    /// <summary>
    /// Premios que esperaban a que quien invitó tuviera suscripción: se encadenan después de su prueba, con el tope.
    /// </summary>
    /// <param name="subscription">La suscripción recién creada (con su prueba), aún sin guardar.</param>
    /// <returns>Premios otorgados.</returns>
    public static async Task<int> GrantPendingAsync(
        AppDbContext db, Subscription subscription, MerchantStatus referrerStatus, DateTimeOffset now, SystemSettings settings,
        CancellationToken cancellationToken)
    {
        var pending = await db.Referrals.IgnoreQueryFilters()
            .Where(r => r.ReferrerMerchantId == subscription.MerchantId
                && r.QualifiedAt != null && r.RewardedAt == null && r.SkippedReason == null && r.RevokedAt == null)
            .OrderBy(r => r.QualifiedAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return 0;
        }

        var rewards = await RewardsInWindowAsync(db, subscription.MerchantId, now, cancellationToken);
        var granted = 0;
        foreach (var referral in pending)
        {
            if (Apply(referral, referrerStatus, subscription, rewards, now, settings).Decision == ReferralRewardDecision.Grant)
            {
                rewards++;
                granted++;
            }
        }

        return granted;
    }

    private static (ReferralRewardDecision Decision, ReferralSkipReason? Reason) Apply(
        Referral referral, MerchantStatus referrerStatus, Subscription? subscription, int rewardsInWindow,
        DateTimeOffset now, SystemSettings settings)
    {
        var (decision, reason) = ReferralRewardPolicy.Decide(referrerStatus, subscription is not null, rewardsInWindow, settings);
        switch (decision)
        {
            case ReferralRewardDecision.Grant:
                // El premio es del plan del final de su cadena, donde se encadena (§8; la prueba es de Pro).
                subscription!.AddDaysPeriod(
                    SubscriptionPeriodKind.ReferralReward, subscription.ChainEndPlan, settings.ReferralRewardDays, now, settings.GraceDays, referral.Id);
                referral.MarkRewarded(now);
                break;
            case ReferralRewardDecision.Skip:
                referral.Skip(reason!.Value);
                break;
        }

        return (decision, reason);
    }

    private static Task<int> RewardsInWindowAsync(AppDbContext db, int referrerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = ReferralRewardPolicy.WindowStart(now);
        return db.Referrals.IgnoreQueryFilters()
            .CountAsync(r => r.ReferrerMerchantId == referrerId && r.RewardedAt >= since && r.RevokedAt == null, cancellationToken);
    }
}

/// <summary>
/// Aviso en el sitio y correo a quien invitó (5.8), después de guardar el abono. Nunca lanza: el pago ya quedó guardado.
/// </summary>
internal sealed class ReferralRewardNotifier(
    AppDbContext db,
    INotificationService notifications,
    ILogger<ReferralRewardNotifier> logger)
{
    public async Task NotifyAsync(ReferralQualification qualification, CancellationToken cancellationToken = default)
    {
        try
        {
            var names = await db.Merchants.AsNoTracking()
                .Where(m => m.Id == qualification.ReferrerMerchantId || m.Id == qualification.ReferredMerchantId)
                .Select(m => new { m.Id, m.OwnerName, m.BusinessName })
                .ToListAsync(cancellationToken);
            var referrer = names.Single(n => n.Id == qualification.ReferrerMerchantId);
            var referred = names.Single(n => n.Id == qualification.ReferredMerchantId);
            var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);

            var message = MerchantEmails.ReferralReward(
                qualification.Decision,
                qualification.Reason,
                Merchant.GreetingNameFor(referrer.OwnerName, referrer.BusinessName),
                Merchant.GreetingNameFor(referred.OwnerName, referred.BusinessName),
                settings,
                qualification.Plan,
                qualification.Reactivated,
                qualification.ProUntil is { } until ? MexicoTime.DateOf(until.AddTicks(-1)) : null);
            var type = qualification.Decision switch
            {
                ReferralRewardDecision.Grant => NotificationType.ReferralRewardGranted,
                ReferralRewardDecision.Pending => NotificationType.ReferralRewardPending,
                _ => NotificationType.ReferralRewardSkipped,
            };
            await notifications.NotifyAsync(new NotificationRequest(
                qualification.ReferrerMerchantId, type, message.Title, message.Body, MerchantEmails.InvitePath,
                $"ref:{qualification.ReferralId}", message.Email), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo avisar al vendedor {MerchantId} del referido {ReferralId}",
                qualification.ReferrerMerchantId, qualification.ReferralId);
        }
    }
}
