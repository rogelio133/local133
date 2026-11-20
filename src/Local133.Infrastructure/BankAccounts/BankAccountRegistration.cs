using FluentValidation;
using Local133.Application.BankAccounts;
using Local133.Application.Payments;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Infrastructure.Persistence;
using Local133.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.BankAccounts;

/// <summary>
/// Alta de la CLABE del vendedor: valida → <c>POST /bank-account</c> → guarda la cuenta y el nuevo estatus del
/// vendedor juntos. Si Pey falla no se guarda nada.
/// </summary>
internal sealed class BankAccountRegistration(
    AppDbContext db,
    IPaymentGateway gateway,
    IValidator<RegisterBankAccountCommand> validator,
    IBankAccountActivationNotifier activationNotifier,
    TimeProvider clock,
    ILogger<BankAccountRegistration> logger) : IBankAccountRegistration
{
    private const string GenericFailure =
        "No pudimos registrar tu cuenta por un problema de nuestro lado. Intenta de nuevo más tarde.";

    private const string UnavailableFailure =
        "No pudimos comunicarnos con el servicio de pagos. Intenta de nuevo en unos minutos.";

    public async Task<RegisterBankAccountResult> RegisterAsync(
        RegisterBankAccountCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return new RegisterBankAccountResult.Invalid(validation.ToDictionary().AsReadOnly());
        }

        var merchant = await db.Merchants.SingleOrDefaultAsync(m => m.UserId == command.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"El usuario {command.UserId} no tiene Merchant.");
        // Explícito por MerchantId y sin filtro: la decisión de no duplicar el alta no depende del claim de la sesión.
        var current = await db.BankAccounts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(b => b.MerchantId == merchant.Id, cancellationToken);
        if (!merchant.CanSubmitBankAccount(current))
        {
            return new RegisterBankAccountResult.NotAllowed();
        }

        var clabe = Clabe.Normalize(command.Clabe);
        var holderName = command.HolderName.Trim();
        var alias = command.Alias.Trim();

        RegisteredBankAccount registered;
        try
        {
            registered = await gateway.RegisterBankAccountAsync(
                new RegisterBankAccountRequest(alias, holderName, clabe, command.Entity), cancellationToken);
        }
        catch (InvalidClabeException ex)
        {
            logger.LogInformation("Pey rechazó la CLABE {Clabe} del vendedor {MerchantId}; requestId {RequestId}",
                Clabe.Mask(clabe), merchant.Id, ex.RequestId);
            return Invalid(nameof(RegisterBankAccountCommand.Clabe), ClabeMessages.RejectedByGateway);
        }
        catch (PaymentGatewayValidationException ex)
        {
            logger.LogWarning("Pey rechazó los datos de la cuenta del vendedor {MerchantId} ({ErrorType}); requestId {RequestId}: {GatewayMessage}",
                merchant.Id, ex.ErrorType, ex.RequestId, ex.GatewayMessage);
            return Invalid(string.Empty, "El servicio de pagos no aceptó los datos. Revisa el nombre del titular y la CLABE.");
        }
        catch (PaymentGatewayUnavailableException ex)
        {
            // En un POST sin respuesta el resultado es incierto: Pey pudo haber registrado la cuenta.
            logger.LogWarning(ex, "Alta de cuenta bancaria del vendedor {MerchantId} sin confirmar (Pey no disponible); requestId {RequestId}",
                merchant.Id, ex.RequestId);
            return new RegisterBankAccountResult.GatewayFailed(UnavailableFailure);
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogError(ex, "Pey falló al registrar la cuenta del vendedor {MerchantId} ({ErrorType}); requestId {RequestId}",
                merchant.Id, ex.ErrorType, ex.RequestId);
            return new RegisterBankAccountResult.GatewayFailed(GenericFailure);
        }
        catch (InvalidOperationException ex)
        {
            // Configuración (p. ej. falta Pey:ApiKey).
            logger.LogError(ex, "No se pudo llamar a Pey para registrar la cuenta del vendedor {MerchantId}", merchant.Id);
            return new RegisterBankAccountResult.GatewayFailed(GenericFailure);
        }

        var now = clock.GetUtcNow();
        if (current is null)
        {
            db.BankAccounts.Add(BankAccount.Submit(
                merchant.Id, holderName, clabe, alias, command.Entity, registered.Id, registered.CodiStatus, now));
        }
        else
        {
            current.Resubmit(holderName, clabe, alias, command.Entity, registered.Id, registered.CodiStatus, now);
        }

        var activated = merchant.ApplyBankAccountStatus(registered.CodiStatus) && merchant.Status == MerchantStatus.Active;
        if (string.IsNullOrWhiteSpace(merchant.BusinessName))
        {
            merchant.BusinessName = alias;
        }

        // Pey la activó en el alta: la prueba de Pro empieza ya, en el mismo guardado.
        if (activated)
        {
            await TrialStarter.EnsureTrialAsync(db, merchant.Id, now, cancellationToken);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Doble envío simultáneo: el índice único de MerchantId dejó pasar solo uno; esta alta quedó huérfana en Pey.
            logger.LogError(ex, "No se guardó la cuenta Pey {PeyBankAccountId} del vendedor {MerchantId} (¿doble envío?); queda huérfana en Pey",
                registered.Id, merchant.Id);
            return new RegisterBankAccountResult.NotAllowed();
        }

        logger.LogInformation("Vendedor {MerchantId} registró la CLABE {Clabe} en Pey ({PeyBankAccountId}, {CodiStatus})",
            merchant.Id, Clabe.Mask(clabe), registered.Id, registered.CodiStatus);
        if (activated)
        {
            // Pey la activó en el alta (p. ej. la CLABE de prueba): el mismo aviso que con el webhook o el sondeo.
            await activationNotifier.NotifyActivatedAsync(merchant.Id, cancellationToken);
        }

        return new RegisterBankAccountResult.Submitted(registered.CodiStatus);
    }

    private static RegisterBankAccountResult.Invalid Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
