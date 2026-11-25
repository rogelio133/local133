using System.Globalization;
using Local133.Domain.Common;

namespace Local133.Domain.Subscriptions;

/// <summary>Quién originó un cargo de la suscripción.</summary>
public enum SubscriptionChargeOrigin
{
    /// <summary>El vendedor eligió un paquete en «Mi plan».</summary>
    Merchant,

    /// <summary>Reservado: el job diario ya no crea links (5.7, el link se genera cuando el vendedor elige paquete).</summary>
    Renewal,
}

/// <summary>
/// Cargo de la suscripción: un paquete con su precio congelado y <b>un</b> link de pago de Pey (§4 del diseño). El
/// monto se guarda al crearlo; cambiar un precio en <c>SystemSettings</c> no lo altera. Se abona con el webhook
/// <c>payment_link.status_updated</c> o con una consulta a Pey; el regreso del navegador nunca abona.
/// </summary>
public class SubscriptionCharge : Entity
{
    /// <summary>Prefijo del <c>clientReferenceId</c> del link: <c>sub-{Id}</c>.</summary>
    public const string ClientReferencePrefix = "sub-";

    public const int FailureReasonMaxLength = 500;

    public int MerchantId { get; set; }

    public SubscriptionPackage Package { get; set; }

    public int Months { get; set; }

    /// <summary>Precio antes del descuento, con IVA incluido.</summary>
    public decimal ListPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public SubscriptionDiscountReason? DiscountReason { get; set; }

    /// <summary>Lo que se cobra en el link (con IVA incluido).</summary>
    public decimal Amount { get; set; }

    /// <summary>Se cotizó con precio de fundador (el número se asigna al abonarse, si quedan lugares).</summary>
    public bool IsFounderPrice { get; set; }

    public SubscriptionChargeOrigin Origin { get; set; }

    public SubscriptionChargeStatus Status { get; set; }

    /// <summary>Id (ULID) del link en Pey; <c>null</c> mientras no se crea o si la creación falló.</summary>
    public string? PeyPaymentLinkId { get; set; }

    /// <summary>Página de pago de Pey.</summary>
    public string? Url { get; set; }

    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>El admin lo abonó a mano (6.2), sin que Pey lo confirmara; motivo y autor en <c>AuditLogs</c>.</summary>
    public DateTimeOffset? CreditedManuallyAt { get; set; }

    /// <summary>Última consulta a Pey del estado del link (conciliación).</summary>
    public DateTimeOffset? PaymentCheckedAt { get; set; }

    public List<SubscriptionPayment> Payments { get; set; } = [];

    public string ClientReferenceId => ClientReferenceFor(Id);

    /// <summary>
    /// Se puede reutilizar el link: pendiente, con link, del mismo paquete y monto, y creado hace a lo más
    /// <paramref name="maxAgeDays"/> días (los días de gracia, 5.7). Uno más viejo se abandona y se crea otro.
    /// </summary>
    public bool CanBeReusedFor(SubscriptionQuote quote, DateTimeOffset now, int maxAgeDays) =>
        Status == SubscriptionChargeStatus.Pending && PeyPaymentLinkId is not null && Url is not null
        && Package == quote.Package && Amount == quote.Amount
        && CreatedAt >= ReusableSince(now, maxAgeDays);

    /// <summary>Fecha de creación más vieja que todavía se reutiliza.</summary>
    public static DateTimeOffset ReusableSince(DateTimeOffset now, int maxAgeDays) => now.AddDays(-Math.Max(0, maxAgeDays));

