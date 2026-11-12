using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;

namespace Local133.Domain.BankAccounts;

/// <summary>
/// Cuenta bancaria (CLABE) del vendedor registrada en Pey; los cobros del vendedor se depositan ahí. Hoy una por
/// vendedor (el historial y el cambio de cuenta llegan en la Fase 4).
/// </summary>
public class BankAccount : Entity
{
    public const int HolderNameMaxLength = PeyLimits.BankAccountTextMaxLength;
    public const int AliasMaxLength = Merchant.BusinessNameMaxLength;
    public const int PeyIdMaxLength = 64;
    public const int ErrorMessageMaxLength = 1000;

    public int MerchantId { get; set; }

    /// <summary>Nombre legal del titular, como aparece en el banco (<c>name</c> en Pey).</summary>
    public string HolderName { get; set; } = null!;

    /// <summary>CLABE de 18 dígitos (normalizada). No mostrarla completa: <see cref="Clabe.Mask"/>.</summary>
    public string Clabe { get; set; } = null!;

    /// <summary>Nombre del negocio (<c>alias</c> en Pey).</summary>
    public string Alias { get; set; } = null!;

    /// <summary>Persona física o moral.</summary>
    public PeyEntity Entity { get; set; }

    /// <summary>Id de la cuenta en Pey (ULID); es el <c>bankAccountId</c> de cada cobro.</summary>
    public string PeyBankAccountId { get; set; } = null!;

    public PeyCodiStatus CodiStatus { get; set; }

    /// <summary>Motivo del rechazo (<c>errorMessage</c> de Pey); solo con <see cref="PeyCodiStatus.Error"/>.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Último envío a Pey (alta o reintento).</summary>
    public DateTimeOffset SubmittedAt { get; set; }

    /// <summary>Último cambio de <see cref="CodiStatus"/>.</summary>
    public DateTimeOffset StatusUpdatedAt { get; set; }

    /// <summary>
    /// Última lectura del estatus en Pey (respuesta del alta o <c>GET /bank-account/{id}</c> desde el botón
    /// "Verificar estatus" o el sondeo). El sondeo revisa primero las que llevan más tiempo sin leerse.
    /// </summary>
    public DateTimeOffset? StatusCheckedAt { get; set; }

    /// <summary>Banxico aún la revisa (<c>PENDING</c> o <c>PROCESSING</c>): es lo que consultan el botón y el sondeo.</summary>
    public bool IsInReview => CodiStatus is PeyCodiStatus.Pending or PeyCodiStatus.Processing;

    /// <summary>Alta recién aceptada por Pey.</summary>
    public static BankAccount Submit(
        int merchantId, string holderName, string clabe, string alias, PeyEntity entity,
        string peyBankAccountId, PeyCodiStatus codiStatus, DateTimeOffset now)
    {
        var account = new BankAccount { MerchantId = merchantId };
        account.Resubmit(holderName, clabe, alias, entity, peyBankAccountId, codiStatus, now);
        return account;
    }

    /// <summary>Reintento tras un rechazo: nuevos datos y nuevo id de Pey sobre la misma fila.</summary>
    public void Resubmit(
        string holderName, string clabe, string alias, PeyEntity entity,
        string peyBankAccountId, PeyCodiStatus codiStatus, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(peyBankAccountId);
        if (!BankAccounts.Clabe.IsValid(clabe))
        {
            throw new ArgumentException("CLABE inválida.", nameof(clabe));
        }

        HolderName = holderName.Trim();
        Clabe = clabe;
        Alias = alias.Trim();
        Entity = entity;
        PeyBankAccountId = peyBankAccountId;
        CodiStatus = codiStatus;
        ErrorMessage = null;
        SubmittedAt = now;
        StatusUpdatedAt = now;
        StatusCheckedAt = now; // la respuesta del alta ya es una lectura del estatus
    }

    /// <summary>
    /// Resultado de consultar el estatus en Pey (botón o sondeo): registra la lectura aunque no cambie nada.
    /// El webhook usa <see cref="UpdateStatus"/>, porque no es una consulta. Devuelve <c>true</c> si cambió el estatus.
    /// </summary>
    public bool RecordStatusCheck(PeyCodiStatus codiStatus, string? errorMessage, DateTimeOffset now)
    {
        StatusCheckedAt = now;
        return UpdateStatus(codiStatus, errorMessage, now);
    }

    /// <summary>
    /// Cuánto falta para poder volver a consultar a Pey con un intervalo mínimo entre lecturas; <c>null</c> si ya se puede.
    /// </summary>
    public TimeSpan? TimeUntilNextStatusCheck(DateTimeOffset now, TimeSpan minInterval)
    {
        if (StatusCheckedAt is not { } checkedAt)
        {
            return null;
        }

        var wait = checkedAt + minInterval - now;
        return wait > TimeSpan.Zero ? wait : null;
    }

    /// <summary>Estatus reportado por Pey (webhook o sondeo). Devuelve <c>true</c> si cambió algo.</summary>
    public bool UpdateStatus(PeyCodiStatus codiStatus, string? errorMessage, DateTimeOffset now)
    {
        var message = codiStatus == PeyCodiStatus.Error ? Truncate(errorMessage) : null;
        if (codiStatus == CodiStatus && message == ErrorMessage)
        {
            return false;
        }

        CodiStatus = codiStatus;
        ErrorMessage = message;
        StatusUpdatedAt = now;
        return true;
    }

    private static string? Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= ErrorMessageMaxLength ? value : value[..ErrorMessageMaxLength];
}
