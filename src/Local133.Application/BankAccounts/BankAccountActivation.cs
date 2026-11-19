namespace Local133.Application.BankAccounts;

/// <summary>
/// Avisa al vendedor que su cuenta bancaria quedó activa y ya puede cobrar. Se llama después de guardar la
/// transición del vendedor a <c>Active</c> (alta, webhook <c>bank.account.updated</c>, sondeo o "Verificar estatus").
/// <para>
/// Es idempotente (un solo aviso por vendedor, aunque dos caminos vean la activación a la vez) y nunca lanza: el
/// aviso no debe tumbar el webhook ni el job, que ya guardaron el cambio.
/// </para>
/// </summary>
public interface IBankAccountActivationNotifier
{
    Task NotifyActivatedAsync(int merchantId, CancellationToken cancellationToken = default);
}
