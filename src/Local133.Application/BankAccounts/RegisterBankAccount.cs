using FluentValidation;
using Local133.Domain.BankAccounts;
using Local133.Domain.Pey;

namespace Local133.Application.BankAccounts;

/// <summary>El vendedor (por su usuario de Identity) registra la CLABE donde recibirá sus cobros.</summary>
/// <param name="Alias">Nombre del negocio (se envía a Pey como <c>alias</c>).</param>
public sealed record RegisterBankAccountCommand(string UserId, PeyEntity Entity, string HolderName, string Clabe, string Alias);

/// <summary>Validación en servidor (la del formulario en cliente es solo de apoyo).</summary>
public sealed class RegisterBankAccountCommandValidator : AbstractValidator<RegisterBankAccountCommand>
{
    public RegisterBankAccountCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Entity).IsInEnum().WithMessage("Elige el tipo de titular.");
        RuleFor(c => c.HolderName)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Escribe el nombre del titular.")
            .Must(v => v is null || v.Trim().Length <= BankAccount.HolderNameMaxLength)
            .WithMessage($"El nombre del titular admite hasta {BankAccount.HolderNameMaxLength} caracteres.");
        RuleFor(c => c.Alias)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Escribe el nombre de tu negocio.")
            .Must(v => v is null || v.Trim().Length <= BankAccount.AliasMaxLength)
            .WithMessage($"El nombre del negocio admite hasta {BankAccount.AliasMaxLength} caracteres.");
        RuleFor(c => c.Clabe)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Escribe tu CLABE.")
            .Must(v => Clabe.IsValid(Clabe.Normalize(v))).WithMessage(ClabeMessages.Invalid)
            .When(c => !string.IsNullOrWhiteSpace(c.Clabe), ApplyConditionTo.CurrentValidator);
    }
}

public static class ClabeMessages
{
    public const string Invalid = "La CLABE debe tener 18 dígitos y un dígito verificador válido. Revísala en tu app o estado de cuenta del banco.";

    public const string RejectedByGateway = "El banco no aceptó esta CLABE. Revisa el número o confirma que tu banco tenga CoDi.";
}

/// <summary>Resultado del alta de la cuenta bancaria.</summary>
public abstract record RegisterBankAccountResult
{
    private RegisterBankAccountResult()
    {
    }

    /// <summary>Pey aceptó la cuenta; el vendedor quedó en revisión (o activo, si Pey ya la activó).</summary>
    public sealed record Submitted(PeyCodiStatus CodiStatus) : RegisterBankAccountResult;

    /// <summary>Datos inválidos; llave = propiedad del comando (<c>Clabe</c>, <c>HolderName</c>…) o vacía para un error general.</summary>
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : RegisterBankAccountResult;

    /// <summary>Ya tiene una cuenta enviada (o su estatus no permite registrar otra).</summary>
    public sealed record NotAllowed : RegisterBankAccountResult;

    /// <summary>Falla de la pasarela; <see cref="Message"/> se puede mostrar al vendedor.</summary>
    public sealed record GatewayFailed(string Message) : RegisterBankAccountResult;
}

/// <summary>Caso de uso del alta (implementado en Infrastructure: EF Core + <c>IPaymentGateway</c>).</summary>
public interface IBankAccountRegistration
{
    Task<RegisterBankAccountResult> RegisterAsync(RegisterBankAccountCommand command, CancellationToken cancellationToken = default);
}
