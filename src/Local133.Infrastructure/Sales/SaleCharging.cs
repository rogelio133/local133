using System.Globalization;
using FluentValidation;
using Local133.Application.Payments;
using Local133.Application.Sales;
using Local133.Application.Subscriptions;
using Local133.Domain.Pey;
using Local133.Domain.Subscriptions;
using Local133.Domain.Sales;
using Local133.Domain.Settings;
using Local133.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Sales;

/// <summary>
/// Venta por monto directo o con carrito: valida → guarda la venta (para tener su folio e id; con carrito, junto con
/// la copia de sus productos) → <c>POST /codi/qr</c> (o <c>POST /codi/pn</c> al celular del pagador) hacia la cuenta
/// del vendedor con <c>concept = "{negocio} · {concepto}"</c> → adjunta el QR (o la transacción del push). Si Pey
/// falla, la venta se borra; un push sin respuesta de Pey se conserva cancelado (ver <see cref="SendPushAsync"/>).
/// </summary>
internal sealed class SaleCharging(
    AppDbContext db,
    IPaymentGateway gateway,
    IValidator<CreateDirectSaleCommand> validator,
    IValidator<CreateCartSaleCommand> cartValidator,
    IPlanAccess plans,
    IChargeUsage usage,
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

    private const string PushRejectedFailure =
        "El servicio de pagos no aceptó el cobro. Revisa el monto y el celular, o cobra con QR.";

    private const string PushGenericFailure =
        "No pudimos enviar el cobro al celular por un problema de nuestro lado. Intenta de nuevo más tarde o cobra con QR.";

    private const string PushUnconfirmedFailure =
        "No pudimos confirmar el envío del cobro al celular de tu cliente. Si le llegó la solicitud y la paga, verás el pago aquí. Si no le llegó, cobra de nuevo.";

    public async Task<CreateSaleResult> CreateDirectSaleAsync(
        CreateDirectSaleCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return new CreateSaleResult.Invalid(validation.ToDictionary().AsReadOnly());
        }

        if (await LoadChargerAsync(command.UserId, cancellationToken) is not { } charger)
        {
            return new CreateSaleResult.NotAllowed();
        }

        if (await PlanBlockAsync(charger.Merchant.Id, withCart: false, cancellationToken) is { } blocked)
        {
            return blocked;
        }

        return await ChargeAsync(
            charger, command.Method, command.PhoneNumber, command.Amount, command.Concept!, items: null, cancellationToken);
    }

    public async Task<CreateSaleResult> CreateCartSaleAsync(
        CreateCartSaleCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await cartValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return new CreateSaleResult.Invalid(validation.ToDictionary().AsReadOnly());
        }

        if (await LoadChargerAsync(command.UserId, cancellationToken) is not { } charger)
        {
            return new CreateSaleResult.NotAllowed();
        }

        if (await PlanBlockAsync(charger.Merchant.Id, withCart: true, cancellationToken) is { } blocked)
        {
            return blocked;
        }

        // Explícito por MerchantId y sin filtro (como el catálogo): un producto de otro vendedor no existe para este.
        var merchantId = charger.Merchant.Id;
        var ids = command.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.MerchantId == merchantId && !p.IsDeleted && ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        if (products.Count != ids.Count)
        {
            return Invalid(nameof(CreateCartSaleCommand.Lines), SaleCartMessages.ProductUnavailable);
        }

        // Cada intento de folio arma su propia copia (las entidades de un intento fallido se desconectan).
        List<SaleItem> BuildItems() => command.Lines.Select(l => SaleItem.FromProduct(products[l.ProductId], l.Quantity)).ToList();
        var total = SaleCart.Total(BuildItems());
        if (total > Sale.MaxAmount)
        {
            return Invalid(nameof(CreateCartSaleCommand.Lines), SaleCartMessages.TotalTooHigh);
        }

        if (total != command.ExpectedTotal)
        {
            logger.LogInformation("Vendedor {MerchantId}: el total del carrito cambió ({ExpectedTotal} ⇒ {Total}); no se cobra",
                merchantId, command.ExpectedTotal, total);
            return Invalid(nameof(CreateCartSaleCommand.ExpectedTotal), SaleCartMessages.PricesChanged);
        }

        return await ChargeAsync(
            charger, command.Method, command.PhoneNumber, total, command.Concept!, BuildItems, cancellationToken);
    }

    private static CreateSaleResult.Invalid Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] }.AsReadOnly());

    /// <summary>
    /// Límites del plan (§5): el carrito es de Pro, y en el gratuito no se genera un cobro con el límite del mes
    /// concretado ni con el tope de generados. Se revisa antes de insertar la venta y de llamar a Pey. Dos
    /// cobros simultáneos en el borde pueden pasar los dos (se acepta: el tope es un freno, no un saldo).
    /// </summary>
    private async Task<CreateSaleResult?> PlanBlockAsync(int merchantId, bool withCart, CancellationToken cancellationToken)
    {
        var plan = await plans.GetForMerchantAsync(merchantId, cancellationToken);
        if (withCart && !plan.Entitlements.CanChargeWithCart)
        {
            logger.LogInformation("Vendedor {MerchantId} (plan {Tier}) intentó cobrar con carrito", merchantId, plan.Tier);
            return new CreateSaleResult.PlanRequired();
        }

        if (!plan.Entitlements.HasMonthlyChargeLimit)
        {
            return null;
        }

        var month = await usage.GetMonthAsync(merchantId, cancellationToken);
        CreateSaleResult? blocked = month.Allowance.State switch
        {
            ChargeAllowanceState.Exhausted => new CreateSaleResult.MonthlyLimitReached(month.ResetsOn),
            ChargeAllowanceState.GenerationCapReached => new CreateSaleResult.GenerationLimitReached(month.ResetsOn),
            _ => null,
        };
        if (blocked is not null)
        {
            logger.LogInformation("Vendedor {MerchantId} (gratuito) sin cobros disponibles en el mes: {Paid} concretados, {Generated} generados ({State})",
                merchantId, month.Paid, month.Generated, month.Allowance.State);
        }

        return blocked;
    }

    /// <summary>Vendedor y cuenta bancaria, solo si puede cobrar.</summary>
    private sealed record Charger(Domain.Merchants.Merchant Merchant, string PeyBankAccountId);

    private async Task<Charger?> LoadChargerAsync(string userId, CancellationToken cancellationToken)
    {
        var merchant = await db.Merchants.AsNoTracking().SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException($"El usuario {userId} no tiene Merchant.");
        // Explícito por MerchantId y sin filtro: la decisión no depende del claim de la sesión.
        var account = await db.BankAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(b => b.MerchantId == merchant.Id, cancellationToken);
        return merchant.CanCharge(account) ? new Charger(merchant, account!.PeyBankAccountId) : null;
    }

    /// <summary>
    /// Guarda la venta (con <paramref name="items"/>, una venta con carrito) y pide a Pey el QR o el cobro al celular.
    /// </summary>
    private async Task<CreateSaleResult> ChargeAsync(
        Charger charger, ChargeMethod method, string? phoneNumber, decimal amount, string conceptText,
        Func<List<SaleItem>>? items, CancellationToken cancellationToken)
    {
        var merchant = charger.Merchant;
        var expirationSeconds = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Id == SystemSettings.SingletonId)
            .Select(s => s.SaleQrExpirationSeconds)
            .SingleAsync(cancellationToken);

        // La hora se toma antes de llamar a Pey: la cuenta regresiva queda corta, nunca larga.
        var now = clock.GetUtcNow();
        DateTimeOffset? expiresAt = expirationSeconds == PeyLimits.NoExpiration ? null : now.AddSeconds(expirationSeconds);

        // Solo lo que escribió el vendedor (ajustes 2026-11-26): ya no se antepone el nombre del negocio.
        var concept = conceptText.Trim();
        if (method == ChargeMethod.PushNotification)
        {
            if (expiresAt is not { } pushExpiresAt)
            {
                // Pey exige ≥ 360 s en un push; el parámetro global "0 = no expira" solo aplica al QR.
                throw new InvalidOperationException("La expiración de los cobros no puede ser 0 para un cobro por celular.");
            }

            var phone = PhoneNumber.Normalize(phoneNumber);
            var pushSale = await InsertWithNextFolioAsync(
                merchant.Id,
                folio => Sale.StartPush(merchant.Id, folio, amount, concept, phone, pushExpiresAt, now, items?.Invoke()),
                cancellationToken);
            return await SendPushAsync(pushSale, merchant.Id, charger.PeyBankAccountId, expirationSeconds);
        }

        var sale = await InsertWithNextFolioAsync(
            merchant.Id, folio => Sale.StartDirect(merchant.Id, folio, amount, concept, expiresAt, now, items?.Invoke()),
            cancellationToken);

        CodiQr qr;
        try
        {
            qr = await gateway.CreateCodiQrAsync(new CreateCodiQrRequest(
                sale.Amount,
                expirationSeconds,
                charger.PeyBankAccountId,
                sale.Concept,
                sale.NumRef,
                MetadataFor(sale)), cancellationToken);
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

        logger.LogInformation("Vendedor {MerchantId} creó la venta {SaleId} (folio {Folio}, {Source}, {ItemCount} productos) por {Amount} con el QR {PeyQrId}",
            merchant.Id, sale.Id, sale.Folio, sale.Source, sale.Items.Count, sale.Amount, qr.Id);
        return new CreateSaleResult.Created(sale.Id);
    }

    /// <summary>
    /// <c>POST /codi/pn</c> al celular de la venta, hacia la cuenta del vendedor. Si Pey no responde (resultado
    /// incierto) la venta no se borra: la notificación pudo llegarle al cliente, así que queda cancelada y, si aun así
    /// paga, el webhook la encuentra por la <c>saleId</c> de la metadata.
    /// </summary>
    private async Task<CreateSaleResult> SendPushAsync(Sale sale, int merchantId, string bankAccountId, int expirationSeconds)
    {
        var maskedPhone = PhoneNumber.Mask(sale.PhoneNumber);
        CodiPushNotification push;
        try
        {
            // Sin el token de la petición: si el vendedor cierra la página a medio envío, no se deja la venta a medias.
            push = await gateway.CreateCodiPushNotificationAsync(new CreateCodiPushNotificationRequest(
                sale.Amount,
                sale.PhoneNumber!,
                bankAccountId,
                expirationSeconds,
                sale.Concept,
                sale.NumRef,
                MetadataFor(sale)), CancellationToken.None);
        }
        catch (PaymentGatewayValidationException ex)
        {
            logger.LogWarning("Pey rechazó el cobro por celular {MaskedPhone} de la venta {SaleId} del vendedor {MerchantId} ({ErrorType}); requestId {RequestId}: {GatewayMessage}",
                maskedPhone, sale.Id, merchantId, ex.ErrorType, ex.RequestId, ex.GatewayMessage);
            return await DiscardAsync(sale, PushRejectedFailure);
        }
        catch (PaymentGatewayUnavailableException ex)
        {
            logger.LogWarning(ex, "Cobro por celular {MaskedPhone} de la venta {SaleId} del vendedor {MerchantId} sin confirmar (Pey no disponible); requestId {RequestId}. La venta queda cancelada por si llega el pago",
                maskedPhone, sale.Id, merchantId, ex.RequestId);
            sale.Cancel(clock.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None);
            return new CreateSaleResult.GatewayFailed(PushUnconfirmedFailure, sale.Id);
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogError(ex, "Pey falló al enviar el cobro por celular de la venta {SaleId} del vendedor {MerchantId} ({ErrorType}); requestId {RequestId}",
                sale.Id, merchantId, ex.ErrorType, ex.RequestId);
            return await DiscardAsync(sale, PushGenericFailure);
        }
        catch (InvalidOperationException ex)
        {
            // Configuración (p. ej. falta Pey:ApiKey): no se llegó a enviar.
            logger.LogError(ex, "No se pudo llamar a Pey para el cobro por celular de la venta {SaleId} del vendedor {MerchantId}", sale.Id, merchantId);
            return await DiscardAsync(sale, PushGenericFailure);
        }

        sale.AttachPushNotification(push.TransactionId, push.CodiId);
        await db.SaveChangesAsync(CancellationToken.None); // la notificación ya salió: no se abandona el guardado

        logger.LogInformation("Vendedor {MerchantId} creó la venta {SaleId} (folio {Folio}, {Source}, {ItemCount} productos) por {Amount} con cobro al celular {MaskedPhone}; transacción {PeyTransactionId}",
            merchantId, sale.Id, sale.Folio, sale.Source, sale.Items.Count, sale.Amount, maskedPhone, push.TransactionId);
        return new CreateSaleResult.Created(sale.Id);
    }

    private static Dictionary<string, string> MetadataFor(Sale sale) => new()
    {
        [SaleMetadata.KindKey] = SaleMetadata.SaleKind,
        [SaleMetadata.MerchantIdKey] = sale.MerchantId.ToString(CultureInfo.InvariantCulture),
        [SaleMetadata.SaleIdKey] = sale.Id.ToString(CultureInfo.InvariantCulture),
    };

    public async Task<CancelSaleResult> CancelSaleAsync(CancelSaleCommand command, CancellationToken cancellationToken = default)
    {
        var merchantId = await db.Merchants.AsNoTracking()
            .Where(m => m.UserId == command.UserId)
            .Select(m => (int?)m.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"El usuario {command.UserId} no tiene Merchant.");

        // Explícito por MerchantId: la venta de otro vendedor no existe para este.
        var sale = await db.Sales.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == command.SaleId && s.MerchantId == merchantId, cancellationToken);
        if (sale is null)
        {
            return new CancelSaleResult.NotFound();
        }

        var now = clock.GetUtcNow();
        if (!sale.CanCancel || sale.EffectiveStatus(now) != SaleStatus.Pending)
        {
            return NotCancellable(sale.EffectiveStatus(now));
        }

        // UPDATE condicional (atómico): si el webhook la marcó pagada o el job la venció mientras tanto, no se pisa.
        var cancelled = await db.Sales.IgnoreQueryFilters()
            .Where(s => s.Id == sale.Id && s.MerchantId == merchantId && s.Status == SaleStatus.Pending
                && (s.ExpiresAt == null || s.ExpiresAt > now))
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Status, SaleStatus.Cancelled)
                .SetProperty(s => s.CancelledAt, now), cancellationToken);
        if (cancelled == 0)
        {
            var current = await db.Sales.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.Id == sale.Id)
                .SingleAsync(cancellationToken);
            return NotCancellable(current.EffectiveStatus(now));
        }

        logger.LogInformation("Vendedor {MerchantId} canceló la venta {SaleId} (folio {Folio})", merchantId, sale.Id, sale.Folio);
        return new CancelSaleResult.Cancelled();
    }

    private static CancelSaleResult.NotCancellable NotCancellable(SaleStatus status) => new(status switch
    {
        SaleStatus.Paid => "Este cobro ya se pagó; no se puede cancelar.",
        SaleStatus.Expired => "Este cobro ya expiró; no hace falta cancelarlo.",
        SaleStatus.Cancelled => "Este cobro ya estaba cancelado.",
        _ => "Este cobro no se puede cancelar.",
    });

    /// <summary>Inserta la venta con el siguiente folio del vendedor; si otra venta simultánea lo tomó, reintenta.</summary>
    private async Task<Sale> InsertWithNextFolioAsync(
        int merchantId, Func<int, Sale> start, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var lastFolio = await db.Sales.IgnoreQueryFilters()
                .Where(s => s.MerchantId == merchantId)
                .MaxAsync(s => (int?)s.Folio, cancellationToken) ?? 0;
            var folio = lastFolio + 1;
            var sale = start(folio);
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
                foreach (var item in sale.Items)
                {
                    // Los productos del intento fallido también: si no, se insertarían con la venta siguiente.
                    db.Entry(item).State = EntityState.Detached;
                }
            }
        }
    }

    private async Task<CreateSaleResult> DiscardAsync(Sale sale, string message)
    {
        db.Sales.Remove(sale);
        await db.SaveChangesAsync(CancellationToken.None);
        return new CreateSaleResult.GatewayFailed(message);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
