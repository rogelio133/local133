using System.Globalization;
using Local133.Application.Payments;
using Local133.Application.Sales;
using Local133.Application.Subscriptions;
using Local133.Domain.Payments;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Payments;

/// <summary>
/// Pago CoDi recibido (QR o notificación push) ⇒ <see cref="Payment"/> + venta pagada, en un
/// solo guardado; luego empieza la prueba de Pro si es su primer cobro (5.13) y avisa al vendedor en tiempo real.
/// Idempotente por <c>transactionId</c> (índice único). Sin dependencia de la sesión: lo usan los webhooks y la
/// conciliación (2.9).
/// </summary>
internal sealed class PaymentRecorder(
    AppDbContext db,
    ISaleNotifier notifier,
    IChargeLimitAlerts limitAlerts,
    ITrialStarter trialStarter,
    TimeProvider clock,
    ILogger<PaymentRecorder> logger) : IPaymentRecorder
{
    public async Task<PaymentRecordResult> RecordCodiPaymentAsync(
        ReceivedCodiPayment received, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(received);
        ArgumentException.ThrowIfNullOrWhiteSpace(received.TransactionId);

        var recordedSaleId = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.PeyTransactionId == received.TransactionId)
            .Select(p => (int?)p.SaleId)
            .SingleOrDefaultAsync(cancellationToken);
        if (recordedSaleId is { } alreadySaleId)
        {
            return new PaymentRecordResult.AlreadyRecorded(alreadySaleId);
        }

        var kind = Value(received.External, SaleMetadata.KindKey);
        if (kind == SaleMetadata.StaticKind)
        {
            return new PaymentRecordResult.Ignored("Pago del QR fijo: se registra a partir del paso 2.7.");
        }

        if (received.Amount <= 0)
        {
            return new PaymentRecordResult.Unmatched($"Monto inválido: {received.Amount.ToString(CultureInfo.InvariantCulture)}.");
        }

        var (sale, unmatched) = received.Channel == CodiPaymentChannel.PushNotification
            ? await FindPushSaleAsync(received, cancellationToken)
            : await FindSaleAsync(received, cancellationToken);
        if (sale is null)
        {
            return new PaymentRecordResult.Unmatched(unmatched!);
        }

        if (received.Channel == CodiPaymentChannel.PushNotification && sale.PeyTransactionId is null)
        {
            // Push cuyo envío quedó sin confirmar (Pey no respondió): ahora sí se sabe su transacción.
            sale.AttachPushNotification(received.TransactionId, peyCodiId: null);
            logger.LogWarning("La venta {SaleId} del vendedor {MerchantId} (cobro por celular sin confirmar) recibió el pago {PeyTransactionId}",
                sale.Id, sale.MerchantId, received.TransactionId);
        }

        var previousStatus = sale.Status;
        var payment = Payment.Record(
            sale.MerchantId, sale.Id, received.TransactionId, received.Amount, received.PaidAt,
            received.TrackingKey, received.PayerBankCode, clock.GetUtcNow());
        db.Payments.Add(payment);
        sale.MarkPaid(received.PaidAt);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Otra entrega del mismo pago ganó la carrera.
            db.ChangeTracker.Clear();
            return new PaymentRecordResult.AlreadyRecorded(sale.Id);
        }

        logger.LogInformation(
            "Pago {PeyTransactionId} de {Amount} en la venta {SaleId} (folio {Folio}) del vendedor {MerchantId}: venta {PreviousStatus} → {Status}",
            payment.PeyTransactionId, payment.Amount, sale.Id, sale.Folio, sale.MerchantId, previousStatus, sale.Status);
        if (previousStatus == SaleStatus.Paid)
        {
            logger.LogWarning("La venta {SaleId} del vendedor {MerchantId} recibió un pago adicional ({PeyTransactionId})",
                sale.Id, sale.MerchantId, payment.PeyTransactionId);
        }
        else if (previousStatus != SaleStatus.Pending)
        {
            logger.LogWarning("La venta {SaleId} del vendedor {MerchantId} estaba {PreviousStatus} y recibió el pago {PeyTransactionId}",
                sale.Id, sale.MerchantId, previousStatus, payment.PeyTransactionId);
        }

        if (payment.Amount != sale.Amount && sale.Source != SaleSource.OpenAmount)
        {
            logger.LogWarning("El pago {PeyTransactionId} ({Amount}) no coincide con el monto de la venta {SaleId} ({SaleAmount})",
                payment.PeyTransactionId, payment.Amount, sale.Id, sale.Amount);
        }

        if (previousStatus != SaleStatus.Paid)
        {
            // Primer cobro pagado ⇒ empieza la prueba de Pro, desde el PaidAt del pago (una conciliación tardía no le
            // quita minutos). Antes del aviso en tiempo real, para que Inicio ya la muestre. Nunca tumba el pago: si
            // falla, la empieza el respaldo de suscripciones-diario.
            try
            {
                await trialStarter.StartAsync(sale.MerchantId, payment.PaidAt, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "No se pudo empezar la prueba del vendedor {MerchantId} tras el pago de la venta {SaleId}; la empezará el job diario",
                    sale.MerchantId, sale.Id);
            }
        }

        try
        {
            await notifier.PaymentReceivedAsync(sale.MerchantId, sale.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // El pago ya quedó guardado; la vista también lo detecta con polling.
            logger.LogWarning(ex, "No se pudo avisar en tiempo real del pago de la venta {SaleId}", sale.Id);
        }

        if (previousStatus != SaleStatus.Paid)
        {
            // Un cobro concretado más en el mes: umbrales del gratuito y uso justo de Pro. Nunca tumba el pago.
            try
            {
                await limitAlerts.PaidChargeRecordedAsync(sale.MerchantId, sale.Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudieron revisar los límites del mes tras el pago de la venta {SaleId}", sale.Id);
            }
        }

        return new PaymentRecordResult.Recorded(payment.Id, sale.Id, sale.MerchantId);
    }

    /// <summary>
    /// Venta del pago: por el IDC del QR (<c>Sales.PeyQrId</c>) y, de respaldo, por <c>saleId</c> de la metadata. Si la
    /// metadata trae <c>merchantId</c> o <c>saleId</c>, debe coincidir con la venta encontrada.
    /// </summary>
    private async Task<(Sale? Sale, string? Unmatched)> FindSaleAsync(ReceivedCodiPayment received, CancellationToken cancellationToken)
    {
        var metaSaleId = IntValue(received.External, SaleMetadata.SaleIdKey);
        var metaMerchantId = IntValue(received.External, SaleMetadata.MerchantIdKey);

        Sale? sale = null;
        if (!string.IsNullOrWhiteSpace(received.CodiQrId))
        {
            sale = await db.Sales.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.PeyQrId == received.CodiQrId, cancellationToken);
        }

        if (sale is null && metaSaleId is { } saleId)
        {
            sale = await db.Sales.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.Id == saleId, cancellationToken);
            if (sale?.PeyQrId is { } qrId && received.CodiQrId is not null && qrId != received.CodiQrId)
            {
                return (null, $"La venta {saleId} de la metadata tiene otro QR ({qrId}), no {received.CodiQrId}.");
            }
        }

        if (sale is null)
        {
            return (null, $"No hay venta con el QR {received.CodiQrId ?? "(sin codiQrId)"} ni con saleId {Value(received.External, SaleMetadata.SaleIdKey) ?? "(sin saleId)"}.");
        }

        if (sale.Method == ChargeMethod.PushNotification)
        {
            return (null, $"La venta {sale.Id} es un cobro por celular; no puede recibir un pago de QR.");
        }

        return CheckMetadata(sale, metaMerchantId, metaSaleId, $"el QR {received.CodiQrId}");
    }

    /// <summary>
    /// Venta de un pago de notificación push: por la transacción que devolvió <c>POST /codi/pn</c>
    /// (<c>Sales.PeyTransactionId</c>) y, de respaldo, por <c>saleId</c> de la metadata. Por metadata solo vale un
    /// cobro por celular sin transacción (envío sin confirmar) o con la misma.
    /// </summary>
    private async Task<(Sale? Sale, string? Unmatched)> FindPushSaleAsync(ReceivedCodiPayment received, CancellationToken cancellationToken)
    {
        var metaSaleId = IntValue(received.External, SaleMetadata.SaleIdKey);
        var metaMerchantId = IntValue(received.External, SaleMetadata.MerchantIdKey);

        var sale = await db.Sales.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.PeyTransactionId == received.TransactionId, cancellationToken);
        if (sale is null && metaSaleId is { } saleId)
        {
            sale = await db.Sales.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.Id == saleId, cancellationToken);
            if (sale?.PeyTransactionId is { } otherTransaction && otherTransaction != received.TransactionId)
            {
                return (null, $"La venta {saleId} de la metadata tiene otra transacción ({otherTransaction}), no {received.TransactionId}.");
            }
        }

        if (sale is null)
        {
            return (null, $"No hay venta con la transacción {received.TransactionId} ni con saleId {Value(received.External, SaleMetadata.SaleIdKey) ?? "(sin saleId)"}.");
        }

        if (sale.Method != ChargeMethod.PushNotification)
        {
            return (null, $"La venta {sale.Id} no es un cobro por celular; no puede recibir un pago de notificación push.");
        }

        return CheckMetadata(sale, metaMerchantId, metaSaleId, $"la transacción {received.TransactionId}");
    }

    /// <summary>Si la metadata trae <c>merchantId</c> o <c>saleId</c>, debe coincidir con la venta encontrada.</summary>
    private static (Sale? Sale, string? Unmatched) CheckMetadata(Sale sale, int? metaMerchantId, int? metaSaleId, string foundBy)
    {
        if (metaMerchantId is { } merchantId && merchantId != sale.MerchantId)
        {
            return (null, $"La metadata dice vendedor {merchantId}, pero la venta {sale.Id} es del vendedor {sale.MerchantId}.");
        }

        if (metaSaleId is { } expectedSaleId && expectedSaleId != sale.Id)
        {
            return (null, $"La metadata dice venta {expectedSaleId}, pero {foundBy} es de la venta {sale.Id}.");
        }

        return (sale, null);
    }

    private static string? Value(IReadOnlyDictionary<string, string?> external, string key) =>
        external.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static int? IntValue(IReadOnlyDictionary<string, string?> external, string key) =>
        int.TryParse(Value(external, key), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
