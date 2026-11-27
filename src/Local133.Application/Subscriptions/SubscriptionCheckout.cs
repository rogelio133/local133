using System.Globalization;
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

/// <summary>Rutas del checkout que comparten el sitio, el link de Pey y los correos (5.15).</summary>
public static class SubscriptionPaths
{
    public const string Plan = "/app/plan";

    /// <summary>Paso 1: elegir plan y duración (Básico y Pro, 1 mes y Anual), en una sola página.</summary>
    public const string Checkout = "/app/plan/checkout";

    /// <summary>Paso 1 con el scroll en la sección de un plan (<c>/app/plan/checkout#pro</c>).</summary>
    public static string ChoosePlan(SubscriptionPlan plan) => $"{Checkout}#{plan.Slug()}";

    /// <summary>Paso 2: resumen del pedido (también el regreso de Pey si cancela).</summary>
    public static string Summary(SubscriptionPlan plan, SubscriptionPackage package) =>
        $"{Checkout}/{plan.Slug()}/{package.Slug()}";

    /// <summary>
    /// «Elegir plan» o «Renovar» (vencido, gracia, Inicio, banner y correos): el resumen de su último plan y paquete; si
    /// ese paquete ya no se ofrece (3 o 6 meses), la sección de su plan; si nunca pagó, el paso 1 (§6 del diseño).
    /// </summary>
    public static string ChoosePlanFor(SubscriptionPlan? lastPlan, SubscriptionPackage? lastPackage) =>
        (lastPlan, lastPackage) switch
        {
            ({ } plan, { } package) when package.IsOffered() => Summary(plan, package),
            ({ } plan, _) => ChoosePlan(plan),
            _ => Checkout,
        };

    /// <summary>Paso 4: confirmación (regreso de Pey tras pagar).</summary>
    public static string Payment(int chargeId) => $"/app/plan/pago/{chargeId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Comprobante de un pedido pagado.</summary>
    public static string Receipt(int chargeId) =>
        $"/app/plan/pedidos/{chargeId.ToString(CultureInfo.InvariantCulture)}/comprobante";

    /// <summary>Número de pedido en pantalla y en correos: «#123».</summary>
    public static string OrderNumber(int chargeId) => "#" + chargeId.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Pago de la suscripción con link de Pey (checkout por pasos, 5.15): ofrece los paquetes de cada plan, arma el
/// resumen del pedido y crea (o reutiliza) el cargo y su link solo al pagar. El dinero cae en la cuenta de la plataforma.
/// </summary>
public interface ISubscriptionCheckout
{
    /// <summary>
    /// Paquetes con el precio que le toca al vendedor, de <paramref name="plan"/> o de los dos planes; vacío si todavía
    /// no puede pagar.
    /// </summary>
    Task<SubscriptionOffersResult> GetOffersAsync(
        int merchantId, SubscriptionPlan? plan = null, CancellationToken cancellationToken = default);

    /// <summary>Resumen del pedido (paso 2). No crea nada ni llama a Pey.</summary>
    Task<CheckoutOrderResult> GetOrderAsync(
        int merchantId, SubscriptionPlan plan, SubscriptionPackage package, CancellationToken cancellationToken = default);

    Task<CheckoutResult> StartAsync(StartCheckoutCommand command, CancellationToken cancellationToken = default);
}

/// <param name="BaseUrl">Base pública del sitio para las URLs de regreso; Pey exige HTTPS.</param>
/// <param name="TermsVersion">Versión de los Términos que aceptó en el resumen (<c>LegalDocuments</c>).</param>
/// <param name="ExpectedTotal">Total que vio el vendedor en el resumen: si cambió, no se paga.</param>
public sealed record StartCheckoutCommand(
    int MerchantId,
    SubscriptionPlan Plan,
    SubscriptionPackage Package,
    Uri BaseUrl,
    string TermsVersion,
    decimal ExpectedTotal);

/// <param name="Order">El pedido; <c>null</c> si no puede pagar.</param>
/// <param name="NotAllowedMessage">Por qué no puede pagar (onboarding o dado de baja).</param>
public sealed record CheckoutOrderResult(
    SubscriptionOrder? Order, string? NotAllowedMessage, bool IsFounder, int FounderSlotsLeft)
{
    public bool CanPay => Order is not null;
}

/// <param name="CanPay">Ya tiene suscripción (la prueba empezó) y no está dado de baja.</param>
/// <param name="FounderSlotsLeft">Lugares de fundador que quedan (0 si ya no hay o si ya es fundador).</param>
/// <param name="WelcomeDiscount">
/// Descuento de bienvenida del referido en su primer pago (5.8), ya incluido en los precios; <c>null</c> si no tiene.
/// </param>
public sealed record SubscriptionOffersResult(
    bool CanPay, IReadOnlyList<SubscriptionOffer> Offers, bool IsFounder, int FounderSlotsLeft, decimal? WelcomeDiscount = null);

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

    /// <summary>El total ya no es el que vio en el resumen (cambió un precio, el fundador o el descuento). No se llamó a Pey.</summary>
    public sealed record PriceChanged(decimal Total) : CheckoutResult;
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

/// <param name="PeriodStartsAt">Inicio del periodo pagado.</param>
/// <param name="PeriodEndsAt">Fin (excluido) del periodo pagado.</param>
/// <param name="ProUntil">Fin (excluido) de toda su cadena, de cualquier plan.</param>
/// <param name="StartsLater">El periodo empieza después del pago: se encadenó a lo que ya tenía.</param>
/// <param name="Conversion">Los días de Básico que se volvieron Pro al subir de plan; <c>null</c> si no hubo.</param>
public sealed record SubscriptionPaymentCredited(
    int MerchantId,
    int ChargeId,
    SubscriptionPlan Plan,
    SubscriptionPackage Package,
    decimal Amount,
    DateTimeOffset PeriodStartsAt,
    DateTimeOffset PeriodEndsAt,
    DateTimeOffset ProUntil,
    bool StartsLater,
    BasicToProConversion? Conversion,
    int? FounderNumber);
