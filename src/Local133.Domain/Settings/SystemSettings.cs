using Local133.Domain.Common;

namespace Local133.Domain.Settings;

/// <summary>
/// Parámetros globales de la plataforma (una sola fila): expiración del QR, IVA y los de la suscripción (precios,
/// planes Básico y Pro, prueba, gracia, uso justo y referidos). Los precios llevan el IVA incluido. Cada cargo de la
/// suscripción guarda su propio monto: cambiar un precio aquí solo afecta a los links nuevos.
/// </summary>
public class SystemSettings : Entity
{
    public const int SingletonId = 1;

    public const decimal DefaultVatRate = 0.16m;
    public const int DefaultGraceDays = 5;
    public const int DefaultSaleQrExpirationSeconds = 600;
    public const decimal DefaultBasicMonthlyPrice = 99m;
    public const decimal DefaultBasicAnnualPrice = 990m;
    public const decimal DefaultBasicFounderMonthlyPrice = 69m;
    public const decimal DefaultBasicFounderAnnualPrice = 690m;
    public const decimal DefaultProMonthlyPrice = 199m;
    public const decimal DefaultProAnnualPrice = 1990m;
    public const decimal DefaultProFounderMonthlyPrice = 149m;
    public const decimal DefaultProFounderAnnualPrice = 1490m;
    public const int DefaultFounderSlots = 100;
    public const int DefaultTrialDays = 15;
    public const int DefaultFreeMonthlyPaidLimit = 100;
    public const int DefaultFreeMonthlyGeneratedLimit = 300;
    public const int DefaultFreeHistoryDays = 30;
    public const int DefaultFairUseMonthlyPaid = 5000;
    public const int DefaultReferralRewardDays = 10;
    public const int DefaultReferralRewardsPerYear = 12;
    public const decimal DefaultReferralWelcomeDiscountRate = 0.30m;
    public const decimal DefaultReferenceTerminalFeeRate = 0.035m;

    /// <summary>Tasa de IVA incluida en los precios (0.16 = 16 %), para desglosarlo.</summary>
    public decimal VatRate { get; set; } = DefaultVatRate;

    /// <summary>Días de gracia después de vencer un periodo pagado, con las funciones del plan que pagó.</summary>
    public int GraceDays { get; set; } = DefaultGraceDays;

    /// <summary>Expiración del QR dinámico de venta, en segundos (Pey exige 0 o ≥ 360).</summary>
    public int SaleQrExpirationSeconds { get; set; } = DefaultSaleQrExpirationSeconds;

    /// <summary>Básico, un mes (IVA incluido). Los paquetes de 3 y 6 meses son múltiplos de este precio.</summary>
    public decimal BasicMonthlyPrice { get; set; } = DefaultBasicMonthlyPrice;

    /// <summary>Básico, un año (IVA incluido).</summary>
    public decimal BasicAnnualPrice { get; set; } = DefaultBasicAnnualPrice;

    /// <summary>Básico, un mes, precio de fundador (de por vida).</summary>
    public decimal BasicFounderMonthlyPrice { get; set; } = DefaultBasicFounderMonthlyPrice;

    /// <summary>Básico, un año, precio de fundador.</summary>
    public decimal BasicFounderAnnualPrice { get; set; } = DefaultBasicFounderAnnualPrice;

    /// <summary>Pro, un mes (IVA incluido). Los paquetes de 3 y 6 meses son múltiplos de este precio.</summary>
    public decimal ProMonthlyPrice { get; set; } = DefaultProMonthlyPrice;

    /// <summary>Pro, un año (IVA incluido).</summary>
    public decimal ProAnnualPrice { get; set; } = DefaultProAnnualPrice;

    /// <summary>Pro, un mes, precio de fundador (de por vida para los primeros <see cref="FounderSlots"/> que paguen).</summary>
    public decimal ProFounderMonthlyPrice { get; set; } = DefaultProFounderMonthlyPrice;

    /// <summary>Pro, un año, precio de fundador.</summary>
    public decimal ProFounderAnnualPrice { get; set; } = DefaultProFounderAnnualPrice;

    /// <summary>Lugares de fundador, entre los dos planes: los primeros que paguen cualquiera.</summary>
    public int FounderSlots { get; set; } = DefaultFounderSlots;

    /// <summary>Días de la prueba de Pro (una sola vez por vendedor).</summary>
    public int TrialDays { get; set; } = DefaultTrialDays;

    /// <summary>
    /// Cobros concretados al mes en el gratuito (mes calendario en hora de México). Al llegar a este número ya no se
    /// generan cobros nuevos: no hay cobros de cortesía (se quitaron el 2026-11-20).
    /// </summary>
    public int FreeMonthlyPaidLimit { get; set; } = DefaultFreeMonthlyPaidLimit;

    /// <summary>Tope técnico de cobros generados (pagados o no) al mes en el gratuito.</summary>
    public int FreeMonthlyGeneratedLimit { get; set; } = DefaultFreeMonthlyGeneratedLimit;

    /// <summary>Días del historial de ventas visibles en el gratuito.</summary>
    public int FreeHistoryDays { get; set; } = DefaultFreeHistoryDays;

    /// <summary>Uso justo (Básico y Pro): cobros concretados al mes a partir de los que se alerta al admin (sin bloquear).</summary>
    public int FairUseMonthlyPaid { get; set; } = DefaultFairUseMonthlyPaid;

    /// <summary>Días que gana un vendedor (del plan del final de su cadena) cuando su referido paga por primera vez.</summary>
    public int ReferralRewardDays { get; set; } = DefaultReferralRewardDays;

    /// <summary>Máximo de premios por referidos en 12 meses móviles.</summary>
    public int ReferralRewardsPerYear { get; set; } = DefaultReferralRewardsPerYear;

    /// <summary>Descuento del referido en su primer pago, sobre el precio de un mes (0.30 = 30 %).</summary>
    public decimal ReferralWelcomeDiscountRate { get; set; } = DefaultReferralWelcomeDiscountRate;

    /// <summary>Comisión de referencia de una terminal (sin IVA) para el resumen mensual; es una estimación.</summary>
    public decimal ReferenceTerminalFeeRate { get; set; } = DefaultReferenceTerminalFeeRate;
}
