using Local133.Domain.Common;

namespace Local133.Domain.Settings;

/// <summary>
/// Parámetros globales de la plataforma (una sola fila). La comisión del vendedor es
/// <c>Merchant.CommissionRateOverride ?? CommissionRate</c> y se copia en cada pago al cobrarse.
/// </summary>
public class SystemSettings : Entity
{
    public const int SingletonId = 1;

    public const decimal DefaultCommissionRate = 0.03m;
    public const decimal DefaultVatRate = 0.16m;
    public const int DefaultGraceDays = 3;
    public const int DefaultSaleQrExpirationSeconds = 600;

    /// <summary>Tasa de comisión global (0.03 = 3 %).</summary>
    public decimal CommissionRate { get; set; } = DefaultCommissionRate;

    /// <summary>Tasa de IVA aplicada a la comisión (0.16 = 16 %).</summary>
    public decimal VatRate { get; set; } = DefaultVatRate;

    /// <summary>Días después del vencimiento de un estado de cuenta antes de suspender.</summary>
    public int GraceDays { get; set; } = DefaultGraceDays;

    /// <summary>Expiración del QR dinámico de venta, en segundos (Pey exige 0 o ≥ 360).</summary>
    public int SaleQrExpirationSeconds { get; set; } = DefaultSaleQrExpirationSeconds;
}
