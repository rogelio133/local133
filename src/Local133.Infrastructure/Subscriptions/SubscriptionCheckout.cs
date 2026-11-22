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
/// «Mi plan» (5.6): cotiza los paquetes y crea el cargo con su link de pago de Pey (§4 del diseño). El cargo se inserta
/// antes de llamar a Pey (su id va en <c>clientReferenceId</c> y en la metadata). Un link <c>pending</c> del mismo
/// paquete y monto creado en los últimos <c>GraceDays</c> días se reutiliza; uno más viejo se abandona (5.7). El link
/// solo nace cuando el vendedor elige un paquete: el job diario no crea links. Si Pey no responde, el cargo queda <c>Failed</c>: nadie conoce la URL del link, así
/// que nadie lo puede pagar, y al reintentar se crea otro.
/// </summary>
internal sealed class SubscriptionCheckout(
    AppDbContext db,
    IPaymentGateway gateway,
    TimeProvider clock,
    ILogger<SubscriptionCheckout> logger) : ISubscriptionCheckout
{
    internal const string NotStartedMessage =
        "Podrás pagar Pro cuando tu cuenta bancaria esté activa. Primero tendrás tu prueba gratis.";

    internal const string DisabledMessage = "Tu cuenta está dada de baja. Escríbenos si quieres reactivarla.";

    internal const string RejectedFailure =
        "Pey no aceptó generar el link de pago. Intenta de nuevo; si sigue pasando, escríbenos.";

    internal const string UnavailableFailure =
        "Pey no respondió a tiempo y no se generó el link de pago. Intenta de nuevo en unos minutos.";

    internal const string GenericFailure = "No pudimos generar el link de pago. Intenta de nuevo en unos minutos.";

    /// <summary>Ruta de la confirmación (regreso de Pey tras pagar).</summary>
    public static string PaymentPath(int chargeId) => $"/app/plan/pago/{chargeId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Ruta de «Mi plan» (regreso de Pey si cancela).</summary>
    public const string PlanPath = "/app/plan";

    public async Task<SubscriptionOffersResult> GetOffersAsync(int merchantId, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(merchantId, cancellationToken);
        if (context.Subscription is null || context.MerchantStatus == MerchantStatus.Disabled)
        {
            return new SubscriptionOffersResult(false, [], context.Subscription?.IsFounder ?? false, context.FounderSlotsLeft);
        }

        var offers = SubscriptionOffers.For(context.Subscription, context.Settings, clock.GetUtcNow(), context.FounderSlotAvailable);
        return new SubscriptionOffersResult(true, offers, context.Subscription.IsFounder, context.FounderSlotsLeft);
    }

    public async Task<CheckoutResult> StartAsync(StartCheckoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.BaseUrl);
        if (!Enum.IsDefined(command.Package))
        {
            throw new ArgumentOutOfRangeException(nameof(command), command.Package, "Paquete desconocido.");
        }

        var context = await LoadAsync(command.MerchantId, cancellationToken);
        if (context.MerchantStatus == MerchantStatus.Disabled)
        {
            return new CheckoutResult.NotAllowed(DisabledMessage);
        }

        if (context.Subscription is not { } subscription)
        {
            return new CheckoutResult.NotAllowed(NotStartedMessage);
        }

        var now = clock.GetUtcNow();
        var status = subscription.StatusAt(now, context.Settings.GraceDays);
        var (quote, isFounderPrice) = subscription.QuoteFor(command.Package, context.Settings, status, context.FounderSlotAvailable);

        var reusableSince = SubscriptionCharge.ReusableSince(now, context.Settings.GraceDays);
        var reusable = await db.SubscriptionCharges.IgnoreQueryFilters()
            .Where(c => c.MerchantId == command.MerchantId
                && c.Status == SubscriptionChargeStatus.Pending
                && c.Package == quote.Package
                && c.Amount == quote.Amount
                && c.PeyPaymentLinkId != null
                && c.Url != null
                && c.CreatedAt >= reusableSince)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (reusable is not null && reusable.CanBeReusedFor(quote, now, context.Settings.GraceDays))
        {
            logger.LogInformation("Vendedor {MerchantId} reutiliza el link {PaymentLinkId} del cargo {ChargeId} ({Package}, {Amount})",
                command.MerchantId, reusable.PeyPaymentLinkId, reusable.Id, reusable.Package, reusable.Amount);
            return new CheckoutResult.Redirect(reusable.Id, new Uri(reusable.Url!), Reused: true);
        }

        // Pey exige URLs de regreso HTTPS: sin ellas no se crea el cargo ni se llama a Pey.
        if (command.BaseUrl.Scheme != Uri.UriSchemeHttps)
        {
            return new CheckoutResult.HttpsRequired();
        }

        var charge = SubscriptionCharge.Create(command.MerchantId, quote, isFounderPrice, SubscriptionChargeOrigin.Merchant, now);
        db.SubscriptionCharges.Add(charge);
        await db.SaveChangesAsync(cancellationToken);

        var baseUrl = command.BaseUrl.AbsoluteUri.TrimEnd('/');
        CreatedPaymentLink link;
        try
        {
            // Sin el token de la petición: si el vendedor cierra la página a medio envío, el cargo no queda a medias.
            link = await gateway.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(
                charge.Amount,
                new Uri(baseUrl + PaymentPath(charge.Id)),
                new Uri(baseUrl + PlanPath),
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

        logger.LogInformation("Vendedor {MerchantId} creó el cargo {ChargeId} ({Origin}, {Package}, {Amount}, fundador {IsFounderPrice}) con el link {PaymentLinkId}",
            command.MerchantId, charge.Id, charge.Origin, charge.Package, charge.Amount, charge.IsFounderPrice, link.Id);
        return new CheckoutResult.Redirect(charge.Id, link.Url, Reused: false);
    }

    internal static Dictionary<string, string> MetadataFor(SubscriptionCharge charge) => new()
    {
        [SubscriptionChargeMetadata.KindKey] = SubscriptionChargeMetadata.SubscriptionKind,
        [SubscriptionChargeMetadata.MerchantIdKey] = charge.MerchantId.ToString(CultureInfo.InvariantCulture),
        [SubscriptionChargeMetadata.SubscriptionChargeIdKey] = charge.Id.ToString(CultureInfo.InvariantCulture),
    };

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
        return new CheckoutContext(merchantStatus, subscription, settings, FounderSlotAvailable: !isFounder && slotsLeft > 0,
            FounderSlotsLeft: isFounder ? 0 : slotsLeft);
    }

    private sealed record CheckoutContext(
        MerchantStatus MerchantStatus,
        Subscription? Subscription,
        SystemSettings Settings,
        bool FounderSlotAvailable,
        int FounderSlotsLeft);
}
