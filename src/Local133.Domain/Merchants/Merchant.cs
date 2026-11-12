using Local133.Domain.BankAccounts;
using Local133.Domain.Common;
using Local133.Domain.Pey;

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

    /// <summary>
    /// Puede capturar (o volver a capturar) su CLABE: aún no la registra, o Pey rechazó la que envió.
    /// </summary>
    public bool CanSubmitBankAccount(BankAccount? current) =>
        Status == MerchantStatus.PendingBankAccount && (current is null || current.CodiStatus == PeyCodiStatus.Error);

    /// <summary>
    /// Transición por el estatus CoDi de su cuenta (alta, webhook <c>bank.account.updated</c> o sondeo). Solo aplica
    /// durante el onboarding; un vendedor activo, suspendido o dado de baja no cambia aquí.
    /// Devuelve <c>true</c> si cambió el estatus.
    /// </summary>
    public bool ApplyBankAccountStatus(PeyCodiStatus codiStatus)
    {
        if (Status is not (MerchantStatus.PendingBankAccount or MerchantStatus.BankAccountInReview))
        {
            return false;
        }

        var next = codiStatus switch
        {
            PeyCodiStatus.Pending or PeyCodiStatus.Processing => MerchantStatus.BankAccountInReview,
            PeyCodiStatus.Active => MerchantStatus.Active,
            PeyCodiStatus.Error => MerchantStatus.PendingBankAccount,
            _ => throw new ArgumentOutOfRangeException(nameof(codiStatus), codiStatus, "Estatus CoDi desconocido."),
        };
        if (next == Status)
        {
            return false;
        }

        Status = next;
        return true;
    }
}
