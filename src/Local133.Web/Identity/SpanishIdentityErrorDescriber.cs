using Microsoft.AspNetCore.Identity;

namespace Local133.Web.Identity;

/// <summary>Mensajes de error de Identity en español.</summary>
public class SpanishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Error(nameof(DefaultError), "Ocurrió un error desconocido.");
    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), "Otro proceso modificó los datos al mismo tiempo. Intenta de nuevo.");
    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "La contraseña es incorrecta.");
    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "La liga no es válida o ya expiró.");
    public override IdentityError RecoveryCodeRedemptionFailed() => Error(nameof(RecoveryCodeRedemptionFailed), "El código de recuperación no es válido.");
    public override IdentityError LoginAlreadyAssociated() => Error(nameof(LoginAlreadyAssociated), "Esa cuenta externa ya está ligada a otro usuario.");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), $"El usuario '{userName}' no es válido.");
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), $"El correo '{email}' no es válido.");
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), $"Ya existe una cuenta con el correo '{userName}'.");
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), $"Ya existe una cuenta con el correo '{email}'.");
    public override IdentityError InvalidRoleName(string? role) => Error(nameof(InvalidRoleName), $"El rol '{role}' no es válido.");
    public override IdentityError DuplicateRoleName(string role) => Error(nameof(DuplicateRoleName), $"El rol '{role}' ya existe.");
    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), "Esta cuenta ya tiene contraseña.");
    public override IdentityError UserLockoutNotEnabled() => Error(nameof(UserLockoutNotEnabled), "El bloqueo no está habilitado para esta cuenta.");
    public override IdentityError UserAlreadyInRole(string role) => Error(nameof(UserAlreadyInRole), $"El usuario ya tiene el rol '{role}'.");
    public override IdentityError UserNotInRole(string role) => Error(nameof(UserNotInRole), $"El usuario no tiene el rol '{role}'.");
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"La contraseña debe tener al menos {length} caracteres.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), $"La contraseña debe tener al menos {uniqueChars} caracteres distintos.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "La contraseña debe tener al menos un símbolo (por ejemplo ! o #).");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "La contraseña debe tener al menos un número (0-9).");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "La contraseña debe tener al menos una minúscula (a-z).");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "La contraseña debe tener al menos una mayúscula (A-Z).");

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
