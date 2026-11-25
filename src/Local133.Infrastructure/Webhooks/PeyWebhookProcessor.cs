using System.Text.Json;
using Local133.Application.BankAccounts;
using Local133.Application.Payments;
using Local133.Application.Subscriptions;
using Local133.Application.Webhooks;
using Local133.Domain.Pey;
using Local133.Domain.Webhooks;
using Local133.Infrastructure.Persistence;
using Local133.Infrastructure.Pey;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Webhooks;

/// <summary>
/// Webhook de Pey con la firma ya verificada: se guarda en <c>WebhookEvents</c> (único por <c>webhook-id</c>) y se
/// procesa en la misma petición. Un id ya procesado (o ignorado) no se repite; uno fallido o a medias se reprocesa
/// cuando Pey lo reintenta, o cuando el admin lo pide desde la bitácora (6.4, <see cref="ReprocessAsync"/>). Nunca se
/// loguea el body (trae la CLABE del pagador).
/// </summary>
internal sealed class PeyWebhookProcessor(
    AppDbContext db,
    IPaymentRecorder payments,
    IBankAccountStatusSync bankAccounts,
    ISubscriptionPaymentRecorder subscriptionPayments,
    TimeProvider clock,
    ILogger<PeyWebhookProcessor> logger) : IPeyWebhookProcessor
{
    public async Task<WebhookHandlingResult> HandleAsync(
        string webhookId, DateTimeOffset timestamp, string body, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookId);
        ArgumentNullException.ThrowIfNull(body);

        var type = TryReadType(body);
        var webhook = await ReceiveAsync(webhookId, type, timestamp, body, cancellationToken);
        if (webhook.IsSettled)
        {
            logger.LogInformation("Webhook {WebhookId} ({EventType}) repetido; ya estaba {Status}", webhookId, webhook.EventType, webhook.Status);
            return WebhookHandlingResult.Duplicate;
        }

        // El intento cuenta aunque el procesamiento truene.
        webhook.StartAttempt();
        await db.SaveChangesAsync(cancellationToken);

        var (result, _) = await ProcessAsync(webhook, type, body, cancellationToken);
        return result;
    }

    public async Task<WebhookReprocessResult> ReprocessAsync(int webhookEventId, CancellationToken cancellationToken = default)
    {
        var webhook = await db.WebhookEvents.SingleOrDefaultAsync(e => e.Id == webhookEventId, cancellationToken);
        if (webhook is null)
        {
            return new WebhookReprocessResult.NotFound();
        }

        if (!webhook.CanReprocess)
        {
            return new WebhookReprocessResult.NotAllowed();
        }

        var before = webhook.Status;
        var webhookId = webhook.WebhookId;
        var eventType = webhook.EventType;
        webhook.StartReprocess();
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Reproceso del webhook {WebhookId} ({EventType}) pedido por el admin; estaba {Status}", webhookId, eventType, before);

        var (result, reason) = await ProcessAsync(webhook, TryReadType(webhook.Payload), webhook.Payload, cancellationToken);
        var after = await db.WebhookEvents.AsNoTracking()
            .Where(e => e.Id == webhookEventId).Select(e => new { e.Status, e.Attempts }).SingleAsync(CancellationToken.None);
        return new WebhookReprocessResult.Done(webhookId, eventType, before, after.Status, result, reason, after.Attempts);
    }

    /// <summary>
    /// Despacha un evento con su intento ya contado y guarda cómo quedó. Una falla transitoria lo deja en
    /// <see cref="WebhookEventStatus.Received"/> (Pey lo reintenta con el mismo id).
    /// </summary>
    private async Task<(WebhookHandlingResult Result, string? Reason)> ProcessAsync(
        WebhookEvent webhook, string? type, string body, CancellationToken cancellationToken)
    {
        var webhookId = webhook.WebhookId;
        Outcome outcome;
        try
        {
            outcome = await DispatchAsync(type, body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falló el procesamiento del webhook {WebhookId} ({EventType}), intento {Attempt}; se espera el reintento de Pey",
                webhookId, type, webhook.Attempts);
            // Lo que haya quedado a medias en el contexto se descarta; el evento se vuelve a leer.
            db.ChangeTracker.Clear();
            var retry = await db.WebhookEvents.SingleAsync(e => e.WebhookId == webhookId, CancellationToken.None);
            var error = $"{ex.GetType().Name}: {ex.Message}";
            retry.MarkRetryable(error);
            await db.SaveChangesAsync(CancellationToken.None);
            return (WebhookHandlingResult.RetryLater, error);
        }

        if (db.Entry(webhook).State == EntityState.Detached)
        {
            // Un manejador limpió el contexto (p. ej. un pago duplicado en carrera).
            webhook = await db.WebhookEvents.SingleAsync(e => e.WebhookId == webhookId, CancellationToken.None);
        }

        var now = clock.GetUtcNow();
        switch (outcome.Result)
        {
            case WebhookHandlingResult.Processed:
                webhook.MarkProcessed(now);
                break;
            case WebhookHandlingResult.Ignored:
                webhook.MarkIgnored(outcome.Reason!, now);
                break;
            default:
                webhook.MarkFailed(outcome.Reason!, now);
                break;
        }

        await db.SaveChangesAsync(CancellationToken.None);

        if (outcome.Result == WebhookHandlingResult.Failed)
        {
            logger.LogError("Webhook {WebhookId} ({EventType}) sin procesar: {Reason}", webhookId, type, outcome.Reason);
        }
        else
        {
            logger.LogInformation("Webhook {WebhookId} ({EventType}): {Result} {Reason}", webhookId, type, outcome.Result, outcome.Reason);
        }

        return (outcome.Result, outcome.Reason);
    }

    /// <summary>Guarda el evento; si el id ya existe (reintento o entrega simultánea) devuelve el existente.</summary>
    private async Task<WebhookEvent> ReceiveAsync(
        string webhookId, string? type, DateTimeOffset timestamp, string body, CancellationToken cancellationToken)
    {
        var existing = await db.WebhookEvents.SingleOrDefaultAsync(e => e.WebhookId == webhookId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var webhook = WebhookEvent.Receive(webhookId, type, body, timestamp, clock.GetUtcNow());
        db.WebhookEvents.Add(webhook);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return webhook;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            return await db.WebhookEvents.SingleAsync(e => e.WebhookId == webhookId, cancellationToken);
        }
    }

    private async Task<Outcome> DispatchAsync(string? type, string body, CancellationToken cancellationToken)
    {
        try
        {
            return type switch
            {
                PeyWebhookEventTypes.CodiQrPaymentReceived => await CodiQrPaymentAsync(body, cancellationToken),
                PeyWebhookEventTypes.BankAccountUpdated => await BankAccountUpdatedAsync(body, cancellationToken),
                PeyWebhookEventTypes.CodiPnPaymentReceived => await CodiPnPaymentAsync(body, cancellationToken),
                PeyWebhookEventTypes.PaymentLinkStatusUpdated => await PaymentLinkStatusUpdatedAsync(body, cancellationToken),
                null => Outcome.Fail("El payload no es JSON o no trae type."),
                _ => Outcome.Fail($"Tipo de evento desconocido: {type}."),
            };
        }
        catch (JsonException ex)
        {
            return Outcome.Fail($"Payload inválido para {type}: {ex.Message}");
        }
    }

    private Task<Outcome> CodiQrPaymentAsync(string body, CancellationToken cancellationToken)
    {
        var payload = PeyWebhookJson.Deserialize<PeyCodiQrPaymentReceived>(body);
        return CodiPaymentAsync(
            CodiPaymentChannel.Qr, payload?.TransactionId, payload?.CodiQrId, payload?.Amount, payload?.DatePaid,
            payload?.Metadata, cancellationToken);
    }

    private Task<Outcome> CodiPnPaymentAsync(string body, CancellationToken cancellationToken)
    {
        var payload = PeyWebhookJson.Deserialize<PeyCodiPnPaymentReceived>(body);
        return CodiPaymentAsync(
            CodiPaymentChannel.PushNotification, payload?.TransactionId, codiQrId: null, payload?.Amount, payload?.DatePaid,
            payload?.Metadata, cancellationToken);
    }

    /// <summary>Pago CoDi (QR o push) ⇒ <see cref="IPaymentRecorder"/>.</summary>
    private async Task<Outcome> CodiPaymentAsync(
        CodiPaymentChannel channel, string? transactionId, string? codiQrId, decimal? amount, long? datePaid,
        PeyTransactionMetadata? metadata, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return Outcome.Fail("Falta transactionId.");
        }

        if (amount is not { } paidAmount)
        {
            return Outcome.Fail("Falta amount.");
        }

        var paidAt = datePaid is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : clock.GetUtcNow();
        var external = metadata?.ExternalMetadata?.ToDictionary(p => p.Key, p => PeyJson.ScalarToString(p.Value))
            ?? new Dictionary<string, string?>();
        var result = await payments.RecordCodiPaymentAsync(new ReceivedCodiPayment(
            transactionId.Trim(), codiQrId, paidAmount, paidAt, metadata?.TrackingKey, metadata?.BankCode, external, channel),
            cancellationToken);

        return result switch
        {
            PaymentRecordResult.Recorded recorded => Outcome.Process($"Pago {recorded.PaymentId} en la venta {recorded.SaleId}."),
            PaymentRecordResult.AlreadyRecorded already => Outcome.Process($"Pago ya registrado en la venta {already.SaleId}."),
            PaymentRecordResult.Ignored ignored => Outcome.Ignore(ignored.Reason),
            PaymentRecordResult.Unmatched unmatched => Outcome.Fail(unmatched.Reason),
            _ => throw new InvalidOperationException($"Resultado inesperado: {result}"),
        };
    }

    /// <summary>Link de pago de la suscripción ⇒ <see cref="ISubscriptionPaymentRecorder"/> (abona si quedó pagado).</summary>
    private async Task<Outcome> PaymentLinkStatusUpdatedAsync(string body, CancellationToken cancellationToken)
    {
        var payload = PeyWebhookJson.Deserialize<PeyPaymentLinkStatusUpdated>(body);
        if (payload is null || string.IsNullOrWhiteSpace(payload.PaymentLinkId))
        {
            return Outcome.Fail("Falta paymentLinkId.");
        }

        if (PeyJson.ParsePaymentLinkStatus(payload.Status) is not { } status)
        {
            return Outcome.Fail($"Estatus de link desconocido: {payload.Status}.");
        }

        var transactions = payload.Transactions ?? [];
        var metadata = transactions
            .Select(t => t.Metadata?.ExternalMetadata)
            .FirstOrDefault(m => m is { Count: > 0 })
            ?.ToDictionary(p => p.Key, p => PeyJson.ScalarToString(p.Value))
            ?? new Dictionary<string, string?>();
        var update = new PaymentLinkUpdate(
            payload.PaymentLinkId.Trim(),
            status,
            payload.ClientReferenceId,
            metadata,
            transactions
                .Where(t => !string.IsNullOrWhiteSpace(t.TransactionId) && t.Amount is not null)
                .Select(t => new PaymentLinkUpdateTransaction(
                    t.TransactionId!.Trim(),
                    t.PaymentMethod,
                    t.DatePaid is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
                    t.Amount!.Value,
                    t.AmountBreakdown?.Base,
                    t.AmountBreakdown?.Fee,
                    t.ApplicationFee))
                .ToList());

        return await subscriptionPayments.RecordAsync(update, cancellationToken) switch
        {
            SubscriptionPaymentResult.Credited credited => Outcome.Process(
                $"Cargo {credited.ChargeId} abonado (periodo {credited.PeriodId})."),
            SubscriptionPaymentResult.AlreadyCredited already => Outcome.Process($"Cargo {already.ChargeId} ya abonado."),
            SubscriptionPaymentResult.StatusUpdated updated => Outcome.Process($"Cargo {updated.ChargeId}: {updated.Status}."),
            SubscriptionPaymentResult.Unmatched unmatched => Outcome.Fail(unmatched.Reason),
            var other => throw new InvalidOperationException($"Resultado inesperado: {other}"),
        };
    }

    private async Task<Outcome> BankAccountUpdatedAsync(string body, CancellationToken cancellationToken)
    {
        var payload = PeyWebhookJson.Deserialize<PeyBankAccountUpdated>(body);
        if (payload is null || string.IsNullOrWhiteSpace(payload.BankAccountId))
        {
            return Outcome.Fail("Falta bankAccountId.");
        }

        if (payload.CodiStatus is not { } status)
        {
            return Outcome.Fail("Falta codiStatus.");
        }

        return await bankAccounts.ApplyWebhookAsync(payload.BankAccountId.Trim(), status, cancellationToken) switch
        {
            BankAccountStatusSyncResult.Synced synced => Outcome.Process(
                synced.Changed ? $"Cuenta {synced.Previous} → {synced.Current}; vendedor {synced.MerchantStatus}." : "Sin cambios."),
            BankAccountStatusSyncResult.NotFound => Outcome.Fail($"No hay cuenta con el id de Pey {payload.BankAccountId}."),
            var other => throw new InvalidOperationException($"Resultado inesperado: {other}"),
        };
    }

    private static string? TryReadType(string body)
    {
        try
        {
            var type = PeyWebhookJson.Deserialize<PeyWebhookEnvelope>(body)?.Type;
            return string.IsNullOrWhiteSpace(type) ? null : type.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private readonly record struct Outcome(WebhookHandlingResult Result, string? Reason)
    {
        public static Outcome Process(string? note = null) => new(WebhookHandlingResult.Processed, note);

        public static Outcome Ignore(string reason) => new(WebhookHandlingResult.Ignored, reason);

        public static Outcome Fail(string reason) => new(WebhookHandlingResult.Failed, reason);
    }
}
