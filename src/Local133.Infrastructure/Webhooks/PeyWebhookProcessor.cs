using System.Text.Json;
using Local133.Application.BankAccounts;
using Local133.Application.Payments;
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
/// cuando Pey lo reintenta. Nunca se loguea el body (trae la CLABE del pagador).
/// </summary>
internal sealed class PeyWebhookProcessor(
    AppDbContext db,
    IPaymentRecorder payments,
    IBankAccountStatusSync bankAccounts,
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
            retry.MarkRetryable($"{ex.GetType().Name}: {ex.Message}");
            await db.SaveChangesAsync(CancellationToken.None);
            return WebhookHandlingResult.RetryLater;
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

        return outcome.Result;
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
                PeyWebhookEventTypes.PaymentLinkStatusUpdated => Outcome.Ignore("Links de pago: fuera del MVP."),
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
