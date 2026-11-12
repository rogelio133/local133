using System.Globalization;
using FluentValidation;
using Local133.Application.Payments;
using Local133.Application.Sales;
using Local133.Domain.Pey;
using Local133.Domain.Sales;
using Local133.Domain.Settings;
using Local133.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Sales;

/// <summary>
/// Venta por monto directo: valida → guarda la venta (para tener su folio e id) → <c>POST /codi/qr</c> hacia la
/// cuenta del vendedor con <c>concept = "{negocio} · {concepto}"</c> → adjunta el QR. Si Pey falla, la venta se borra.
/// </summary>
internal sealed class SaleCharging(
    AppDbContext db,
    IPaymentGateway gateway,
    IValidator<CreateDirectSaleCommand> validator,
    TimeProvider clock,
    ILogger<SaleCharging> logger) : ISaleCharging
{
    private const int MaxFolioAttempts = 3;

    private const string GenericFailure =
        "No pudimos generar el QR por un problema de nuestro lado. Intenta de nuevo más tarde.";

    private const string UnavailableFailure =
        "No pudimos comunicarnos con el servicio de pagos. Intenta de nuevo en unos minutos.";

    private const string RejectedFailure =
        "El servicio de pagos no aceptó el cobro. Revisa el monto e intenta de nuevo.";

    public async Task<CreateDirectSaleResult> CreateDirectSaleAsync(
        CreateDirectSaleCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return new CreateDirectSaleResult.Invalid(validation.ToDictionary().AsReadOnly());
        }

        var merchant = await db.Merchants.AsNoTracking().SingleOrDefaultAsync(m => m.UserId == command.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"El usuario {command.UserId} no tiene Merchant.");
        // Explícito por MerchantId y sin filtro: la decisión no depende del claim de la sesión.
        var account = await db.BankAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(b => b.MerchantId == merchant.Id, cancellationToken);
        if (!merchant.CanCharge(account))
        {
            return new CreateDirectSaleResult.NotAllowed();
        }

        var expirationSeconds = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Id == SystemSettings.SingletonId)
            .Select(s => s.SaleQrExpirationSeconds)
            .SingleAsync(cancellationToken);

        // La hora se toma antes de llamar a Pey: la cuenta regresiva queda corta, nunca larga.
        var now = clock.GetUtcNow();
        DateTimeOffset? expiresAt = expirationSeconds == PeyLimits.NoExpiration ? null : now.AddSeconds(expirationSeconds);

        var concept = Sale.BuildConcept(merchant.BusinessName, command.Concept!);
        var sale = await InsertWithNextFolioAsync(merchant.Id, concept, command.Amount, expiresAt, now, cancellationToken);

        CodiQr qr;
        try
        {
            qr = await gateway.CreateCodiQrAsync(new CreateCodiQrRequest(
                sale.Amount,
                expirationSeconds,
                sale.Concept,
                sale.NumRef,
                account!.PeyBankAccountId,
                new Dictionary<string, string>
                {
                    [SaleMetadata.KindKey] = SaleMetadata.SaleKind,
                    [SaleMetadata.MerchantIdKey] = merchant.Id.ToString(CultureInfo.InvariantCulture),
                    [SaleMetadata.SaleIdKey] = sale.Id.ToString(CultureInfo.InvariantCulture),
                }), cancellationToken);
        }
        catch (PaymentGatewayValidationException ex)
        {
            logger.LogWarning("Pey rechazó el QR de la venta {SaleId} del vendedor {MerchantId} ({ErrorType}); requestId {RequestId}: {GatewayMessage}",
                sale.Id, merchant.Id, ex.ErrorType, ex.RequestId, ex.GatewayMessage);
            return await DiscardAsync(sale, RejectedFailure);
        }
        catch (PaymentGatewayUnavailableException ex)
        {
            // En un POST sin respuesta el resultado es incierto: Pey pudo haber creado el QR, pero nadie lo tiene.
            logger.LogWarning(ex, "QR de la venta {SaleId} del vendedor {MerchantId} sin confirmar (Pey no disponible); requestId {RequestId}",
                sale.Id, merchant.Id, ex.RequestId);
            return await DiscardAsync(sale, UnavailableFailure);
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogError(ex, "Pey falló al crear el QR de la venta {SaleId} del vendedor {MerchantId} ({ErrorType}); requestId {RequestId}",
                sale.Id, merchant.Id, ex.ErrorType, ex.RequestId);
            return await DiscardAsync(sale, GenericFailure);
        }
        catch (InvalidOperationException ex)
        {
            // Configuración (p. ej. falta Pey:ApiKey).
            logger.LogError(ex, "No se pudo llamar a Pey para el QR de la venta {SaleId} del vendedor {MerchantId}", sale.Id, merchant.Id);
            return await DiscardAsync(sale, GenericFailure);
        }

        sale.AttachQr(qr.Id, qr.QrString);
        await db.SaveChangesAsync(CancellationToken.None); // el QR ya existe en Pey: no se abandona el guardado

        logger.LogInformation("Vendedor {MerchantId} creó la venta {SaleId} (folio {Folio}) por {Amount} con el QR {PeyQrId}",
            merchant.Id, sale.Id, sale.Folio, sale.Amount, qr.Id);
        return new CreateDirectSaleResult.Created(sale.Id);
    }

    /// <summary>Inserta la venta con el siguiente folio del vendedor; si otra venta simultánea lo tomó, reintenta.</summary>
    private async Task<Sale> InsertWithNextFolioAsync(
        int merchantId, string concept, decimal amount, DateTimeOffset? expiresAt, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var lastFolio = await db.Sales.IgnoreQueryFilters()
                .Where(s => s.MerchantId == merchantId)
                .MaxAsync(s => (int?)s.Folio, cancellationToken) ?? 0;
            var folio = lastFolio + 1;
            var sale = Sale.StartDirect(merchantId, folio, amount, concept, expiresAt, now);
            db.Sales.Add(sale);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return sale;
            }
            catch (DbUpdateException ex) when (attempt < MaxFolioAttempts && IsUniqueViolation(ex))
            {
                logger.LogInformation("Folio {Folio} del vendedor {MerchantId} ya tomado; reintento {Attempt}", folio, merchantId, attempt);
                db.Entry(sale).State = EntityState.Detached;
            }
        }
    }

    private async Task<CreateDirectSaleResult> DiscardAsync(Sale sale, string message)
    {
        db.Sales.Remove(sale);
        await db.SaveChangesAsync(CancellationToken.None);
        return new CreateDirectSaleResult.GatewayFailed(message);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
