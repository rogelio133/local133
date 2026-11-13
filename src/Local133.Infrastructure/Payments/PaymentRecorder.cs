using System.Globalization;
using Local133.Application.Payments;
using Local133.Application.Sales;
using Local133.Domain.Payments;
using Local133.Domain.Sales;
using Local133.Domain.Settings;
using Local133.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Payments;

/// <summary>
/// Pago CoDi recibido en un QR ⇒ <see cref="Payment"/> con la comisión vigente + venta pagada, en un solo guardado;
/// luego avisa al vendedor en tiempo real. Idempotente por <c>transactionId</c> (índice único). Sin dependencia de la
/// sesión: lo usan el webhook y la conciliación (2.9).
/// </summary>
internal sealed class PaymentRecorder(
    AppDbContext db,
    ISaleNotifier notifier,
    TimeProvider clock,
    ILogger<PaymentRecorder> logger) : IPaymentRecorder
{
    public async Task<PaymentRecordResult> RecordCodiQrPaymentAsync(
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
        if (kind == SaleMetadata.StatementKind)
        {
            return new PaymentRecordResult.Ignored("Pago de un estado de cuenta: se concilia en la Fase 5.");
        }

        if (kind == SaleMetadata.StaticKind)
        {
            return new PaymentRecordResult.Ignored("Pago del QR fijo: se registra a partir del paso 2.7.");
        }

        if (received.Amount <= 0)
        {
            return new PaymentRecordResult.Unmatched($"Monto inválido: {received.Amount.ToString(CultureInfo.InvariantCulture)}.");
        }

        var (sale, unmatched) = await FindSaleAsync(received, cancellationToken);
        if (sale is null)
        {
            return new PaymentRecordResult.Unmatched(unmatched!);
        }

        var merchant = await db.Merchants.AsNoTracking().SingleAsync(m => m.Id == sale.MerchantId, cancellationToken);
        var globalRate = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Id == SystemSettings.SingletonId)
            .Select(s => s.CommissionRate)
            .SingleAsync(cancellationToken);
        var rate = Commission.EffectiveRate(merchant.CommissionRateOverride, globalRate);

        var previousStatus = sale.Status;
        var payment = Payment.Record(
            sale.MerchantId, sale.Id, received.TransactionId, received.Amount, received.PaidAt,
            received.TrackingKey, received.PayerBankCode, rate, clock.GetUtcNow());
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
            "Pago {PeyTransactionId} de {Amount} en la venta {SaleId} (folio {Folio}) del vendedor {MerchantId}: comisión {CommissionAmount} ({CommissionRate}); venta {PreviousStatus} → {Status}",
            payment.PeyTransactionId, payment.Amount, sale.Id, sale.Folio, sale.MerchantId, payment.CommissionAmount,
            payment.CommissionRate, previousStatus, sale.Status);
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

        try
        {
            await notifier.PaymentReceivedAsync(sale.MerchantId, sale.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // El pago ya quedó guardado; la vista también lo detecta con polling.
            logger.LogWarning(ex, "No se pudo avisar en tiempo real del pago de la venta {SaleId}", sale.Id);
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

        if (metaMerchantId is { } merchantId && merchantId != sale.MerchantId)
        {
            return (null, $"La metadata dice vendedor {merchantId}, pero la venta {sale.Id} es del vendedor {sale.MerchantId}.");
        }

        if (metaSaleId is { } expectedSaleId && expectedSaleId != sale.Id)
        {
            return (null, $"La metadata dice venta {expectedSaleId}, pero el QR {received.CodiQrId} es de la venta {sale.Id}.");
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
