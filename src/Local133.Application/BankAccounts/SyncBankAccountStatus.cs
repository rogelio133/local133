using Local133.Domain.Merchants;
using Local133.Domain.Pey;

namespace Local133.Application.BankAccounts;

/// <summary>
/// Consulta el estatus CoDi de una cuenta en Pey (<c>GET /bank-account/{id}</c>) y lo aplica a la cuenta y al
/// vendedor (<c>BankAccount.RecordStatusCheck</c> + <c>Merchant.ApplyBankAccountStatus</c>) en un solo guardado.
/// <para>
/// No depende de la sesión HTTP ni del claim del vendedor: lo usan el botón "Verificar estatus" de
/// <c>/app/cuenta-bancaria</c> y el job de sondeo (paso 2.3), que debe llamar a <see cref="SyncAsync"/> por cada
/// cuenta en revisión (<c>Pending</c>/<c>Processing</c>), ordenadas por <c>StatusCheckedAt</c>, cada una en su
/// propio scope de DI y sin <c>minInterval</c>.
/// </para>
/// </summary>
public interface IBankAccountStatusSync
{
    /// <param name="bankAccountId">Id de nuestra fila <c>BankAccounts</c> (no el id de Pey).</param>
    /// <param name="minInterval">
    /// Freno entre lecturas: si la última fue hace menos, responde <see cref="BankAccountStatusSyncResult.TooSoon"/>
    /// sin llamar a Pey. Lo pasa el botón (<see cref="BankAccountStatusChecks.ManualMinInterval"/>); el job, <c>null</c>.
    /// </param>
    Task<BankAccountStatusSyncResult> SyncAsync(int bankAccountId, TimeSpan? minInterval = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Estatus avisado por el webhook <c>bank.account.updated</c> (Pey solo lo manda al pasar a <c>ACTIVE</c>). No es
    /// una consulta: usa <c>BankAccount.UpdateStatus</c>, no toca <c>StatusCheckedAt</c> y no llama a Pey.
    /// </summary>
    /// <param name="peyBankAccountId">Id de la cuenta en Pey (<c>bankAccountId</c> del webhook).</param>
    /// <returns><see cref="BankAccountStatusSyncResult.Synced"/> o <see cref="BankAccountStatusSyncResult.NotFound"/>.</returns>
    Task<BankAccountStatusSyncResult> ApplyWebhookAsync(string peyBankAccountId, PeyCodiStatus codiStatus, CancellationToken cancellationToken = default);
}

public static class BankAccountStatusChecks
{
    /// <summary>Intervalo mínimo entre consultas manuales del vendedor, para no saturar a Pey.</summary>
    public static readonly TimeSpan ManualMinInterval = TimeSpan.FromMinutes(1);
}

/// <summary>Resultado de la consulta del estatus de la cuenta bancaria.</summary>
public abstract record BankAccountStatusSyncResult
{
    private BankAccountStatusSyncResult()
    {
    }

    /// <summary>Pey respondió; se guardó la lectura. <see cref="Changed"/>: cambió el estatus de la cuenta o del vendedor.</summary>
    public sealed record Synced(PeyCodiStatus Previous, PeyCodiStatus Current, MerchantStatus MerchantStatus, bool Changed)
        : BankAccountStatusSyncResult;

    /// <summary>No existe la cuenta indicada.</summary>
    public sealed record NotFound : BankAccountStatusSyncResult;

    /// <summary>Se consultó hace menos de <c>minInterval</c>; no se llamó a Pey.</summary>
    public sealed record TooSoon(TimeSpan RetryAfter) : BankAccountStatusSyncResult;

    /// <summary>Falla de la pasarela (no se guardó nada); <see cref="Message"/> se puede mostrar al vendedor.</summary>
    public sealed record GatewayFailed(string Message) : BankAccountStatusSyncResult;
}
