using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Local133.Domain.Common;
using Local133.Domain.Pey;

namespace Local133.Domain.Sales;

/// <summary>
/// Venta del vendedor. Se cobra con un QR CoDi (u otro método); los pagos que recibe se registran aparte.
/// </summary>
public partial class Sale : Entity
{
    /// <summary>Monto mínimo de una venta con monto (1 centavo).</summary>
    public const decimal MinAmount = 0.01m;

    /// <summary>Monto máximo que acepta el teclado de cobro (6 enteros y 2 decimales).</summary>
    public const decimal MaxAmount = 999_999.99m;

    /// <summary>Concepto que escribe el vendedor al cobrar.</summary>
    public const int ConceptTextMaxLength = 100;

    /// <summary>
    /// Caracteres permitidos en el concepto que escribe el vendedor: letras (con acentos, ü y ñ), números, espacios y
    /// <c>. , - # / ( )</c>. El mismo patrón se valida en servidor (.NET) y en cliente (JS).
    /// </summary>
    public const string ConceptTextPattern = @"^[A-Za-z0-9ÁÉÍÓÚÜÑáéíóúüñ .,\-#/()]+$";

    /// <summary>Separador entre el nombre del negocio y el concepto.</summary>
    public const string ConceptSeparator = " · ";

    /// <summary>
    /// Concepto completo enviado a Pey y guardado: negocio + separador + concepto del vendedor. Pey no documenta un
    /// límite para <c>concept</c>.
    /// </summary>
    public const int ConceptMaxLength = 250;

    public const int PeyIdMaxLength = 64;

    /// <summary>Folio CoDi de Banxico de una notificación push (20 dígitos).</summary>
    public const int PeyCodiIdMaxLength = 20;

    /// <summary>Largo del token del recibo público: 16 bytes aleatorios en hexadecimal (minúsculas).</summary>
    public const int ReceiptTokenLength = 32;

    public int MerchantId { get; set; }

    /// <summary>Folio consecutivo por vendedor (1, 2, 3…); se envía a Pey como <c>numRef</c>.</summary>
    public int Folio { get; set; }

    public ChargeMethod Method { get; set; }

    public SaleSource Source { get; set; }

    public SaleStatus Status { get; set; } = SaleStatus.Pending;

    /// <summary>Monto en MXN con hasta 2 decimales.</summary>
    public decimal Amount { get; set; }

    /// <summary>Concepto enviado a Pey.</summary>
    public string? Concept { get; set; }

    /// <summary>IDC del QR CoDi (Banxico); con él llega el webhook y se concilian sus pagos.</summary>
    public string? PeyQrId { get; set; }

    /// <summary>Contenido del QR (se dibuja en el navegador).</summary>
    public string? QrString { get; set; }

    /// <summary>
    /// Transacción de Pey de un cobro por celular (la devuelve <c>POST /codi/pn</c>); con ella llega el webhook
    /// <c>codi.pn.payment_received</c>. <c>null</c> en ventas con QR, o si el envío quedó sin confirmar.
    /// </summary>
    public string? PeyTransactionId { get; set; }

    /// <summary>Folio CoDi de Banxico de la notificación push (soporte).</summary>
    public string? PeyCodiId { get; set; }

    /// <summary>Celular (10 dígitos) al que se envió el cobro por notificación push.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Vencimiento del QR (o de la notificación push); <c>null</c> = no expira.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Hora del primer pago recibido (la del pago en Pey).</summary>
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>Cuándo la canceló el vendedor.</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>
    /// Última consulta a Pey de la conciliación (respaldo del webhook); <c>null</c> si nunca se consultó. Ver
    /// <see cref="PaymentReconciliationSchedule"/>.
    /// </summary>
    public DateTimeOffset? PaymentCheckedAt { get; set; }

    /// <summary>
    /// Token secreto de la liga pública del recibo (<c>/recibo/{token}</c>). Se genera al crear la venta; el recibo solo
    /// se muestra si la venta está pagada (<see cref="HasReceipt"/>).
    /// </summary>
    public string ReceiptToken { get; set; } = null!;

    /// <summary>Productos de una venta con carrito (<see cref="SaleSource.Cart"/>); vacío en las demás.</summary>
    public List<SaleItem> Items { get; private set; } = [];

    /// <summary>Referencia numérica para Pey (hasta 7 dígitos).</summary>
    public int NumRef => Folio % (PeyLimits.MaxNumRef + 1);

    /// <summary>
    /// Venta cobrada con un QR dinámico: por monto capturado o, con <paramref name="items"/>, con carrito (el monto
    /// debe ser la suma de los productos). El QR se adjunta con <see cref="AttachQr"/>.
    /// </summary>
    public static Sale StartDirect(
        int merchantId, int folio, decimal amount, string concept, DateTimeOffset? expiresAt, DateTimeOffset now,
        IReadOnlyList<SaleItem>? items = null) =>
        StartWithAmount(ChargeMethod.DynamicQr, merchantId, folio, amount, concept, expiresAt, now, items);

