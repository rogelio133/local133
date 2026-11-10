using Local133.Domain.Common;

namespace Local133.Domain.Merchants;

/// <summary>
/// Vendedor (tenant). Se crea al registrarse, ligado 1:1 al usuario de Identity, en
/// <see cref="MerchantStatus.PendingBankAccount"/>.
/// </summary>
public class Merchant : Entity
{
    public const int BusinessNameMaxLength = 120;

    /// <summary>Id del usuario de Identity (<c>AspNetUsers.Id</c>).</summary>
    public string UserId { get; set; } = null!;

    public MerchantStatus Status { get; set; } = MerchantStatus.PendingBankAccount;

    /// <summary>Nombre del negocio (se captura en "Mi información").</summary>
    public string? BusinessName { get; set; }

    /// <summary>Comisión propia del vendedor; si es <c>null</c> aplica la global de <c>SystemSettings</c>.</summary>
    public decimal? CommissionRateOverride { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static Merchant Register(string userId, DateTimeOffset now) => new()
    {
        UserId = userId,
        Status = MerchantStatus.PendingBankAccount,
        CreatedAt = now,
    };
}
