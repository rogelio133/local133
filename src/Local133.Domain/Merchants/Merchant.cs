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

    /// <summary>
    /// Nombre de la persona (<see cref="PersonName"/>), capturado en el alta desde la 4.0. Nulo para los vendedores
    /// registrados antes.
    /// </summary>
    public string? OwnerName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Cuándo se le avisó por correo que su cuenta quedó activa. Es el candado del aviso: el webhook y el sondeo
    /// pueden ver la misma activación a la vez, y solo el primero que lo llena manda el correo.
    /// </summary>
    public DateTimeOffset? ActivationEmailSentAt { get; set; }

    /// <param name="ownerName">Nombre de la persona; se normaliza con <see cref="PersonName.Normalize"/>.</param>
    /// <exception cref="ArgumentException">El nombre no es válido (<see cref="PersonName.IsValid"/>).</exception>
    public static Merchant Register(string userId, DateTimeOffset now, string ownerName)
    {
        if (!PersonName.IsValid(ownerName))
        {
            throw new ArgumentException("El nombre de la persona no es válido.", nameof(ownerName));
        }

        return new Merchant
        {
            UserId = userId,
            Status = MerchantStatus.PendingBankAccount,
            OwnerName = PersonName.Normalize(ownerName),
            CreatedAt = now,
        };
    }

    /// <summary>Con quién saludar en Inicio y en los correos (ver <see cref="GreetingNameFor"/>).</summary>
    public string? GreetingName => GreetingNameFor(OwnerName, BusinessName);

    /// <summary>
    /// Saludo: el primer nombre de la persona; sin él, el nombre del negocio; sin ninguno, <c>null</c> (cada pantalla o
    /// correo pone su respaldo). Estático para usarlo con lo que se proyecta de la base.
    /// </summary>
    public static string? GreetingNameFor(string? ownerName, string? businessName) =>
        PersonName.FirstName(ownerName) ?? (string.IsNullOrWhiteSpace(businessName) ? null : businessName.Trim());

    /// <summary>
    /// Puede capturar (o volver a capturar) su CLABE: aún no la registra, o Pey rechazó la que envió.
    /// </summary>
    public bool CanSubmitBankAccount(BankAccount? current) =>
        Status == MerchantStatus.PendingBankAccount && (current is null || current.CodiStatus == PeyCodiStatus.Error);

    /// <summary>
    /// Puede generar cobros: el vendedor está activo y su cuenta bancaria está activa en CoDi (cada cobro se
    /// deposita en ella con su <c>bankAccountId</c>).
    /// </summary>
    public bool CanCharge(BankAccount? account) =>
        Status == MerchantStatus.Active && account is { CodiStatus: PeyCodiStatus.Active };

    /// <summary>
    /// Puede dar de alta, editar y dar de baja productos: cualquier vendedor salvo uno dado de baja. Durante la
    /// revisión de su cuenta ya puede armar su catálogo, y la suspensión (por cobranza) solo frena los cobros.
    /// </summary>
    public bool CanManageProducts => Status != MerchantStatus.Disabled;

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
