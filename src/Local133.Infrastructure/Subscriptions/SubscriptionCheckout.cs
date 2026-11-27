using System.Globalization;
using Local133.Application.Payments;
using Local133.Application.Subscriptions;
using Local133.Domain.Merchants;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>
/// Checkout por pasos (5.6, 5.15): cotiza los paquetes de cada plan, arma el resumen del pedido y, solo al pulsar
/// «Pagar», crea el cargo con su link de pago de Pey (§4 del diseño de suscripciones). Antes vuelve a cotizar: si el
/// total ya no es el que vio el vendedor, no crea nada. El cargo se inserta antes de llamar a Pey (su id va en
/// <c>clientReferenceId</c> y en la metadata) y guarda la versión de los Términos que aceptó. Un link <c>pending</c> del
/// mismo plan, paquete, monto y versión de los Términos creado en los últimos <c>GraceDays</c> días se reutiliza; uno
/// más viejo se abandona (5.7). El job diario no crea links. Si Pey no responde, el cargo queda <c>Failed</c>: nadie
/// conoce la URL del link, así que nadie lo puede pagar, y al reintentar se crea otro.
/// </summary>
internal sealed class SubscriptionCheckout(
    AppDbContext db,
    IPaymentGateway gateway,
    TimeProvider clock,
    ILogger<SubscriptionCheckout> logger) : ISubscriptionCheckout
{
    internal const string NotStartedMessage =
        "Tu prueba de Pro empieza con tu primer cobro. Después podrás elegir tu plan.";

    internal const string DisabledMessage = "Tu cuenta está dada de baja. Escríbenos si quieres reactivarla.";

    internal const string RejectedFailure =
        "Pey no aceptó generar el link de pago. Intenta de nuevo; si sigue pasando, escríbenos.";

    internal const string UnavailableFailure =
        "Pey no respondió a tiempo y no se generó el link de pago. Intenta de nuevo en unos minutos.";

    internal const string GenericFailure = "No pudimos generar el link de pago. Intenta de nuevo en unos minutos.";

    public async Task<SubscriptionOffersResult> GetOffersAsync(
        int merchantId, SubscriptionPlan? plan = null, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(merchantId, cancellationToken);
        if (context.Subscription is null || context.MerchantStatus == MerchantStatus.Disabled)
        {
            return new SubscriptionOffersResult(false, [], context.Subscription?.IsFounder ?? false, context.FounderSlotsLeft);
        }

        var now = clock.GetUtcNow();
        SubscriptionPlan[] plans = plan is { } one ? [one] : Enum.GetValues<SubscriptionPlan>();
        var offers = plans
            .SelectMany(p => SubscriptionOffers.For(
                context.Subscription, p, context.Settings, now, context.FounderSlotAvailable, context.WelcomeDiscount))
            .ToList();
        var discount = offers.Select(o => o.Quote.DiscountAmount).Where(d => d > 0).DefaultIfEmpty().Max();
        return new SubscriptionOffersResult(true, offers, context.Subscription.IsFounder, context.FounderSlotsLeft,
            discount > 0 ? discount : null);
    }

    public async Task<CheckoutOrderResult> GetOrderAsync(
        int merchantId, SubscriptionPlan plan, SubscriptionPackage package, CancellationToken cancellationToken = default)
    {
        EnsureDefined(plan, package);
        var context = await LoadAsync(merchantId, cancellationToken);
        var isFounder = context.Subscription?.IsFounder ?? false;
        if (NotAllowedMessageFor(context) is { } message)
        {
            return new CheckoutOrderResult(null, message, isFounder, context.FounderSlotsLeft);
        }

        var order = SubscriptionOrder.For(context.Subscription!, plan, package, context.Settings, clock.GetUtcNow(),
            context.FounderSlotAvailable, context.WelcomeDiscount);
        return new CheckoutOrderResult(order, null, isFounder, context.FounderSlotsLeft);
    }

    public async Task<CheckoutResult> StartAsync(StartCheckoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.BaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TermsVersion);
        EnsureDefined(command.Plan, command.Package);

        var context = await LoadAsync(command.MerchantId, cancellationToken);
        if (NotAllowedMessageFor(context) is { } message)
        {
            return new CheckoutResult.NotAllowed(message);
        }

        var subscription = context.Subscription!;
        var now = clock.GetUtcNow();
        var status = subscription.StatusAt(now, context.Settings.GraceDays);
        var (quote, isFounderPrice) = subscription.QuoteFor(
            command.Plan, command.Package, context.Settings, status, context.FounderSlotAvailable, context.WelcomeDiscount);

        // Se vuelve a cotizar: si cambió un precio, se acabaron los lugares de fundador o ya se usó el descuento, el
        // vendedor revisa el total antes de pagar (el mismo patrón que el carrito).
        if (quote.Amount != command.ExpectedTotal)
        {
            logger.LogInformation("Vendedor {MerchantId}: el pedido {Plan} {Package} cambió de {Expected} a {Amount}; no se crea el link",
                command.MerchantId, command.Plan, command.Package, command.ExpectedTotal, quote.Amount);
            return new CheckoutResult.PriceChanged(quote.Amount);
        }

        var reusableSince = SubscriptionCharge.ReusableSince(now, context.Settings.GraceDays);
        var reusable = await db.SubscriptionCharges.IgnoreQueryFilters()
            .Where(c => c.MerchantId == command.MerchantId
                && c.Status == SubscriptionChargeStatus.Pending
                && c.Plan == quote.Plan
                && c.Package == quote.Package
                && c.Amount == quote.Amount
                && c.TermsVersion == command.TermsVersion
                && c.PeyPaymentLinkId != null
                && c.Url != null
                && c.CreatedAt >= reusableSince)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (reusable is not null && reusable.CanBeReusedFor(quote, now, context.Settings.GraceDays, command.TermsVersion))
        {
            logger.LogInformation("Vendedor {MerchantId} reutiliza el link {PaymentLinkId} del cargo {ChargeId} ({Plan}, {Package}, {Amount})",
                command.MerchantId, reusable.PeyPaymentLinkId, reusable.Id, reusable.Plan, reusable.Package, reusable.Amount);
            return new CheckoutResult.Redirect(reusable.Id, new Uri(reusable.Url!), Reused: true);
        }

        // Pey exige URLs de regreso HTTPS: sin ellas no se crea el cargo ni se llama a Pey.
        if (command.BaseUrl.Scheme != Uri.UriSchemeHttps)
        {
            return new CheckoutResult.HttpsRequired();
        }

        var charge = SubscriptionCharge.Create(
            command.MerchantId, quote, isFounderPrice, SubscriptionChargeOrigin.Merchant, now, command.TermsVersion);
        db.SubscriptionCharges.Add(charge);
        await db.SaveChangesAsync(cancellationToken);

        var baseUrl = command.BaseUrl.AbsoluteUri.TrimEnd('/');
        CreatedPaymentLink link;
        try
        {
            // Sin el token de la petición: si el vendedor cierra la página a medio envío, el cargo no queda a medias.
            link = await gateway.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(
                charge.Amount,
                new Uri(baseUrl + SubscriptionPaths.Payment(charge.Id)),
                // «Regresar» de Pey vuelve al resumen del mismo pedido, como el carrito de una tienda.
                new Uri(baseUrl + SubscriptionPaths.Summary(charge.Plan, charge.Package)),
                HasTip: false,
                ClientReferenceId: charge.ClientReferenceId,
                Metadata: MetadataFor(charge)), CancellationToken.None);
        }
        catch (PaymentGatewayValidationException ex)
        {
            logger.LogWarning("Pey rechazó el link del cargo {ChargeId} del vendedor {MerchantId} ({ErrorType}); requestId {RequestId}: {GatewayMessage}",
                charge.Id, command.MerchantId, ex.ErrorType, ex.RequestId, ex.GatewayMessage);
            return await FailAsync(charge, $"Rechazado por Pey ({ex.ErrorType}).", RejectedFailure);
        }
        catch (PaymentGatewayUnavailableException ex)
        {
            // POST sin respuesta: Pey pudo crear el link, pero nadie conoce su URL.
            logger.LogWarning(ex, "Link del cargo {ChargeId} del vendedor {MerchantId} sin confirmar (Pey no disponible); requestId {RequestId}",
                charge.Id, command.MerchantId, ex.RequestId);
            return await FailAsync(charge, "Pey no respondió.", UnavailableFailure);
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogError(ex, "Pey falló al crear el link del cargo {ChargeId} del vendedor {MerchantId} ({ErrorType}); requestId {RequestId}",
                charge.Id, command.MerchantId, ex.ErrorType, ex.RequestId);
            return await FailAsync(charge, $"Error de Pey ({ex.ErrorType}).", GenericFailure);
        }
        catch (InvalidOperationException ex)
        {
            // Configuración (p. ej. falta Pey:ApiKey).
            logger.LogError(ex, "No se pudo llamar a Pey para el link del cargo {ChargeId} del vendedor {MerchantId}", charge.Id, command.MerchantId);
            return await FailAsync(charge, "No se pudo llamar a Pey.", GenericFailure);
        }

        charge.AttachLink(link.Id, link.Url.AbsoluteUri);
        await db.SaveChangesAsync(CancellationToken.None); // el link ya existe en Pey: no se abandona el guardado

        logger.LogInformation("Vendedor {MerchantId} creó el cargo {ChargeId} ({Origin}, {Plan}, {Package}, {Amount}, fundador {IsFounderPrice}, términos {TermsVersion}) con el link {PaymentLinkId}",
            command.MerchantId, charge.Id, charge.Origin, charge.Plan, charge.Package, charge.Amount, charge.IsFounderPrice,
            charge.TermsVersion, link.Id);
        return new CheckoutResult.Redirect(charge.Id, link.Url, Reused: false);
    }

    internal static Dictionary<string, string> MetadataFor(SubscriptionCharge charge) => new()
    {
        [SubscriptionChargeMetadata.KindKey] = SubscriptionChargeMetadata.SubscriptionKind,
        [SubscriptionChargeMetadata.MerchantIdKey] = charge.MerchantId.ToString(CultureInfo.InvariantCulture),
        [SubscriptionChargeMetadata.SubscriptionChargeIdKey] = charge.Id.ToString(CultureInfo.InvariantCulture),
    };

    private static void EnsureDefined(SubscriptionPlan plan, SubscriptionPackage package)
    {
        if (!Enum.IsDefined(plan))
        {
            throw new ArgumentOutOfRangeException(nameof(plan), plan, "Plan desconocido.");
        }

        if (!Enum.IsDefined(package))
        {
            throw new ArgumentOutOfRangeException(nameof(package), package, "Paquete desconocido.");
        }

        // 3 y 6 meses ya no se venden (2026-11-27): ni resumen ni cargo, aunque llegue un POST a mano.
        if (!package.IsOffered())
        {
            throw new ArgumentOutOfRangeException(nameof(package), package, "Paquete que ya no se ofrece.");
        }
    }

    /// <summary>Dado de baja o sin prueba todavía (solo paga quien ya tiene suscripción).</summary>
    private static string? NotAllowedMessageFor(CheckoutContext context) =>
        context.MerchantStatus == MerchantStatus.Disabled ? DisabledMessage
        : context.Subscription is null ? NotStartedMessage
        : null;

    private async Task<CheckoutResult> FailAsync(SubscriptionCharge charge, string reason, string message)
    {
        charge.MarkFailed(reason);
        await db.SaveChangesAsync(CancellationToken.None);
        return new CheckoutResult.GatewayFailed(message, charge.Id);
    }

    private async Task<CheckoutContext> LoadAsync(int merchantId, CancellationToken cancellationToken)
    {
        var merchantStatus = await db.Merchants.AsNoTracking()
            .Where(m => m.Id == merchantId)
            .Select(m => (MerchantStatus?)m.Status)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"No existe el vendedor {merchantId}.");
        var subscription = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Include(s => s.Periods)
            .SingleOrDefaultAsync(s => s.MerchantId == merchantId, cancellationToken);
        var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);
        var assigned = await db.Subscriptions.IgnoreQueryFilters().CountAsync(s => s.FounderNumber != null, cancellationToken);
        var slotsLeft = Math.Max(0, settings.FounderSlots - assigned);
        var isFounder = subscription?.IsFounder ?? false;

        // Descuento de bienvenida (5.8): se registró con un código y todavía no se le abona ningún pago.
        var welcomeDiscount = await db.Referrals.IgnoreQueryFilters()
            .AnyAsync(r => r.ReferredMerchantId == merchantId && r.QualifiedAt == null, cancellationToken);

        return new CheckoutContext(merchantStatus, subscription, settings, FounderSlotAvailable: !isFounder && slotsLeft > 0,
            FounderSlotsLeft: isFounder ? 0 : slotsLeft, welcomeDiscount);
    }

    private sealed record CheckoutContext(
        MerchantStatus MerchantStatus,
        Subscription? Subscription,
        SystemSettings Settings,
        bool FounderSlotAvailable,
        int FounderSlotsLeft,
        bool WelcomeDiscount);
}
