using System.Globalization;
using System.Text.Json;
using Local133.Application.Admin;
using Local133.Application.Notifications;
using Local133.Domain.Audit;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Notifications;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Email;
using Local133.Infrastructure.Persistence;
using Local133.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Admin;

/// <summary>
/// Acciones del admin sobre el plan de un vendedor (6.2). Cada una va en una transacción con el candado de la
/// suscripción (el mismo que toman los abonos de Pey) y guarda su <see cref="AuditLog"/> en ella. Sin filtro
/// multi-tenant.
/// </summary>
internal sealed class AdminPlanActions(
    AppDbContext db,
    SubscriptionCrediting crediting,
    INotificationService notifications,
    TimeProvider clock,
    ILogger<AdminPlanActions> logger) : IAdminPlanActions
{
    private static readonly CultureInfo Mexico = CultureInfo.GetCultureInfo("es-MX");

    public async Task<AdminPlanResult> GrantDaysAsync(
        int merchantId, int days, string note, bool notify, AdminActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (days is < 1 or > AdminPlanLimits.MaxGrantDays)
        {
            return new AdminPlanResult.Rejected($"Regala entre 1 y {AdminPlanLimits.MaxGrantDays} días.");
        }

        if (NoteError(note) is { } noteError)
        {
            return noteError;
        }

        var merchant = await MerchantAsync(merchantId, cancellationToken);
        if (merchant is null)
        {
            return new AdminPlanResult.NotFound();
        }

        var now = clock.GetUtcNow();
        SubscriptionPeriod period;
        DateTimeOffset proUntil;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await crediting.LockAsync(merchantId, cancellationToken);
            var subscription = await SubscriptionAsync(merchantId, cancellationToken);
            if (subscription is null)
            {
                return new AdminPlanResult.Rejected("Su prueba todavía no empieza (la cuenta bancaria no está activa): aún no se le pueden regalar días.");
            }

            var graceDays = await GraceDaysAsync(cancellationToken);
            var before = subscription.ProUntil;
            period = subscription.AddDaysPeriod(SubscriptionPeriodKind.AdminGrant, days, now, graceDays);
            proUntil = subscription.ProUntil;
            await db.SaveChangesAsync(cancellationToken); // id del periodo para la bitácora y el aviso

            db.AuditLogs.Add(AuditLog.Create(
                actor.UserId, actor.Email, AuditAction.ProDaysGranted, merchantId,
                $"Regaló {DaysText(days)} de Pro ({Date(period.StartsAt)} al {LastDay(period.EndsAt)}){(notify ? ", con aviso al vendedor" : ", sin aviso")}.",
                note,
                Json(new
                {
                    days,
                    periodId = period.Id,
                    period.StartsAt,
                    period.EndsAt,
                    proUntilBefore = before,
                    proUntilAfter = proUntil,
                    notify,
                }),
                now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation("Admin {ActorUserId} regaló {Days} días de Pro al vendedor {MerchantId} (periodo {PeriodId})",
            actor.UserId, days, merchantId, period.Id);

        if (notify)
        {
            await NotifyGrantAsync(merchant, period, days, proUntil);
        }

        return new AdminPlanResult.Done($"Listo: se regalaron {DaysText(days)}. Tiene Pro hasta el {LastDay(proUntil)}.");
    }

    public async Task<AdminPlanResult> CreditChargeAsync(
        int merchantId, int chargeId, string note, AdminActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (NoteError(note) is { } noteError)
        {
            return noteError;
        }

        var exists = await db.SubscriptionCharges.IgnoreQueryFilters()
            .AnyAsync(c => c.Id == chargeId && c.MerchantId == merchantId, cancellationToken);
        if (!exists)
        {
            return new AdminPlanResult.NotFound();
        }

        var now = clock.GetUtcNow();
        SubscriptionCredit credit;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await crediting.LockAsync(merchantId, cancellationToken);
            var charge = await db.SubscriptionCharges.IgnoreQueryFilters()
                .Include(c => c.Payments)
                .SingleAsync(c => c.Id == chargeId, cancellationToken);
            if (charge.Status == SubscriptionChargeStatus.Paid)
            {
                return new AdminPlanResult.Rejected($"El cargo #{chargeId.ToString(CultureInfo.InvariantCulture)} ya está pagado.");
            }

            var statusBefore = charge.Status;
            var paidBase = charge.Payments.Sum(p => p.Base);
            credit = await crediting.CreditAsync(charge, now, manual: true, cancellationToken);

            db.AuditLogs.Add(AuditLog.Create(
                actor.UserId, actor.Email, AuditAction.ChargeCreditedManually, merchantId,
                $"Abonó a mano el cargo #{chargeId.ToString(CultureInfo.InvariantCulture)} ({charge.Package.Label()}, {Money(charge.Amount)}; estaba {statusBefore}): Pro hasta el {LastDay(credit.Result.ProUntil)}.",
                note,
                Json(new
                {
                    chargeId,
                    statusBefore = statusBefore.ToString(),
                    charge.Amount,
                    paidBase,
                    periodId = credit.Result.PeriodId,
                    proUntil = credit.Result.ProUntil,
                    founderNumber = credit.Result.FounderNumber,
                    referralId = credit.Referral?.ReferralId,
                    referralDecision = credit.Referral?.Decision.ToString(),
                }),
                now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation("Admin {ActorUserId} abonó a mano el cargo {ChargeId} del vendedor {MerchantId}",
            actor.UserId, chargeId, merchantId);
        await crediting.NotifyAsync(credit);

        return new AdminPlanResult.Done(
            $"Listo: se abonó el cargo #{chargeId.ToString(CultureInfo.InvariantCulture)}. Tiene Pro hasta el {LastDay(credit.Result.ProUntil)}.");
    }

    public async Task<AdminPlanResult> RevokeReferralRewardAsync(
        int merchantId, int referralId, string note, AdminActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (NoteError(note) is { } noteError)
        {
            return noteError;
        }

        var exists = await db.Referrals.IgnoreQueryFilters()
            .AnyAsync(r => r.Id == referralId && r.ReferrerMerchantId == merchantId, cancellationToken);
        if (!exists)
        {
            return new AdminPlanResult.NotFound();
        }

        var now = clock.GetUtcNow();
        string message;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await crediting.LockAsync(merchantId, cancellationToken);
            var referral = await db.Referrals.IgnoreQueryFilters().SingleAsync(r => r.Id == referralId, cancellationToken);
            if (!referral.CanBeRevoked)
            {
                return new AdminPlanResult.Rejected(referral.RevokedAt is not null
                    ? "Este premio ya estaba anulado."
                    : "Este referido no tiene un premio que anular (no ha pagado o no hubo premio).");
            }

            var referredName = await NameAsync(referral.ReferredMerchantId, cancellationToken);
            var wasPending = referral.IsPendingReward;
            referral.Revoke(now);

            object details;
            if (wasPending)
            {
                message = $"Se anuló el premio pendiente por {referredName}: ya no se dará.";
                details = new { referralId, wasPending };
            }
            else
            {
                var subscription = await SubscriptionAsync(merchantId, cancellationToken);
                var period = subscription?.Periods.SingleOrDefault(p => p.ReferralId == referral.Id);
                if (subscription is null || period is null)
                {
                    message = $"Se anuló el premio por {referredName} (no tenía días registrados).";
                    details = new { referralId, wasPending };
                }
                else
                {
                    var before = new { period.StartsAt, period.EndsAt, subscription.ProUntil };
                    var (removed, removePeriod) = subscription.RemoveUnusedDays(period, now);
                    if (removePeriod)
                    {
                        db.SubscriptionPeriods.Remove(period);
                    }

                    message = removed == TimeSpan.Zero
                        ? $"Se anuló el premio por {referredName}; sus días ya se habían usado, así que el plan no cambia."
                        : $"Se anuló el premio por {referredName}: se quitaron {Duration(removed)}. Tiene Pro hasta el {LastDay(subscription.ProUntil)}.";
                    details = new
                    {
                        referralId,
                        wasPending,
                        periodId = period.Id,
                        removedHours = Math.Round(removed.TotalHours, 2),
                        periodRemoved = removePeriod,
                        before,
                        after = new { period.StartsAt, period.EndsAt, subscription.ProUntil },
                    };
                }
            }

            db.AuditLogs.Add(AuditLog.Create(
                actor.UserId, actor.Email, AuditAction.ReferralRewardRevoked, merchantId, message, note, Json(details), now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation("Admin {ActorUserId} anuló el premio del referido {ReferralId} del vendedor {MerchantId}",
            actor.UserId, referralId, merchantId);
        return new AdminPlanResult.Done(message);
    }

    public async Task<AdminPlanResult> GrantPendingReferralRewardsAsync(
        int merchantId, AdminActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var merchant = await MerchantAsync(merchantId, cancellationToken);
        if (merchant is null)
        {
            return new AdminPlanResult.NotFound();
        }

        var now = clock.GetUtcNow();
        string message;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await crediting.LockAsync(merchantId, cancellationToken);
            var subscription = await SubscriptionAsync(merchantId, cancellationToken);
            if (subscription is null)
            {
                return new AdminPlanResult.Rejected("Su prueba todavía no empieza: los premios pendientes se le darán solos al activarse su cuenta.");
            }

            var pending = await db.Referrals.IgnoreQueryFilters()
                .CountAsync(r => r.ReferrerMerchantId == merchantId
                    && r.QualifiedAt != null && r.RewardedAt == null && r.SkippedReason == null && r.RevokedAt == null,
                    cancellationToken);
            if (pending == 0)
            {
                return new AdminPlanResult.Rejected("No tiene premios pendientes.");
            }

            var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);
            var before = subscription.ProUntil;
            var granted = await ReferralRewarder.GrantPendingAsync(db, subscription, merchant.Status, now, settings, cancellationToken);
            var skipped = pending - granted;
            message = granted == 0
                ? $"No se dio ningún premio: {Count(skipped, "quedó omitido", "quedaron omitidos")} (tope de premios o cuenta dada de baja)."
                : $"Se dieron {Count(granted, "premio", "premios")}{(skipped > 0 ? $" y {Count(skipped, "quedó omitido", "quedaron omitidos")}" : string.Empty)}. Tiene Pro hasta el {LastDay(subscription.ProUntil)}.";

            db.AuditLogs.Add(AuditLog.Create(
                actor.UserId, actor.Email, AuditAction.PendingReferralRewardsGranted, merchantId, message, null,
                Json(new { pending, granted, skipped, proUntilBefore = before, proUntilAfter = subscription.ProUntil }),
                now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation("Admin {ActorUserId} dio los premios pendientes del vendedor {MerchantId}", actor.UserId, merchantId);
        return new AdminPlanResult.Done(message);
    }

    private async Task NotifyGrantAsync(MerchantNames merchant, SubscriptionPeriod period, int days, DateTimeOffset proUntil)
    {
        try
        {
            var message = MerchantEmails.ProDaysGranted(
                Merchant.GreetingNameFor(merchant.OwnerName, merchant.BusinessName), days, MexicoTime.DateOf(proUntil.AddTicks(-1)));
            await notifications.NotifyAsync(new NotificationRequest(
                merchant.Id, NotificationType.ProDaysGranted, message.Title, message.Body, MerchantEmails.PlanPath,
                $"grant:{period.Id.ToString(CultureInfo.InvariantCulture)}", message.Email), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Los días ya quedaron guardados: un aviso fallido no deshace el regalo.
            logger.LogError(ex, "No se pudo avisar al vendedor {MerchantId} de los días regalados (periodo {PeriodId})",
                merchant.Id, period.Id);
        }
    }

    private static AdminPlanResult? NoteError(string? note) =>
        string.IsNullOrWhiteSpace(note)
            ? new AdminPlanResult.Rejected("Escribe el motivo.")
            : note.Trim().Length > AdminPlanLimits.NoteMaxLength
                ? new AdminPlanResult.Rejected($"El motivo admite hasta {AdminPlanLimits.NoteMaxLength} caracteres.")
                : null;

    private Task<MerchantNames?> MerchantAsync(int merchantId, CancellationToken cancellationToken) =>
        db.Merchants.AsNoTracking()
            .Where(m => m.Id == merchantId)
            .Select(m => new MerchantNames(m.Id, m.OwnerName, m.BusinessName, m.Status))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<string> NameAsync(int merchantId, CancellationToken cancellationToken)
    {
        var names = await MerchantAsync(merchantId, cancellationToken);
        return names?.OwnerName ?? names?.BusinessName ?? $"el vendedor #{merchantId.ToString(CultureInfo.InvariantCulture)}";
    }

    private Task<Subscription?> SubscriptionAsync(int merchantId, CancellationToken cancellationToken) =>
        db.Subscriptions.IgnoreQueryFilters()
            .Include(s => s.Periods)
            .SingleOrDefaultAsync(s => s.MerchantId == merchantId, cancellationToken);

    private Task<int> GraceDaysAsync(CancellationToken cancellationToken) =>
        db.SystemSettings.AsNoTracking()
            .Where(s => s.Id == SystemSettings.SingletonId)
            .Select(s => s.GraceDays)
            .SingleAsync(cancellationToken);

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string DaysText(int days) => days == 1 ? "1 día" : $"{days.ToString(Mexico)} días";

    private static string Count(int count, string one, string many) =>
        $"{count.ToString(Mexico)} {(count == 1 ? one : many)}";

    private static string Duration(TimeSpan span)
    {
        if (span.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)Math.Round(span.TotalHours)).ToString(Mexico)} h";
        }

        var days = Math.Round(span.TotalDays, 1);
        return days == 1 ? "1 día" : $"{days.ToString("0.#", Mexico)} días";
    }

    private static string Money(decimal amount) => amount.ToString("$#,0.00", CultureInfo.InvariantCulture);

    private static string Date(DateTimeOffset instant) => MexicoTime.DateOf(instant).ToString("d 'de' MMMM 'de' yyyy", Mexico);

    /// <summary>Último día (en México) de un fin excluido.</summary>
    private static string LastDay(DateTimeOffset end) => Date(end.AddTicks(-1));

    private sealed record MerchantNames(int Id, string? OwnerName, string? BusinessName, MerchantStatus Status);
}
