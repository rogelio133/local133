using System.Globalization;

namespace Local133.Domain.Settings;

/// <summary>Un error de validación de un parámetro (<see cref="Field"/> es el nombre de la propiedad).</summary>
public sealed record SettingsError(string Field, string Message);

/// <summary>Un parámetro que cambió, con sus valores en texto invariante.</summary>
public sealed record SettingsChange(string Field, string OldValue, string NewValue);

/// <summary>
/// Reglas de los parámetros globales que edita el admin (6.2). Los precios llevan el IVA incluido y las tasas son
/// fracciones (0.16 = 16 %) con hasta 4 decimales, por la columna <c>decimal(5,4)</c>.
/// </summary>
public static class SystemSettingsRules
{
    public const decimal MinPrice = 0.01m;
    public const decimal MaxPrice = 99_999.99m;
    public const int MaxTrialDays = 365;
    public const int MaxGraceDays = 30;
    public const int MaxCount = 1_000_000;
    public const int MaxRewardDays = 365;
    public const int MaxRewardsPerYear = 365;
    public const decimal MaxWelcomeDiscountRate = 0.90m;
    public const decimal MaxVatRate = 0.50m;
    public const decimal MaxTerminalFeeRate = 0.20m;

    /// <summary>Pey exige 0 o ≥ 360 s; el QR de venta nunca es «sin expiración».</summary>
    public const int MinSaleQrExpirationSeconds = 360;

    public const int MaxSaleQrExpirationSeconds = 86_400;

