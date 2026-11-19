using Local133.Domain.Common;

namespace Local133.Domain.Settings;

/// <summary>
/// Parámetros globales de la plataforma (una sola fila). Los de la suscripción (precios, prueba, límites del
/// gratuito) llegan en la Fase 5.
/// </summary>
public class SystemSettings : Entity
{
    public const int SingletonId = 1;

    public const decimal DefaultVatRate = 0.16m;
    public const int DefaultGraceDays = 5;
    public const int DefaultSaleQrExpirationSeconds = 600;

    /// <summary>Tasa de IVA incluida en los precios (0.16 = 16 %), para desglosarlo.</summary>
    public decimal VatRate { get; set; } = DefaultVatRate;

    /// <summary>Días de gracia después de vencer la suscripción, antes de bajar al plan gratuito.</summary>
    public int GraceDays { get; set; } = DefaultGraceDays;

    /// <summary>Expiración del QR dinámico de venta, en segundos (Pey exige 0 o ≥ 360).</summary>
    public int SaleQrExpirationSeconds { get; set; } = DefaultSaleQrExpirationSeconds;
}
