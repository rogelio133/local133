using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Local133.Domain.Settings;

namespace Local133.Web.Areas.Admin.Models;

/// <summary>
/// Formulario de los parámetros globales (6.2). Los nombres son los de <see cref="SystemSettings"/>, para que los
/// errores de <see cref="SystemSettingsRules"/> caigan en su campo. Precios y porcentajes llegan como texto con punto
/// decimal (cultura invariante); los porcentajes van en % (16 = 0.16).
/// </summary>
public sealed class SettingsInput
{
    public const string PricePattern = @"^\d{1,5}(\.\d{1,2})?$";
    public const string PercentPattern = @"^\d{1,2}(\.\d{1,2})?$";
    private const string PriceMessage = "Escribe un precio como 199 o 199.50.";
    private const string PercentMessage = "Escribe un porcentaje como 16 o 3.5.";
    private const string RequiredMessage = "Escribe un valor.";
    private const string WholeMessage = "Escribe un número entero.";

    [Display(Name = "Pro, 1 mes")]
    [Required(ErrorMessage = RequiredMessage)]
    [RegularExpression(PricePattern, ErrorMessage = PriceMessage)]
    public string? ProMonthlyPrice { get; set; }

    [Display(Name = "Pro, anual")]
    [Required(ErrorMessage = RequiredMessage)]
    [RegularExpression(PricePattern, ErrorMessage = PriceMessage)]
    public string? ProAnnualPrice { get; set; }

    [Display(Name = "Fundador, 1 mes")]
    [Required(ErrorMessage = RequiredMessage)]
    [RegularExpression(PricePattern, ErrorMessage = PriceMessage)]
    public string? ProFounderMonthlyPrice { get; set; }

    [Display(Name = "Fundador, anual")]
    [Required(ErrorMessage = RequiredMessage)]
    [RegularExpression(PricePattern, ErrorMessage = PriceMessage)]
    public string? ProFounderAnnualPrice { get; set; }

    [Display(Name = "Lugares de fundador")]
    [Required(ErrorMessage = RequiredMessage)]
    [Range(0, SystemSettingsRules.MaxCount, ErrorMessage = "Escribe un número entre 0 y 1,000,000.")]
    public int? FounderSlots { get; set; }

    [Display(Name = "Días de prueba")]
    [Required(ErrorMessage = RequiredMessage)]
    [Range(1, SystemSettingsRules.MaxTrialDays, ErrorMessage = "Escribe un número entre 1 y 365.")]
    public int? TrialDays { get; set; }

    [Display(Name = "Días de gracia")]
    [Required(ErrorMessage = RequiredMessage)]
    [Range(0, SystemSettingsRules.MaxGraceDays, ErrorMessage = "Escribe un número entre 0 y 30.")]
    public int? GraceDays { get; set; }

    [Display(Name = "Uso justo: cobros concretados al mes")]
    [Required(ErrorMessage = RequiredMessage)]
    [Range(1, SystemSettingsRules.MaxCount, ErrorMessage = "Escribe un número entre 1 y 1,000,000.")]
    public int? FairUseMonthlyPaid { get; set; }

    [Display(Name = "Días de Pro por referido que paga")]
    [Required(ErrorMessage = RequiredMessage)]
    [Range(1, SystemSettingsRules.MaxRewardDays, ErrorMessage = "Escribe un número entre 1 y 365.")]
    public int? ReferralRewardDays { get; set; }

    [Display(Name = "Máximo de premios en 12 meses")]
    [Required(ErrorMessage = RequiredMessage)]
    [Range(0, SystemSettingsRules.MaxRewardsPerYear, ErrorMessage = "Escribe un número entre 0 y 365.")]
    public int? ReferralRewardsPerYear { get; set; }

    [Display(Name = "Descuento de bienvenida del referido")]
    [Required(ErrorMessage = RequiredMessage)]
    [RegularExpression(PercentPattern, ErrorMessage = PercentMessage)]
    public string? ReferralWelcomeDiscountRate { get; set; }

    [Display(Name = "IVA incluido en los precios")]
    [Required(ErrorMessage = RequiredMessage)]
    [RegularExpression(PercentPattern, ErrorMessage = PercentMessage)]
    public string? VatRate { get; set; }

    [Display(Name = "Expiración del QR de venta")]
    [Required(ErrorMessage = RequiredMessage)]
    [Range(SystemSettingsRules.MinSaleQrExpirationSeconds, SystemSettingsRules.MaxSaleQrExpirationSeconds,
        ErrorMessage = "Escribe entre 360 y 86,400 segundos (Pey exige al menos 360).")]
    public int? SaleQrExpirationSeconds { get; set; }

    [Display(Name = "Comisión de referencia de una terminal")]
    [Required(ErrorMessage = RequiredMessage)]
    [RegularExpression(PercentPattern, ErrorMessage = PercentMessage)]
    public string? ReferenceTerminalFeeRate { get; set; }

