using Local133.Domain.Pey;
using Local133.Domain.Subscriptions;

namespace Local133.Application.Subscriptions;

/// <summary>Llaves de la metadata del link de pago de la suscripción (vuelven en el webhook y en la consulta).</summary>
public static class SubscriptionChargeMetadata
{
    public const string KindKey = "kind";
    public const string MerchantIdKey = "merchantId";
    public const string SubscriptionChargeIdKey = "subscriptionChargeId";

    /// <summary><c>kind</c> de un link de la suscripción.</summary>
    public const string SubscriptionKind = "subscription";
}

/// <summary>
/// Pago de la suscripción con link de Pey («Mi plan», 5.6): ofrece los paquetes, crea (o reutiliza) el cargo y su link.
/// El dinero cae en la cuenta de la plataforma.
/// </summary>
public interface ISubscriptionCheckout
{
    /// <summary>Paquetes con el precio que le toca al vendedor; vacío si todavía no puede pagar.</summary>
    Task<SubscriptionOffersResult> GetOffersAsync(int merchantId, CancellationToken cancellationToken = default);

    Task<CheckoutResult> StartAsync(StartCheckoutCommand command, CancellationToken cancellationToken = default);
}

/// <param name="BaseUrl">Base pública del sitio para las URLs de regreso; Pey exige HTTPS.</param>
/// <param name="Origin">Quién pide el link: el vendedor en «Mi plan» o el job diario (renovación, 5.7).</param>
public sealed record StartCheckoutCommand(
    int MerchantId,
    SubscriptionPackage Package,
    Uri BaseUrl,
    SubscriptionChargeOrigin Origin = SubscriptionChargeOrigin.Merchant);

/// <param name="CanPay">Ya tiene suscripción (la prueba empezó) y no está dado de baja.</param>
/// <param name="FounderSlotsLeft">Lugares de fundador que quedan (0 si ya no hay o si ya es fundador).</param>
public sealed record SubscriptionOffersResult(
    bool CanPay, IReadOnlyList<SubscriptionOffer> Offers, bool IsFounder, int FounderSlotsLeft);

public abstract record CheckoutResult
{
    private CheckoutResult()
    {
    }

    /// <summary>Link listo: el navegador va a la página de Pey.</summary>
    public sealed record Redirect(int ChargeId, Uri Url, bool Reused) : CheckoutResult;

    /// <summary>Todavía no puede pagar (sin prueba) o está dado de baja.</summary>
    public sealed record NotAllowed(string Message) : CheckoutResult;

    /// <summary>La base del sitio no es HTTPS: Pey rechazaría las URLs de regreso. No se llamó a Pey.</summary>
    public sealed record HttpsRequired : CheckoutResult;

    /// <summary>Pey rechazó o no respondió: el cargo quedó <c>Failed</c>.</summary>
    public sealed record GatewayFailed(string Message, int ChargeId) : CheckoutResult;
}

/// <summary>
/// Lo que Pey dice de un link (webhook <c>payment_link.status_updated</c> o <c>GET /payment-link/{id}</c>), ya
/// normalizado.
/// </summary>
/// <param name="Metadata">Metadata que enviamos al crear el link (llaves de <see cref="SubscriptionChargeMetadata"/>).</param>
public sealed record PaymentLinkUpdate(
    string PaymentLinkId,
    PeyPaymentLinkStatus Status,
    string? ClientReferenceId,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<PaymentLinkUpdateTransaction> Transactions);

/// <param name="Base">Monto sin propina ni comisión al pagador; <c>null</c> si Pey no manda desglose.</param>
public sealed record PaymentLinkUpdateTransaction(
    string TransactionId,
    string? PaymentMethod,
    DateTimeOffset? PaidAt,
    decimal Amount,
    decimal? Base,
    decimal? Fee,
    decimal? ApplicationFee);

/// <summary>
/// Registra lo que Pey reporta de un link de la suscripción y abona el periodo cuando el pago lo cubre. Idempotente por
/// <c>transactionId</c> y por estatus del cargo. No usa la sesión (webhook y jobs).
/// </summary>
public interface ISubscriptionPaymentRecorder
{
    Task<SubscriptionPaymentResult> RecordAsync(PaymentLinkUpdate update, CancellationToken cancellationToken = default);
}

public abstract record SubscriptionPaymentResult
{
    private SubscriptionPaymentResult()
    {
    }

    /// <summary>Se abonó el periodo.</summary>
    public sealed record Credited(int ChargeId, int PeriodId, DateTimeOffset ProUntil, int? FounderNumber) : SubscriptionPaymentResult;

    /// <summary>El cargo ya estaba pagado (webhook repetido, conciliación).</summary>
    public sealed record AlreadyCredited(int ChargeId) : SubscriptionPaymentResult;

    /// <summary>Sin abono: pendiente, en proceso o incompleto.</summary>
    public sealed record StatusUpdated(int ChargeId, SubscriptionChargeStatus Status) : SubscriptionPaymentResult;

    /// <summary>No corresponde a ningún cargo (o la metadata lo contradice).</summary>
    public sealed record Unmatched(string Reason) : SubscriptionPaymentResult;
}

/// <summary>Consulta a Pey el estado del link de un cargo (respaldo del webhook y confirmación al volver de Pey).</summary>
public interface ISubscriptionPaymentReconciliation
{
    /// <param name="minInterval">No consulta si la última consulta fue hace menos de esto.</param>
    Task<SubscriptionReconciliationResult> ReconcileChargeAsync(
        int chargeId, TimeSpan? minInterval = null, CancellationToken cancellationToken = default);
}

public abstract record SubscriptionReconciliationResult
{
    private SubscriptionReconciliationResult()
    {
    }

    public sealed record Checked(SubscriptionPaymentResult Result) : SubscriptionReconciliationResult;

    /// <summary>No toca: ya pagado, sin link o consultado hace poco.</summary>
    public sealed record Skipped(string Reason) : SubscriptionReconciliationResult;

    /// <summary>Pey no respondió o falló: no se sella y se reintenta en la siguiente corrida.</summary>
    public sealed record GatewayFailed(string Message) : SubscriptionReconciliationResult;
}

/// <summary>Aviso al vendedor de que se abonó su pago (correo; la 5.4 suma el aviso en el sitio). Nunca lanza.</summary>
public interface ISubscriptionPaymentNotifier
{
    Task PaymentCreditedAsync(SubscriptionPaymentCredited payment, CancellationToken cancellationToken = default);
}

/// <param name="ExtendedExistingPro">Ya tenía Pro pagado vigente: este pago extiende su plan (p. ej. pagó dos links).</param>
public sealed record SubscriptionPaymentCredited(
    int MerchantId,
    int ChargeId,
    SubscriptionPackage Package,
    decimal Amount,
    DateTimeOffset ProUntil,
    bool ExtendedExistingPro,
    int? FounderNumber);
