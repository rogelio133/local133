using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Local133.Application.Sales;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Sales;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>
/// Monto del cobro. Llega como texto y se convierte con cultura invariante (punto decimal): así no depende de la
/// cultura del servidor y no se aceptan signos, exponentes ni separadores de miles.
/// </summary>
public sealed class ChargeInput
{
    /// <summary>Hasta 6 enteros y 2 decimales, sin signo. La misma regla aplica <c>amount-input.js</c> al teclear.</summary>
    public const string AmountPattern = @"^\d{1,6}(\.\d{1,2})?$";

    /// <summary>Valor inicial del campo: un centavo (lo mínimo que acepta CoDi, útil para probar).</summary>
    public const string DefaultAmount = "0.01";

    [Required(ErrorMessage = SaleAmountMessages.Required)]
    [RegularExpression(AmountPattern, ErrorMessage = SaleAmountMessages.Invalid)]
    [Display(Name = "Monto a cobrar")]
    public string? Amount { get; set; } = DefaultAmount;

    /// <summary>Concepto del cobro; el pagador lo ve en su app junto con el nombre del negocio.</summary>
    [Required(ErrorMessage = SaleConceptMessages.Required)]
    [StringLength(Sale.ConceptTextMaxLength, ErrorMessage = SaleConceptMessages.TooLong)]
    [RegularExpression(Sale.ConceptTextPattern, ErrorMessage = SaleConceptMessages.Invalid)]
    [Display(Name = "Concepto")]
    public string? Concept { get; set; }

    public bool TryGetAmount(out decimal amount) =>
        decimal.TryParse(Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
}

/// <summary>Pantalla "Cobrar": formulario de monto, o aviso si el vendedor aún no puede cobrar.</summary>
public sealed class ChargeFormViewModel
{
    public ChargeInput Input { get; set; } = new();

    /// <summary>Aviso cuando no puede cobrar; <c>null</c> = puede cobrar.</summary>
    public ChargeBlockedNotice? Blocked { get; init; }

    public static ChargeBlockedNotice? BlockedNoticeFor(Domain.Merchants.Merchant merchant, BankAccount? account)
    {
        if (merchant.CanCharge(account))
        {
            return null;
        }

        return merchant.Status switch
        {
            MerchantStatus.PendingBankAccount when account?.CodiStatus == PeyCodiStatus.Error => new(
                "Corrige tu cuenta bancaria",
                "Banxico rechazó tu cuenta. Corrígela para empezar a cobrar con CoDi.",
                "~/app/cuenta-bancaria", "Corregir mi cuenta"),
            MerchantStatus.PendingBankAccount => new(
                "Registra tu cuenta bancaria",
                "Para cobrar con CoDi necesitas registrar la CLABE donde recibirás tu dinero.",
                "~/app/cuenta-bancaria", "Registrar mi CLABE"),
            MerchantStatus.BankAccountInReview => new(
                "Tu cuenta está en revisión",
                "Banxico está revisando tu cuenta bancaria; puede tardar hasta 5 días hábiles. Podrás cobrar en cuanto quede activa.",
                "~/app/cuenta-bancaria", "Ver estatus"),
            MerchantStatus.Active => new(
                "Tu cuenta bancaria no está activa",
                "Tu cuenta bancaria no está activa para CoDi. Revísala para volver a cobrar.",
                "~/app/cuenta-bancaria", "Ver cuenta bancaria"),
            MerchantStatus.Suspended => new(
                "Tu cuenta está suspendida",
                "Por ahora no puedes generar cobros. Ponte en contacto con nosotros.",
                "~/app", "Volver al inicio"),
            _ => new(
                "Tu cuenta está dada de baja",
                "No puedes generar cobros. Ponte en contacto con nosotros.",
                "~/app", "Volver al inicio"),
        };
    }
}

public sealed record ChargeBlockedNotice(string Title, string Message, string LinkHref, string LinkText);

/// <summary>Pantalla del QR de una venta.</summary>
public sealed record SaleQrViewModel(
    int Id,
    int Folio,
    string AmountText,
    string? Concept,
    SaleStatus Status,
    string? QrString,
    int? RemainingSeconds)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>Se puede mostrar el QR para pagar: pendiente, con QR y sin vencer.</summary>
    public bool ShowQr => Status == SaleStatus.Pending && QrString is not null && RemainingSeconds is not 0;

    /// <summary>Venció por tiempo (o nunca tuvo QR) sin pagarse.</summary>
    public bool IsExpired => Status == SaleStatus.Expired || (Status == SaleStatus.Pending && !ShowQr);

    public string? RemainingText => RemainingSeconds is { } s ? FormatCountdown(s) : null;

    public static SaleQrViewModel From(Sale sale, DateTimeOffset now) => new(
        sale.Id,
        sale.Folio,
        FormatAmount(sale.Amount),
        sale.Concept,
        sale.Status,
        sale.QrString,
        sale.RemainingSeconds(now));

    public static string FormatAmount(decimal amount) => amount.ToString("C2", Spanish);

    /// <summary><c>mm:ss</c>, igual que la cuenta regresiva de <c>sale-qr.js</c>.</summary>
    public static string FormatCountdown(int seconds) =>
        $"{seconds / 60:D2}:{seconds % 60:D2}";
}