    public static SettingsInput From(SystemSettings settings) => new()
    {
        ProMonthlyPrice = Price(settings.ProMonthlyPrice),
        ProAnnualPrice = Price(settings.ProAnnualPrice),
        ProFounderMonthlyPrice = Price(settings.ProFounderMonthlyPrice),
        ProFounderAnnualPrice = Price(settings.ProFounderAnnualPrice),
        FounderSlots = settings.FounderSlots,
        TrialDays = settings.TrialDays,
        GraceDays = settings.GraceDays,
        FairUseMonthlyPaid = settings.FairUseMonthlyPaid,
        ReferralRewardDays = settings.ReferralRewardDays,
        ReferralRewardsPerYear = settings.ReferralRewardsPerYear,
        ReferralWelcomeDiscountRate = Percent(settings.ReferralWelcomeDiscountRate),
        VatRate = Percent(settings.VatRate),
        SaleQrExpirationSeconds = settings.SaleQrExpirationSeconds,
        ReferenceTerminalFeeRate = Percent(settings.ReferenceTerminalFeeRate),
    };

    /// <summary>Los valores como <see cref="SystemSettings"/> (ya pasó la validación del formulario).</summary>
    public SystemSettings ToSettings() => new()
    {
        ProMonthlyPrice = ParseDecimal(ProMonthlyPrice),
        ProAnnualPrice = ParseDecimal(ProAnnualPrice),
        ProFounderMonthlyPrice = ParseDecimal(ProFounderMonthlyPrice),
        ProFounderAnnualPrice = ParseDecimal(ProFounderAnnualPrice),
        FounderSlots = FounderSlots ?? 0,
        TrialDays = TrialDays ?? 0,
        GraceDays = GraceDays ?? 0,
        FairUseMonthlyPaid = FairUseMonthlyPaid ?? 0,
        ReferralRewardDays = ReferralRewardDays ?? 0,
        ReferralRewardsPerYear = ReferralRewardsPerYear ?? 0,
        ReferralWelcomeDiscountRate = ParseDecimal(ReferralWelcomeDiscountRate) / 100m,
        VatRate = ParseDecimal(VatRate) / 100m,
        SaleQrExpirationSeconds = SaleQrExpirationSeconds ?? 0,
        ReferenceTerminalFeeRate = ParseDecimal(ReferenceTerminalFeeRate) / 100m,
    };

    private static string Price(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Percent(decimal rate) => (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture);

    private static decimal ParseDecimal(string? text) =>
        decimal.TryParse(text?.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : -1m;
}

/// <summary>Un cambio reciente de los parámetros.</summary>
public sealed record SettingsChangeRow(string WhenText, string ActorEmail, IReadOnlyList<SettingsChange> Changes);

/// <summary>Pantalla de parámetros (/admin/parametros).</summary>
public sealed class SettingsViewModel
{
    public const int RecentChanges = 10;

    public required SettingsInput Input { get; set; }

    public int AssignedFounders { get; init; }

    public IReadOnlyList<SettingsChangeRow> Recent { get; init; } = [];
}

/// <summary>Nombre y ayuda de cada parámetro (los de la pantalla y los de la bitácora).</summary>
public static class SettingsLabels
{
    public static string Label(string field) =>
        typeof(SettingsInput).GetProperty(field)?.GetCustomAttributes(typeof(DisplayAttribute), false)
            .OfType<DisplayAttribute>().FirstOrDefault()?.Name ?? field;

    public static string Help(string field) => field switch
    {
        nameof(SystemSettings.ProMonthlyPrice) => "IVA incluido. Los paquetes de 3 y 6 meses son múltiplos. Solo afecta a los links nuevos.",
        nameof(SystemSettings.ProAnnualPrice) => "IVA incluido. Solo afecta a los links nuevos; quien renueva el anual conserva su precio congelado.",
        nameof(SystemSettings.ProFounderMonthlyPrice) => "De por vida para los fundadores. No puede ser mayor que el precio normal.",
        nameof(SystemSettings.ProFounderAnnualPrice) => "De por vida para los fundadores. No puede ser mayor que el precio normal.",
        nameof(SystemSettings.FounderSlots) => "Los primeros que paguen Pro. No puede bajar de los lugares ya asignados.",
        nameof(SystemSettings.TrialDays) => "Solo afecta a las pruebas que empiecen después del cambio.",
        nameof(SystemSettings.GraceDays) => "Cambia de inmediato el estado de quien tenga un pago vencido (gracia o vencido).",
        nameof(SystemSettings.FairUseMonthlyPaid) => "Al pasarlo se alerta al admin, sin bloquear.",
        nameof(SystemSettings.ReferralRewardDays) => "Para los premios que se den después del cambio.",
        nameof(SystemSettings.ReferralRewardsPerYear) => "En 12 meses móviles. 0 = sin premios.",
        nameof(SystemSettings.ReferralWelcomeDiscountRate) => "Sobre el precio de un mes, en el primer pago. Solo afecta a los links nuevos.",
        nameof(SystemSettings.VatRate) => "Sirve para desglosar el IVA incluido; los precios no cambian.",
        nameof(SystemSettings.SaleQrExpirationSeconds) => "Para los QR nuevos. Pey exige al menos 360 segundos.",
        nameof(SystemSettings.ReferenceTerminalFeeRate) => "Sin IVA. Para la estimación del resumen mensual.",
        _ => string.Empty,
    };

    /// <summary>Valor guardado (texto invariante) para la pantalla: los porcentajes en %.</summary>
    public static string Display(string field, string value) =>
        field is nameof(SystemSettings.ReferralWelcomeDiscountRate) or nameof(SystemSettings.VatRate) or nameof(SystemSettings.ReferenceTerminalFeeRate)
            && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate)
            ? $"{(rate * 100m).ToString("0.##", CultureInfo.InvariantCulture)} %"
            : value;
}
