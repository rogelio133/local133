using System.Globalization;
using Local133.Application.Subscriptions;
using Local133.Domain.Pey;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>
/// Registra lo que Pey reporta de un link de la suscripción (§4 del diseño) y abona el periodo:
/// <list type="bullet">
/// <item>busca el cargo por <c>PeyPaymentLinkId</c>; de respaldo, por <c>clientReferenceId</c> (<c>sub-{id}</c>) y por la
/// metadata;</item>
/// <item>guarda cada transacción nueva (única por <c>transactionId</c>);</item>
/// <item>con <c>paid</c> y la suma de <c>base</c> que cubre el monto, abona el periodo encadenado; con <c>paid</c> por
/// menos o <c>partial</c>, el cargo queda <c>Partial</c> para revisión del admin;</item>
/// <item>todo link pagado se abona, aunque sea viejo o aunque pague dos: Pey no deja anularlos.</item>
/// </list>
/// Todo en una transacción con candado sobre la fila de <c>Subscriptions</c> del vendedor, para que dos cargos pagados
/// a la vez no empiecen en el mismo punto. Sin filtro multi-tenant (webhook y jobs).
/// </summary>
internal sealed class SubscriptionPaymentRecorder(
    AppDbContext db,
    ISubscriptionPaymentNotifier notifier,
    TimeProvider clock,
    ILogger<SubscriptionPaymentRecorder> logger) : ISubscriptionPaymentRecorder
{
    public async Task<SubscriptionPaymentResult> RecordAsync(PaymentLinkUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.PaymentLinkId);

        try
        {
            return await RecordOnceAsync(update, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Otra entrega del mismo pago ganó la carrera (p. ej. webhook y conciliación a la vez): se vuelve a leer.
            logger.LogInformation("Pago del link {PaymentLinkId} registrado en paralelo; se reintenta", update.PaymentLinkId);
            db.ChangeTracker.Clear();
            return await RecordOnceAsync(update, cancellationToken);
        }
    }

    private async Task<SubscriptionPaymentResult> RecordOnceAsync(PaymentLinkUpdate update, CancellationToken cancellationToken)
    {
        var match = await FindChargeAsync(update, cancellationToken);
        if (match.ChargeId is not { } chargeId)
        {
            return new SubscriptionPaymentResult.Unmatched(match.Reason!);
        }

        var now = clock.GetUtcNow();
        SubscriptionPaymentCredited? credited = null;
        SubscriptionPaymentResult result;

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Candado de la suscripción del vendedor: serializa los abonos (y su encadenamiento) del mismo vendedor.
            // Se toma antes de leer el cargo, para leerlo ya serializado.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM [Subscriptions] WITH (UPDLOCK, HOLDLOCK) WHERE [MerchantId] = {match.MerchantId}",
                cancellationToken);
            var charge = await db.SubscriptionCharges.IgnoreQueryFilters()
                .Include(c => c.Payments)
                .SingleAsync(c => c.Id == chargeId, cancellationToken);

            if (charge.PeyPaymentLinkId is null)
            {
                // Se encontró por la referencia de respaldo (no se alcanzó a guardar el id del link).
                charge.AdoptPaymentLinkId(update.PaymentLinkId.Trim());
            }

            var newPayments = AddNewPayments(charge, update, now);

            if (charge.Status == SubscriptionChargeStatus.Paid)
            {
                result = AlreadyCredited(charge, newPayments);
            }
            else if (update.Status == PeyPaymentLinkStatus.Paid && charge.IsCoveredBy(charge.Payments.Sum(p => p.Base)))
            {
                (result, credited) = await CreditAsync(charge, now, cancellationToken);
            }
            else if (update.Status is PeyPaymentLinkStatus.Paid or PeyPaymentLinkStatus.Partial)
            {
                result = Partial(charge, update.Status);
            }
            else if (update.Status == PeyPaymentLinkStatus.Processing)
            {
                charge.MarkProcessing();
                result = new SubscriptionPaymentResult.StatusUpdated(charge.Id, charge.Status);
            }
            else
            {
                result = new SubscriptionPaymentResult.StatusUpdated(charge.Id, charge.Status);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (credited is not null)
        {
            await notifier.PaymentCreditedAsync(credited, CancellationToken.None);
        }

        return result;
    }

    private async Task<(SubscriptionPaymentResult, SubscriptionPaymentCredited?)> CreditAsync(
        SubscriptionCharge charge, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var subscription = await db.Subscriptions.IgnoreQueryFilters()
            .Include(s => s.Periods)
            .SingleOrDefaultAsync(s => s.MerchantId == charge.MerchantId, cancellationToken)
            ?? throw new InvalidOperationException($"El vendedor {charge.MerchantId} tiene el cargo {charge.Id} pero no tiene suscripción.");
        var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);

        // El periodo se encadena desde el momento del pago (no desde que nos enteramos), sin pasar de ahora.
        var paidAt = charge.Payments.Max(p => (DateTimeOffset?)p.PaidAt) is { } last && last < now ? last : now;
        var extendsPaidPro = subscription.Periods.Any(p => p.Kind == SubscriptionPeriodKind.Paid && p.EndsAt > paidAt);

        charge.MarkPaid(paidAt);
        var period = subscription.AddPaidPeriod(charge.Package, paidAt, settings.GraceDays, charge.Id);
        if (charge.Package == SubscriptionPackage.Annual)
        {
            subscription.LockAnnualPrice(charge.ListPrice);
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

        await db.SaveChangesAsync(cancellationToken); // para tener el id del periodo

        logger.LogInformation("Cargo {ChargeId} del vendedor {MerchantId} abonado: {Package} de {StartsAt:o} a {EndsAt:o}{Founder}",
            charge.Id, charge.MerchantId, charge.Package, period.StartsAt, period.EndsAt,
            founderNumber is { } n ? $", fundador {n.ToString(CultureInfo.InvariantCulture)}" : string.Empty);

        return (new SubscriptionPaymentResult.Credited(charge.Id, period.Id, subscription.ProUntil, founderNumber),
            new SubscriptionPaymentCredited(charge.MerchantId, charge.Id, charge.Package, charge.Amount, subscription.ProUntil,
                extendsPaidPro, founderNumber));
    }

    /// <summary>Siguiente lugar de fundador, o <c>null</c> si ya no quedan. Candado de rango sobre los asignados.</summary>
    private async Task<int?> NextFounderNumberAsync(SystemSettings settings, CancellationToken cancellationToken)
    {
        var assigned = (await db.Database.SqlQueryRaw<FounderSlots>(
                "SELECT COUNT(*) AS [Count], MAX([FounderNumber]) AS [MaxNumber] FROM [Subscriptions] WITH (UPDLOCK, HOLDLOCK) WHERE [FounderNumber] IS NOT NULL")
            .ToListAsync(cancellationToken)).Single();
        return assigned.Count < settings.FounderSlots ? (assigned.MaxNumber ?? 0) + 1 : null;
    }

    private SubscriptionPaymentResult AlreadyCredited(SubscriptionCharge charge, int newPayments)
    {
        if (newPayments > 0)
        {
            logger.LogWarning("El cargo {ChargeId} del vendedor {MerchantId} ya estaba pagado y Pey reportó {Count} pago(s) más: se guardan sin abonar, para revisión",
                charge.Id, charge.MerchantId, newPayments);
        }

        return new SubscriptionPaymentResult.AlreadyCredited(charge.Id);
    }

    private SubscriptionPaymentResult Partial(SubscriptionCharge charge, PeyPaymentLinkStatus status)
    {
        if (charge.MarkPartial())
        {
            logger.LogWarning("El cargo {ChargeId} del vendedor {MerchantId} quedó incompleto ({Status}, pagado {Paid} de {Amount}): no se abona, para revisión del admin",
                charge.Id, charge.MerchantId, status, charge.Payments.Sum(p => p.Base), charge.Amount);
        }

        return new SubscriptionPaymentResult.StatusUpdated(charge.Id, charge.Status);
    }

    private int AddNewPayments(SubscriptionCharge charge, PaymentLinkUpdate update, DateTimeOffset now)
    {
        var count = 0;
        foreach (var t in update.Transactions.Where(t => !string.IsNullOrWhiteSpace(t.TransactionId)).DistinctBy(t => t.TransactionId.Trim()))
        {
            var id = t.TransactionId.Trim();
            if (charge.Payments.Any(p => p.PeyTransactionId == id))
            {
                continue;
            }

            charge.Payments.Add(SubscriptionPayment.Record(
                charge, id, t.PaymentMethod, t.Amount, t.Base, t.Fee, t.ApplicationFee, t.PaidAt ?? now, now));
            count++;
        }

        return count;
    }

    private async Task<ChargeMatch> FindChargeAsync(PaymentLinkUpdate update, CancellationToken cancellationToken)
    {
        var linkId = update.PaymentLinkId.Trim();
        var candidate = await db.SubscriptionCharges.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.PeyPaymentLinkId == linkId)
            .Select(c => new { c.Id, c.MerchantId, c.PeyPaymentLinkId })
            .SingleOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            int? fallbackId = SubscriptionCharge.TryParseClientReference(update.ClientReferenceId, out var fromReference)
                ? fromReference
                : update.Metadata.TryGetValue(SubscriptionChargeMetadata.SubscriptionChargeIdKey, out var raw)
                  && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var fromMetadata)
                    ? fromMetadata
                    : null;
            if (fallbackId is null)
            {
                return ChargeMatch.None($"No hay cargo con el link {linkId} ni referencia de respaldo.");
            }

            candidate = await db.SubscriptionCharges.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.Id == fallbackId)
                .Select(c => new { c.Id, c.MerchantId, c.PeyPaymentLinkId })
                .SingleOrDefaultAsync(cancellationToken);
            if (candidate is null)
            {
                return ChargeMatch.None($"No existe el cargo {fallbackId} del link {linkId}.");
            }

            if (candidate.PeyPaymentLinkId is not null)
            {
                return ChargeMatch.None($"El cargo {candidate.Id} tiene otro link ({candidate.PeyPaymentLinkId}), no {linkId}.");
            }
        }

        if (update.Metadata.TryGetValue(SubscriptionChargeMetadata.KindKey, out var kind)
            && kind is not null && kind != SubscriptionChargeMetadata.SubscriptionKind)
        {
            return ChargeMatch.None($"El link {linkId} no es de la suscripción (kind {kind}).");
        }

        if (update.Metadata.TryGetValue(SubscriptionChargeMetadata.MerchantIdKey, out var merchant)
            && merchant is not null && merchant != candidate.MerchantId.ToString(CultureInfo.InvariantCulture))
        {
            return ChargeMatch.None($"La metadata del link {linkId} es de otro vendedor que el cargo {candidate.Id}.");
        }

        return new ChargeMatch(candidate.Id, candidate.MerchantId, null);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2601 or 2627 };

    private sealed record ChargeMatch(int? ChargeId, int MerchantId, string? Reason)
    {
        public static ChargeMatch None(string reason) => new(null, 0, reason);
    }

    private sealed record FounderSlots(int Count, int? MaxNumber);
}
