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

    /// <summary>Vencimiento del QR; <c>null</c> = no expira.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Referencia numérica para Pey (hasta 7 dígitos).</summary>
    public int NumRef => Folio % (PeyLimits.MaxNumRef + 1);

    /// <summary>Venta por monto capturado, cobrada con un QR dinámico. El QR se adjunta con <see cref="AttachQr"/>.</summary>
    public static Sale StartDirect(
        int merchantId, int folio, decimal amount, string concept, DateTimeOffset? expiresAt, DateTimeOffset now)
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

        return new Sale
        {
            MerchantId = merchantId,
            Folio = folio,
            Method = ChargeMethod.DynamicQr,
            Source = SaleSource.DirectAmount,
            Status = SaleStatus.Pending,
            Amount = amount,
            Concept = concept,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };
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

    [GeneratedRegex(ConceptTextPattern)]
    private static partial Regex ConceptTextRegex();

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

    /// <summary>
    /// El QR ya venció (por tiempo). No cambia <see cref="Status"/>: la transición a <see cref="SaleStatus.Expired"/>
    /// la hace el job de expiración.
    /// </summary>
    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAt is { } expiresAt && now >= expiresAt;

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