    /// <summary>
    /// Venta cobrada con una notificación push al celular del pagador (ya normalizado, ver
    /// <see cref="Sales.PhoneNumber.Normalize"/>), por monto capturado o con carrito (<paramref name="items"/>). La
    /// transacción se adjunta con <see cref="AttachPushNotification"/>.
    /// </summary>
    public static Sale StartPush(
        int merchantId, int folio, decimal amount, string concept, string phoneNumber, DateTimeOffset expiresAt,
        DateTimeOffset now, IReadOnlyList<SaleItem>? items = null)
    {
        if (!Sales.PhoneNumber.IsValid(phoneNumber))
        {
            throw new ArgumentException($"El celular debe tener {Sales.PhoneNumber.Length} dígitos.", nameof(phoneNumber));
        }

        if (amount > PeyLimits.MaxPushAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount,
                $"El cobro por celular admite hasta {PeyLimits.MaxPushAmount:N0}.");
        }

        var sale = StartWithAmount(ChargeMethod.PushNotification, merchantId, folio, amount, concept, expiresAt, now, items);
        sale.PhoneNumber = phoneNumber;
        return sale;
    }

    private static Sale StartWithAmount(
        ChargeMethod method, int merchantId, int folio, decimal amount, string concept, DateTimeOffset? expiresAt,
        DateTimeOffset now, IReadOnlyList<SaleItem>? items)
    {
        if (!IsValidAmount(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount,
                $"El monto debe estar entre {MinAmount} y {MaxAmount} con hasta {PeyLimits.AmountDecimals} decimales.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(folio);
        ArgumentException.ThrowIfNullOrWhiteSpace(concept);
        concept = concept.Trim();
        if (concept.Length > ConceptMaxLength)
        {
            throw new ArgumentException($"El concepto admite hasta {ConceptMaxLength} caracteres.", nameof(concept));
        }

        var isCart = items is { Count: > 0 };
        if (isCart)
        {
            EnsureValidCart(merchantId, amount, items!);
        }

        return new Sale
        {
            MerchantId = merchantId,
            Folio = folio,
            Method = method,
            Source = isCart ? SaleSource.Cart : SaleSource.DirectAmount,
            Status = SaleStatus.Pending,
            Amount = amount,
            Concept = concept,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            ReceiptToken = NewReceiptToken(),
            Items = isCart ? [.. items!] : [],
        };
    }

    private static void EnsureValidCart(int merchantId, decimal amount, IReadOnlyList<SaleItem> items)
    {
        if (items.Count > SaleCart.MaxLines)
        {
            throw new ArgumentException($"Una venta admite hasta {SaleCart.MaxLines} productos distintos.", nameof(items));
        }

        if (items.Any(i => i.MerchantId != merchantId))
        {
            throw new ArgumentException("Todos los productos deben ser del vendedor de la venta.", nameof(items));
        }

        if (SaleCart.Total(items) != amount)
        {
            throw new ArgumentException("El monto de la venta debe ser la suma de sus productos.", nameof(amount));
        }
    }

    /// <summary>Monto aceptable para una venta con monto: entre <see cref="MinAmount"/> y <see cref="MaxAmount"/>, en centavos.</summary>
    public static bool IsValidAmount(decimal amount) =>
        amount is >= MinAmount and <= MaxAmount && decimal.Round(amount, PeyLimits.AmountDecimals) == amount;

    /// <summary>
    /// Concepto que escribe el vendedor: no vacío, hasta <see cref="ConceptTextMaxLength"/> caracteres (sin contar
    /// espacios al inicio y al final) y solo con los caracteres de <see cref="ConceptTextPattern"/>.
    /// </summary>
    public static bool IsValidConceptText(string? conceptText)
    {
        if (string.IsNullOrWhiteSpace(conceptText))
        {
            return false;
        }

        var trimmed = conceptText.Trim();
        return trimmed.Length <= ConceptTextMaxLength && ConceptTextRegex().IsMatch(trimmed);
    }

    /// <summary>Lo que ve el pagador en su app: "Tacos Don Ramón · 2 tacos al pastor", o solo el concepto si el negocio no tiene nombre.</summary>
    public static string BuildConcept(string? businessName, string conceptText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conceptText);
        var text = conceptText.Trim();
        return string.IsNullOrWhiteSpace(businessName) ? text : $"{businessName.Trim()}{ConceptSeparator}{text}";
    }

    /// <summary>
    /// Lo que escribió el vendedor, sin el nombre del negocio que <see cref="BuildConcept"/> antepone (si el concepto
    /// empieza con ese negocio y el separador). Si el negocio cambió de nombre, se devuelve el concepto completo.
    /// </summary>
    public static string? ConceptTextOf(string? concept, string? businessName)
    {
        if (string.IsNullOrWhiteSpace(concept) || string.IsNullOrWhiteSpace(businessName))
        {
            return concept;
        }

        var prefix = businessName.Trim() + ConceptSeparator;
        return concept.StartsWith(prefix, StringComparison.Ordinal) && concept.Length > prefix.Length
            ? concept[prefix.Length..]
            : concept;
    }

    [GeneratedRegex(ConceptTextPattern)]
    private static partial Regex ConceptTextRegex();

    /// <summary>Token nuevo para el recibo público (aleatorio criptográfico, no adivinable).</summary>
    public static string NewReceiptToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(ReceiptTokenLength / 2));

    /// <summary>Tiene la forma de un token de recibo (32 caracteres hexadecimales en minúsculas).</summary>
    public static bool IsValidReceiptToken(string? token) =>
        token is { Length: ReceiptTokenLength } && token.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>Hay recibo para compartir: solo de ventas pagadas.</summary>
    public bool HasReceipt => Status == SaleStatus.Paid;

    /// <summary>QR creado en Pey para esta venta.</summary>
    public void AttachQr(string peyQrId, string qrString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peyQrId);
        ArgumentException.ThrowIfNullOrWhiteSpace(qrString);
        if (PeyQrId is not null)
        {
            throw new InvalidOperationException($"La venta {Id} ya tiene un QR.");
        }

        PeyQrId = peyQrId;
        QrString = qrString;
    }

    /// <summary>Notificación push enviada por Pey para esta venta.</summary>
    public void AttachPushNotification(string peyTransactionId, string? peyCodiId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peyTransactionId);
        if (Method != ChargeMethod.PushNotification)
        {
            throw new InvalidOperationException($"La venta {Id} no es un cobro por celular.");
        }

        if (PeyTransactionId is not null)
        {
            throw new InvalidOperationException($"La venta {Id} ya tiene una notificación push.");
        }

        PeyTransactionId = peyTransactionId.Trim();
        PeyCodiId = string.IsNullOrWhiteSpace(peyCodiId) ? null : peyCodiId.Trim();
    }

    /// <summary>Ya tiene con qué pagarse: el QR (ventas con QR) o la notificación push enviada (cobro por celular).</summary>
    public bool HasPaymentRequest => Method == ChargeMethod.PushNotification ? PeyTransactionId is not null : QrString is not null;

    /// <summary>
    /// El QR ya venció (por tiempo). No cambia <see cref="Status"/>: la transición a <see cref="SaleStatus.Expired"/>
    /// la hace el job de expiración.
    /// </summary>
    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAt is { } expiresAt && now >= expiresAt;

    /// <summary>
    /// Llegó un pago. El dinero ya está en la cuenta del vendedor, así que el pago manda: una venta vencida o
    /// cancelada también queda pagada (CoDi no permite anular un QR y el webhook puede llegar tarde). Si ya estaba
    /// pagada no cambia nada (pago adicional). Devuelve <c>true</c> si cambió el estatus.
    /// </summary>
    public bool MarkPaid(DateTimeOffset paidAt)
    {
        if (Status == SaleStatus.Paid)
        {
            return false;
        }

        Status = SaleStatus.Paid;
        PaidAt = paidAt;
        return true;
    }

    /// <summary>El vendedor puede cancelarla: sigue esperando el pago.</summary>
    public bool CanCancel => Status == SaleStatus.Pending;

    /// <summary>
    /// Cancelación del vendedor (solo local: Pey no anula el QR; si aun así llega un pago, <see cref="MarkPaid"/> gana).
    /// </summary>
    public void Cancel(DateTimeOffset now)
    {
        if (!CanCancel)
        {
            throw new InvalidOperationException($"La venta {Id} está {Status}; solo se cancela una venta pendiente.");
        }

        Status = SaleStatus.Cancelled;
        CancelledAt = now;
    }

    /// <summary>Pendiente con el QR vencido ⇒ <see cref="SaleStatus.Expired"/>. Devuelve <c>true</c> si cambió.</summary>
    public bool ExpireIfDue(DateTimeOffset now)
    {
        if (Status != SaleStatus.Pending || !IsExpiredAt(now))
        {
            return false;
        }

        Status = SaleStatus.Expired;
        return true;
    }

    /// <summary>Estatus que ve el vendedor: una pendiente con el QR ya vencido se muestra como vencida aunque el job aún no corra.</summary>
    public SaleStatus EffectiveStatus(DateTimeOffset now) =>
        Status == SaleStatus.Pending && IsExpiredAt(now) ? SaleStatus.Expired : Status;

    /// <summary>Segundos enteros que le quedan al QR (0 si ya venció); <c>null</c> si no expira.</summary>
    public int? RemainingSeconds(DateTimeOffset now)
    {
        if (ExpiresAt is not { } expiresAt)
        {
            return null;
        }

        var remaining = expiresAt - now;
        return remaining <= TimeSpan.Zero ? 0 : (int)Math.Floor(remaining.TotalSeconds);
    }
}
