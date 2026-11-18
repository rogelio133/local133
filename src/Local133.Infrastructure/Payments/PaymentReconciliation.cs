using Local133.Application.Payments;
using Local133.Domain.Pey;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Payments;

/// <summary>
/// Pregunta a Pey por los pagos de una venta sin pagar y registra los que falten con el mismo
/// <see cref="IPaymentRecorder"/> del webhook. Si Pey responde, sella <see cref="Sale.PaymentCheckedAt"/>; si falla,
/// no lo sella y la siguiente corrida del job lo vuelve a intentar.
/// </summary>
internal sealed class PaymentReconciliation(
    AppDbContext db,
    IPaymentGateway gateway,
    IPaymentRecorder recorder,
    TimeProvider clock,
    ILogger<PaymentReconciliation> logger) : IPaymentReconciliation
{
    /// <summary>Tope de páginas de <c>GET /transaction/codi/qr/{idc}</c> (20 pagos cada una).</summary>
    public const int MaxQrPages = 10;

    public async Task<PaymentReconciliationResult> ReconcileSaleAsync(int saleId, CancellationToken cancellationToken = default)
    {
        var sale = await db.Sales.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == saleId)
            .Select(s => new { s.Id, s.MerchantId, s.Method, s.Status, s.PeyQrId, s.PeyTransactionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (sale is null)
        {
            return new PaymentReconciliationResult.SaleNotEligible($"No existe la venta {saleId}.");
        }

        if (sale.Status == SaleStatus.Paid)
        {
            return new PaymentReconciliationResult.SaleNotEligible($"La venta {saleId} ya está pagada.");
        }

        var isPush = sale.Method == ChargeMethod.PushNotification;
        var lookupId = isPush ? sale.PeyTransactionId : sale.Method == ChargeMethod.DynamicQr ? sale.PeyQrId : null;
        if (string.IsNullOrWhiteSpace(lookupId))
        {
            return new PaymentReconciliationResult.SaleNotEligible($"La venta {saleId} no tiene QR ni transacción que consultar.");
        }

        IReadOnlyList<GatewayTransaction> transactions;
        try
        {
            transactions = isPush
                ? [await gateway.GetTransactionAsync(lookupId, cancellationToken)]
                : await QrTransactionsAsync(lookupId, cancellationToken);
        }
        catch (PaymentGatewayNotFoundException)
        {
            // Pey no conoce el QR o la transacción: no hay pagos; se sella para no preguntar en cada corrida.
            logger.LogWarning("Conciliación: Pey no encontró {LookupKind} de la venta {SaleId} del vendedor {MerchantId}",
                isPush ? "la transacción" : "el QR", sale.Id, sale.MerchantId);
            transactions = [];
        }
        catch (Exception ex) when (ex is PaymentGatewayException or InvalidOperationException)
        {
            // InvalidOperationException: falta la API key (configuración).
            logger.LogWarning("Conciliación: no se pudo consultar a Pey por la venta {SaleId}: {Message}", sale.Id, ex.Message);
            return new PaymentReconciliationResult.GatewayFailed(ex.Message);
        }

        int recorded = 0, already = 0, unmatched = 0;
        foreach (var transaction in transactions.Where(t => t.Status == PeyTransactionStatus.Paid))
        {
            var result = await recorder.RecordCodiPaymentAsync(ToReceivedPayment(transaction, sale.PeyQrId, isPush), cancellationToken);
            switch (result)
            {
                case PaymentRecordResult.Recorded:
                    recorded++;
                    logger.LogWarning("Conciliación: el pago {PeyTransactionId} de la venta {SaleId} no había llegado por webhook",
                        transaction.Id, sale.Id);
                    break;
                case PaymentRecordResult.AlreadyRecorded:
                    already++;
                    break;
                case PaymentRecordResult.Ignored ignored:
                    logger.LogInformation("Conciliación: pago {PeyTransactionId} ignorado: {Reason}", transaction.Id, ignored.Reason);
                    break;
                case PaymentRecordResult.Unmatched notMatched:
                    unmatched++;
                    logger.LogError("Conciliación: el pago {PeyTransactionId} de la venta {SaleId} no se pudo asociar: {Reason}",
                        transaction.Id, sale.Id, notMatched.Reason);
                    break;
            }
        }

        var now = clock.GetUtcNow();
        await db.Sales.IgnoreQueryFilters()
            .Where(s => s.Id == sale.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.PaymentCheckedAt, now), cancellationToken);

        return new PaymentReconciliationResult.Checked(recorded, already, unmatched);
    }

    private async Task<IReadOnlyList<GatewayTransaction>> QrTransactionsAsync(string codiQrId, CancellationToken cancellationToken)
    {
        var all = new List<GatewayTransaction>();
        string? nextToken = null;
        for (var page = 0; page < MaxQrPages; page++)
        {
            var result = await gateway.GetCodiQrTransactionsAsync(codiQrId, nextToken, cancellationToken);
            all.AddRange(result.Items);
            nextToken = result.NextToken;
            if (nextToken is null)
            {
                return all;
            }
        }

        logger.LogWarning("Conciliación: el QR {CodiQrId} tiene más de {MaxPages} páginas de pagos; se revisaron solo las primeras",
            codiQrId, MaxQrPages);
        return all;
    }

    private ReceivedCodiPayment ToReceivedPayment(GatewayTransaction transaction, string? codiQrId, bool isPush)
    {
        var paidAt = transaction.PaidAt ?? transaction.CreatedAt;
        if (paidAt is null)
        {
            logger.LogWarning("Conciliación: el pago {PeyTransactionId} no trae fecha; se usa la hora actual", transaction.Id);
        }

        return new ReceivedCodiPayment(
            transaction.Id.Trim(),
            isPush ? null : codiQrId,
            transaction.Amount,
            paidAt ?? clock.GetUtcNow(),
            transaction.Metadata.TrackingKey,
            transaction.Metadata.BankCode,
            transaction.Metadata.External,
            isPush ? CodiPaymentChannel.PushNotification : CodiPaymentChannel.Qr);
    }
}
