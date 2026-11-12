using Local133.Application.BankAccounts;
using Local133.Application.Payments;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.BankAccounts;

/// <summary>
/// Lee el estatus CoDi de la cuenta en Pey y lo aplica a la cuenta y al vendedor juntos. Si Pey falla no se guarda
/// nada. Sin dependencia de la sesión: sirve igual para el botón del vendedor y para el job de sondeo (2.3).
/// </summary>
internal sealed class BankAccountStatusSync(
    AppDbContext db,
    IPaymentGateway gateway,
    TimeProvider clock,
    ILogger<BankAccountStatusSync> logger) : IBankAccountStatusSync
{
    private const string GenericFailure =
        "No pudimos consultar el estatus de tu cuenta por un problema de nuestro lado. Intenta de nuevo más tarde.";

    private const string UnavailableFailure =
        "No pudimos comunicarnos con el servicio de pagos. Intenta de nuevo en unos minutos.";

    public async Task<BankAccountStatusSyncResult> SyncAsync(
        int bankAccountId, TimeSpan? minInterval = null, CancellationToken cancellationToken = default)
    {
        // Sin filtro multi-tenant y por llaves explícitas: el job corre sin vendedor en sesión, y el sitio ya
        // resolvió la cuenta del vendedor con el filtro antes de llamar aquí.
        var account = await db.BankAccounts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(b => b.Id == bankAccountId, cancellationToken);
        if (account is null)
        {
            return new BankAccountStatusSyncResult.NotFound();
        }

        var merchant = await db.Merchants.IgnoreQueryFilters()
            .SingleAsync(m => m.Id == account.MerchantId, cancellationToken);

        if (minInterval is { } interval && account.TimeUntilNextStatusCheck(clock.GetUtcNow(), interval) is { } wait)
        {
            return new BankAccountStatusSyncResult.TooSoon(wait);
        }

        GatewayBankAccount remote;
        try
        {
            remote = await gateway.GetBankAccountAsync(account.PeyBankAccountId, cancellationToken);
        }
        catch (PaymentGatewayUnavailableException ex)
        {
            logger.LogWarning(ex, "No se pudo consultar la cuenta Pey {PeyBankAccountId} del vendedor {MerchantId} (Pey no disponible); requestId {RequestId}",
                account.PeyBankAccountId, merchant.Id, ex.RequestId);
            return new BankAccountStatusSyncResult.GatewayFailed(UnavailableFailure);
        }
        catch (PaymentGatewayException ex)
        {
            // NOT_FOUND / BANK_ACCOUNT_NOT_FOUND aquí es una inconsistencia con Pey: requiere revisión.
            logger.LogError(ex, "Pey falló al consultar la cuenta {PeyBankAccountId} del vendedor {MerchantId} ({ErrorType}); requestId {RequestId}",
                account.PeyBankAccountId, merchant.Id, ex.ErrorType, ex.RequestId);
            return new BankAccountStatusSyncResult.GatewayFailed(GenericFailure);
        }
        catch (InvalidOperationException ex)
        {
            // Configuración (p. ej. falta Pey:ApiKey).
            logger.LogError(ex, "No se pudo llamar a Pey para consultar la cuenta del vendedor {MerchantId}", merchant.Id);
            return new BankAccountStatusSyncResult.GatewayFailed(GenericFailure);
        }

        var previous = account.CodiStatus;
        var changed = Apply(account, merchant, remote);
        await db.SaveChangesAsync(cancellationToken);

        if (changed)
        {
            logger.LogInformation("Cuenta {Clabe} ({PeyBankAccountId}) del vendedor {MerchantId}: {Previous} → {Current}; vendedor {MerchantStatus}",
                Clabe.Mask(account.Clabe), account.PeyBankAccountId, merchant.Id, previous, account.CodiStatus, merchant.Status);
        }

        return new BankAccountStatusSyncResult.Synced(previous, account.CodiStatus, merchant.Status, changed);
    }

    /// <summary>
    /// Transición común (consulta y, en 2.3, webhook <c>bank.account.updated</c>): estatus de la cuenta + estatus del
    /// vendedor. Devuelve <c>true</c> si cambió alguno de los dos.
    /// </summary>
    private bool Apply(BankAccount account, Merchant merchant, GatewayBankAccount remote)
    {
        var accountChanged = account.RecordStatusCheck(remote.CodiStatus, remote.ErrorMessage, clock.GetUtcNow());
        var merchantChanged = merchant.ApplyBankAccountStatus(remote.CodiStatus);
        return accountChanged || merchantChanged;
    }
}
