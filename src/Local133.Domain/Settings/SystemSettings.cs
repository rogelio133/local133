using Local133.Domain.Common;

namespace Local133.Domain.Settings;

/// <summary>
/// Parámetros globales de la plataforma (una sola fila): expiración del QR, IVA y los de la suscripción (precios,
/// prueba, gracia, límites del gratuito y referidos). Los precios llevan el IVA incluido. Cada cargo de la
/// suscripción guarda su propio monto: cambiar un precio aquí solo afecta a los links nuevos.
/// </summary>
public class SystemSettings : Entity
{
    public const int SingletonId = 1;

    public const decimal DefaultVatRate = 0.16m;
    public const int DefaultGraceDays = 5;
    public const int DefaultSaleQrExpirationSeconds = 600;
    public const decimal DefaultMonthlyPrice = 199m;
    public const decimal DefaultAnnualPrice = 1990m;
    public const decimal DefaultFounderMonthlyPrice = 149m;
    public const decimal DefaultFounderAnnualPrice = 1490m;
    public const int DefaultFounderSlots = 100;
    public const int DefaultTrialDays = 30;
    public const int DefaultFreeMonthlyPaidLimit = 100;
    public const int DefaultFreeCourtesyCharges = 5;
    public const int DefaultFreeMonthlyGeneratedLimit = 300;
    public const int DefaultFreeHistoryDays = 30;
    public const int DefaultProFairUseMonthlyPaid = 5000;
    public const int DefaultReferralRewardDays = 10;
    public const int DefaultReferralRewardsPerYear = 12;
    public const decimal DefaultReferralWelcomeDiscountRate = 0.30m;
    public const decimal DefaultReferenceTerminalFeeRate = 0.035m;

    /// <summary>Tasa de IVA incluida en los precios (0.16 = 16 %), para desglosarlo.</summary>
    public decimal VatRate { get; set; } = DefaultVatRate;

    /// <summary>Días de gracia después de vencer un periodo pagado, antes de bajar al plan gratuito.</summary>
    public int GraceDays { get; set; } = DefaultGraceDays;

    /// <summary>Expiración del QR dinámico de venta, en segundos (Pey exige 0 o ≥ 360).</summary>
    public int SaleQrExpirationSeconds { get; set; } = DefaultSaleQrExpirationSeconds;

    /// <summary>Pro, un mes (IVA incluido). Los paquetes de 3 y 6 meses son múltiplos de este precio.</summary>
    public decimal MonthlyPrice { get; set; } = DefaultMonthlyPrice;

    /// <summary>Pro, un año (IVA incluido).</summary>
    public decimal AnnualPrice { get; set; } = DefaultAnnualPrice;

    /// <summary>Precio mensual de fundador (de por vida para los primeros <see cref="FounderSlots"/> que paguen).</summary>
    public decimal FounderMonthlyPrice { get; set; } = DefaultFounderMonthlyPrice;

    /// <summary>Precio anual de fundador.</summary>
    public decimal FounderAnnualPrice { get; set; } = DefaultFounderAnnualPrice;

    /// <summary>Lugares de fundador: los primeros que paguen Pro.</summary>
    public int FounderSlots { get; set; } = DefaultFounderSlots;

    /// <summary>Días de la prueba de Pro, que empieza cuando la cuenta bancaria queda activa.</summary>
    public int TrialDays { get; set; } = DefaultTrialDays;

    /// <summary>Cobros concretados al mes en el gratuito (mes calendario en hora de México).</summary>
    public int FreeMonthlyPaidLimit { get; set; } = DefaultFreeMonthlyPaidLimit;

    /// <summary>Cobros de cortesía después de llegar a <see cref="FreeMonthlyPaidLimit"/>.</summary>
    public int FreeCourtesyCharges { get; set; } = DefaultFreeCourtesyCharges;

    /// <summary>Tope técnico de cobros generados (pagados o no) al mes en el gratuito.</summary>
    public int FreeMonthlyGeneratedLimit { get; set; } = DefaultFreeMonthlyGeneratedLimit;

    /// <summary>Días del historial de ventas visibles en el gratuito.</summary>
    public int FreeHistoryDays { get; set; } = DefaultFreeHistoryDays;

    /// <summary>Uso justo de Pro: cobros concretados al mes a partir de los que se alerta al admin (sin bloquear).</summary>
    public int ProFairUseMonthlyPaid { get; set; } = DefaultProFairUseMonthlyPaid;

    /// <summary>Días de Pro que gana un vendedor cuando su referido paga por primera vez.</summary>
    public int ReferralRewardDays { get; set; } = DefaultReferralRewardDays;

    /// <summary>Máximo de premios por referidos en 12 meses móviles.</summary>
    public int ReferralRewardsPerYear { get; set; } = DefaultReferralRewardsPerYear;

    /// <summary>Descuento del referido en su primer pago, sobre el precio de un mes (0.30 = 30 %).</summary>
    public decimal ReferralWelcomeDiscountRate { get; set; } = DefaultReferralWelcomeDiscountRate;

    /// <summary>Comisión de referencia de una terminal (sin IVA) para el resumen mensual; es una estimación.</summary>
    public decimal ReferenceTerminalFeeRate { get; set; } = DefaultReferenceTerminalFeeRate;
}