    /// <summary>
    /// Parámetros que aparecen escritos en los Términos (los que compara <c>LegalPagesTests</c>). Cambiar uno exige
    /// publicar una versión nueva de los Términos (y, si es un precio o la prueba, cambiar también la portada).
    /// </summary>
    public static readonly IReadOnlySet<string> InTerms = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(SystemSettings.BasicMonthlyPrice),
        nameof(SystemSettings.BasicAnnualPrice),
        nameof(SystemSettings.BasicFounderMonthlyPrice),
        nameof(SystemSettings.BasicFounderAnnualPrice),
        nameof(SystemSettings.ProMonthlyPrice),
        nameof(SystemSettings.ProAnnualPrice),
        nameof(SystemSettings.ProFounderMonthlyPrice),
        nameof(SystemSettings.ProFounderAnnualPrice),
        nameof(SystemSettings.FounderSlots),
        nameof(SystemSettings.TrialDays),
        nameof(SystemSettings.GraceDays),
        nameof(SystemSettings.FairUseMonthlyPaid),
        nameof(SystemSettings.ReferralRewardDays),
        nameof(SystemSettings.ReferralRewardsPerYear),
        nameof(SystemSettings.ReferralWelcomeDiscountRate),
    };

    /// <summary>Todos los parámetros editables, en el orden de la pantalla.</summary>
    public static readonly IReadOnlyList<string> Fields =
    [
        nameof(SystemSettings.BasicMonthlyPrice),
        nameof(SystemSettings.BasicAnnualPrice),
        nameof(SystemSettings.BasicFounderMonthlyPrice),
        nameof(SystemSettings.BasicFounderAnnualPrice),
        nameof(SystemSettings.ProMonthlyPrice),
        nameof(SystemSettings.ProAnnualPrice),
        nameof(SystemSettings.ProFounderMonthlyPrice),
        nameof(SystemSettings.ProFounderAnnualPrice),
        nameof(SystemSettings.FounderSlots),
        nameof(SystemSettings.TrialDays),
        nameof(SystemSettings.GraceDays),
        nameof(SystemSettings.FairUseMonthlyPaid),
        nameof(SystemSettings.ReferralRewardDays),
        nameof(SystemSettings.ReferralRewardsPerYear),
        nameof(SystemSettings.ReferralWelcomeDiscountRate),
        nameof(SystemSettings.VatRate),
        nameof(SystemSettings.SaleQrExpirationSeconds),
        nameof(SystemSettings.ReferenceTerminalFeeRate),
    ];

    /// <param name="assignedFounders">Lugares de fundador ya asignados: no se puede bajar de ahí.</param>
    public static IReadOnlyList<SettingsError> Validate(SystemSettings settings, int assignedFounders)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<SettingsError>();

        foreach (var field in PriceFields)
        {
            Price(errors, field, PriceOf(settings, field));
        }

        // Fundador ≤ normal en cada plan.
        NotAbove(errors, nameof(SystemSettings.BasicFounderMonthlyPrice), settings.BasicFounderMonthlyPrice, settings.BasicMonthlyPrice,
            "El precio de fundador no puede ser mayor que el normal.");
        NotAbove(errors, nameof(SystemSettings.BasicFounderAnnualPrice), settings.BasicFounderAnnualPrice, settings.BasicAnnualPrice,
            "El precio de fundador no puede ser mayor que el normal.");
        NotAbove(errors, nameof(SystemSettings.ProFounderMonthlyPrice), settings.ProFounderMonthlyPrice, settings.ProMonthlyPrice,
            "El precio de fundador no puede ser mayor que el normal.");
        NotAbove(errors, nameof(SystemSettings.ProFounderAnnualPrice), settings.ProFounderAnnualPrice, settings.ProAnnualPrice,
            "El precio de fundador no puede ser mayor que el normal.");

        // Básico < Pro, también entre los precios de fundador: si no, subir de plan no tendría sentido (la conversión
        // de los días de Básico a Pro usa la proporción de los mensuales).
        Below(errors, nameof(SystemSettings.BasicMonthlyPrice), settings.BasicMonthlyPrice, settings.ProMonthlyPrice);
        Below(errors, nameof(SystemSettings.BasicAnnualPrice), settings.BasicAnnualPrice, settings.ProAnnualPrice);
        Below(errors, nameof(SystemSettings.BasicFounderMonthlyPrice), settings.BasicFounderMonthlyPrice, settings.ProFounderMonthlyPrice);
        Below(errors, nameof(SystemSettings.BasicFounderAnnualPrice), settings.BasicFounderAnnualPrice, settings.ProFounderAnnualPrice);

        Range(errors, nameof(SystemSettings.FounderSlots), settings.FounderSlots, 0, MaxCount);
        if (settings.FounderSlots < assignedFounders)
        {
            errors.Add(new(nameof(SystemSettings.FounderSlots),
                $"Ya hay {assignedFounders.ToString(CultureInfo.InvariantCulture)} fundadores: no puede haber menos lugares."));
        }

        Range(errors, nameof(SystemSettings.TrialDays), settings.TrialDays, 1, MaxTrialDays);
        Range(errors, nameof(SystemSettings.GraceDays), settings.GraceDays, 0, MaxGraceDays);
        Range(errors, nameof(SystemSettings.FairUseMonthlyPaid), settings.FairUseMonthlyPaid, 1, MaxCount);
        Range(errors, nameof(SystemSettings.ReferralRewardDays), settings.ReferralRewardDays, 1, MaxRewardDays);
        Range(errors, nameof(SystemSettings.ReferralRewardsPerYear), settings.ReferralRewardsPerYear, 0, MaxRewardsPerYear);
        Rate(errors, nameof(SystemSettings.ReferralWelcomeDiscountRate), settings.ReferralWelcomeDiscountRate, MaxWelcomeDiscountRate);
        Rate(errors, nameof(SystemSettings.VatRate), settings.VatRate, MaxVatRate);
        Range(errors, nameof(SystemSettings.SaleQrExpirationSeconds), settings.SaleQrExpirationSeconds,
            MinSaleQrExpirationSeconds, MaxSaleQrExpirationSeconds);
        Rate(errors, nameof(SystemSettings.ReferenceTerminalFeeRate), settings.ReferenceTerminalFeeRate, MaxTerminalFeeRate);
        return errors;
    }

    /// <summary>Los parámetros editables que difieren, en el orden de <see cref="Fields"/>.</summary>
    public static IReadOnlyList<SettingsChange> Changes(SystemSettings before, SystemSettings after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var changes = new List<SettingsChange>();
        foreach (var field in Fields)
        {
            var oldValue = ValueOf(before, field);
            var newValue = ValueOf(after, field);
            if (oldValue != newValue)
            {
                changes.Add(new(field, oldValue, newValue));
            }
        }

        return changes;
    }

    /// <summary>Copia los parámetros editables (no el id).</summary>
    public static void CopyValues(SystemSettings from, SystemSettings to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        to.BasicMonthlyPrice = from.BasicMonthlyPrice;
        to.BasicAnnualPrice = from.BasicAnnualPrice;
        to.BasicFounderMonthlyPrice = from.BasicFounderMonthlyPrice;
        to.BasicFounderAnnualPrice = from.BasicFounderAnnualPrice;
        to.ProMonthlyPrice = from.ProMonthlyPrice;
        to.ProAnnualPrice = from.ProAnnualPrice;
        to.ProFounderMonthlyPrice = from.ProFounderMonthlyPrice;
        to.ProFounderAnnualPrice = from.ProFounderAnnualPrice;
        to.FounderSlots = from.FounderSlots;
        to.TrialDays = from.TrialDays;
        to.GraceDays = from.GraceDays;
        to.FairUseMonthlyPaid = from.FairUseMonthlyPaid;
        to.ReferralRewardDays = from.ReferralRewardDays;
        to.ReferralRewardsPerYear = from.ReferralRewardsPerYear;
        to.ReferralWelcomeDiscountRate = from.ReferralWelcomeDiscountRate;
        to.VatRate = from.VatRate;
        to.SaleQrExpirationSeconds = from.SaleQrExpirationSeconds;
        to.ReferenceTerminalFeeRate = from.ReferenceTerminalFeeRate;
    }

    /// <summary>Valor en texto invariante (los decimales sin ceros de sobra: 199, 0.3).</summary>
    public static string ValueOf(SystemSettings settings, string field) => field switch
    {
        _ when PriceFields.Contains(field) => Text(PriceOf(settings, field)),
        nameof(SystemSettings.FounderSlots) => Text(settings.FounderSlots),
        nameof(SystemSettings.TrialDays) => Text(settings.TrialDays),
        nameof(SystemSettings.GraceDays) => Text(settings.GraceDays),
        nameof(SystemSettings.FairUseMonthlyPaid) => Text(settings.FairUseMonthlyPaid),
        nameof(SystemSettings.ReferralRewardDays) => Text(settings.ReferralRewardDays),
        nameof(SystemSettings.ReferralRewardsPerYear) => Text(settings.ReferralRewardsPerYear),
        nameof(SystemSettings.ReferralWelcomeDiscountRate) => Text(settings.ReferralWelcomeDiscountRate),
        nameof(SystemSettings.VatRate) => Text(settings.VatRate),
        nameof(SystemSettings.SaleQrExpirationSeconds) => Text(settings.SaleQrExpirationSeconds),
        nameof(SystemSettings.ReferenceTerminalFeeRate) => Text(settings.ReferenceTerminalFeeRate),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Parámetro desconocido."),
    };

    /// <summary>Los 8 precios (Básico y Pro, normal y de fundador).</summary>
    private static readonly IReadOnlyList<string> PriceFields =
    [
        nameof(SystemSettings.BasicMonthlyPrice),
        nameof(SystemSettings.BasicAnnualPrice),
        nameof(SystemSettings.BasicFounderMonthlyPrice),
        nameof(SystemSettings.BasicFounderAnnualPrice),
        nameof(SystemSettings.ProMonthlyPrice),
        nameof(SystemSettings.ProAnnualPrice),
        nameof(SystemSettings.ProFounderMonthlyPrice),
        nameof(SystemSettings.ProFounderAnnualPrice),
    ];

    private static decimal PriceOf(SystemSettings settings, string field) => field switch
    {
        nameof(SystemSettings.BasicMonthlyPrice) => settings.BasicMonthlyPrice,
        nameof(SystemSettings.BasicAnnualPrice) => settings.BasicAnnualPrice,
        nameof(SystemSettings.BasicFounderMonthlyPrice) => settings.BasicFounderMonthlyPrice,
        nameof(SystemSettings.BasicFounderAnnualPrice) => settings.BasicFounderAnnualPrice,
        nameof(SystemSettings.ProMonthlyPrice) => settings.ProMonthlyPrice,
        nameof(SystemSettings.ProAnnualPrice) => settings.ProAnnualPrice,
        nameof(SystemSettings.ProFounderMonthlyPrice) => settings.ProFounderMonthlyPrice,
        nameof(SystemSettings.ProFounderAnnualPrice) => settings.ProFounderAnnualPrice,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "No es un precio."),
    };

    private static void NotAbove(List<SettingsError> errors, string field, decimal value, decimal max, string message)
    {
        if (value > max)
        {
            errors.Add(new(field, message));
        }
    }

    private static void Below(List<SettingsError> errors, string field, decimal basic, decimal pro)
    {
        if (basic >= pro)
        {
            errors.Add(new(field, "Básico debe costar menos que Pro."));
        }
    }

    private static string Text(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Price(List<SettingsError> errors, string field, decimal value)
    {
        if (value < MinPrice || value > MaxPrice)
        {
            errors.Add(new(field, $"Escribe un precio entre {MinPrice.ToString(CultureInfo.InvariantCulture)} y {MaxPrice.ToString("#,0.00", CultureInfo.InvariantCulture)}."));
        }
        else if (decimal.Round(value, 2) != value)
        {
            errors.Add(new(field, "El precio admite hasta 2 decimales."));
        }
    }

    private static void Range(List<SettingsError> errors, string field, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            errors.Add(new(field, $"Escribe un número entre {min.ToString("#,0", CultureInfo.InvariantCulture)} y {max.ToString("#,0", CultureInfo.InvariantCulture)}."));
        }
    }

    private static void Rate(List<SettingsError> errors, string field, decimal value, decimal max)
    {
        if (value < 0 || value > max)
        {
            errors.Add(new(field, $"Escribe un porcentaje entre 0 y {(max * 100).ToString("0.##", CultureInfo.InvariantCulture)} %."));
        }
        else if (decimal.Round(value, 4) != value)
        {
            errors.Add(new(field, "El porcentaje admite hasta 2 decimales."));
        }
    }
}