    public static SubscriptionCharge Create(
        int merchantId, SubscriptionQuote quote, bool isFounderPrice, SubscriptionChargeOrigin origin, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quote.Amount);
        return new SubscriptionCharge
        {
            MerchantId = merchantId,
            Package = quote.Package,
            Months = quote.Months,
            ListPrice = quote.ListPrice,
            DiscountAmount = quote.DiscountAmount,
            DiscountReason = quote.DiscountReason,
            Amount = quote.Amount,
            IsFounderPrice = isFounderPrice,
            Origin = origin,
            Status = SubscriptionChargeStatus.Pending,
            CreatedAt = now,
        };
    }

    public static string ClientReferenceFor(int chargeId) =>
        ClientReferencePrefix + chargeId.ToString(CultureInfo.InvariantCulture);

    /// <summary><c>sub-42</c> ⇒ 42.</summary>
    public static bool TryParseClientReference(string? value, out int chargeId)
    {
        chargeId = 0;
        var text = value?.Trim();
        return text is not null
            && text.StartsWith(ClientReferencePrefix, StringComparison.Ordinal)
            && int.TryParse(text.AsSpan(ClientReferencePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out chargeId)
            && chargeId > 0;
    }

    public void AttachLink(string peyPaymentLinkId, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peyPaymentLinkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        PeyPaymentLinkId = peyPaymentLinkId;
        Url = url;
    }

    /// <summary>
    /// Pey reportó un pago del link de este cargo, que no tenía el id guardado (se encontró por <c>clientReferenceId</c>
    /// o por la metadata). La URL no se conoce.
    /// </summary>
    public void AdoptPaymentLinkId(string peyPaymentLinkId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peyPaymentLinkId);
        if (PeyPaymentLinkId is not null && PeyPaymentLinkId != peyPaymentLinkId)
        {
            throw new InvalidOperationException("El cargo ya tiene otro link.");
        }

        PeyPaymentLinkId = peyPaymentLinkId;
    }

    /// <summary>La creación del link falló o quedó sin respuesta: nadie conoce su URL, así que nadie lo puede pagar.</summary>
    public void MarkFailed(string reason)
    {
        if (Status != SubscriptionChargeStatus.Pending || PeyPaymentLinkId is not null)
        {
            throw new InvalidOperationException($"Solo un cargo pendiente sin link puede fallar (está {Status}).");
        }

        Status = SubscriptionChargeStatus.Failed;
        FailureReason = Truncate(reason);
    }

    /// <summary>Pey está procesando el pago. No retrocede un cargo pagado o incompleto.</summary>
    public bool MarkProcessing()
    {
        if (Status is not (SubscriptionChargeStatus.Pending or SubscriptionChargeStatus.Failed))
        {
            return false;
        }

        Status = SubscriptionChargeStatus.Processing;
        return true;
    }

    /// <summary>Pago incompleto (<c>partial</c> o <c>paid</c> por menos): no se abona, queda para revisión del admin.</summary>
    public bool MarkPartial()
    {
        if (Status == SubscriptionChargeStatus.Paid)
        {
            return false;
        }

        var changed = Status != SubscriptionChargeStatus.Partial;
        Status = SubscriptionChargeStatus.Partial;
        return changed;
    }

    /// <summary>El pago manda: también desde <see cref="SubscriptionChargeStatus.Failed"/> o <c>Partial</c>.</summary>
    /// <param name="creditedManuallyAt">Cuándo lo abonó el admin a mano (6.2); <c>null</c> si lo confirmó Pey.</param>
    public void MarkPaid(DateTimeOffset paidAt, DateTimeOffset? creditedManuallyAt = null)
    {
        if (Status == SubscriptionChargeStatus.Paid)
        {
            throw new InvalidOperationException("El cargo ya está pagado.");
        }

        Status = SubscriptionChargeStatus.Paid;
        PaidAt = paidAt;
        CreditedManuallyAt = creditedManuallyAt;
        FailureReason = null;
    }

    /// <summary>La suma de lo pagado (sin propina ni comisión) cubre el monto del cargo.</summary>
    public bool IsCoveredBy(decimal paidBase) => paidBase >= Amount;

    private static string Truncate(string reason) =>
        reason.Length <= FailureReasonMaxLength ? reason : reason[..FailureReasonMaxLength];
}

/// <summary>Una transacción que Pey reportó en el link de un cargo (única por <see cref="PeyTransactionId"/>).</summary>
public class SubscriptionPayment : Entity
{
    public const int PaymentMethodMaxLength = 20;

    public int MerchantId { get; set; }

    public int SubscriptionChargeId { get; set; }

    public string PeyTransactionId { get; set; } = string.Empty;

    /// <summary><c>CODI</c> o <c>CARD</c>, como lo manda Pey.</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>Total pagado (base + propina + comisión al pagador).</summary>
    public decimal Amount { get; set; }

    /// <summary>Lo que cuenta para cubrir el cargo; si Pey no manda desglose, el total.</summary>
    public decimal Base { get; set; }

    public decimal? Fee { get; set; }

    public decimal? ApplicationFee { get; set; }

    public DateTimeOffset PaidAt { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public static SubscriptionPayment Record(
        SubscriptionCharge charge, string peyTransactionId, string? paymentMethod, decimal amount, decimal? @base,
        decimal? fee, decimal? applicationFee, DateTimeOffset paidAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(charge);
        ArgumentException.ThrowIfNullOrWhiteSpace(peyTransactionId);
        var method = paymentMethod?.Trim().ToUpperInvariant();
        return new SubscriptionPayment
        {
            MerchantId = charge.MerchantId,
            SubscriptionChargeId = charge.Id,
            PeyTransactionId = peyTransactionId.Trim(),
            PaymentMethod = method is { Length: > PaymentMethodMaxLength } ? method[..PaymentMethodMaxLength] : method,
            Amount = amount,
            Base = @base ?? amount,
            Fee = fee,
            ApplicationFee = applicationFee,
            PaidAt = paidAt,
            RecordedAt = now,
        };
    }
}
