using Local133.Application.Payments;
using Local133.Domain.Pey;

namespace Local133.Infrastructure.Pey;

/// <summary>
/// Reglas de Pey que se validan antes de llamar (un error aquí es un bug de quien llama: los formularios
/// deben validar antes). Lanza <see cref="ArgumentException"/>.
/// </summary>
internal static class PeyRequestGuard
{
    public static void Validate(RegisterBankAccountRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireText(request.Alias, PeyLimits.BankAccountTextMaxLength, nameof(request.Alias));
        RequireText(request.HolderName, PeyLimits.BankAccountTextMaxLength, nameof(request.HolderName));
        if (!IsDigits(request.Clabe, PeyLimits.ClabeLength))
        {
            throw new ArgumentException($"La CLABE debe tener {PeyLimits.ClabeLength} dígitos.", nameof(request.Clabe));
        }

        if (!Enum.IsDefined(request.Entity))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Entity), request.Entity, "Tipo de titular inválido.");
        }
    }

    public static void Validate(CreateCodiQrRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Amount), request.Amount, "El monto del QR no puede ser negativo.");
        }

        RequireCents(request.Amount, nameof(request.Amount));
        if (request.ExpirationSeconds != PeyLimits.NoExpiration && request.ExpirationSeconds < PeyLimits.MinExpirationSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ExpirationSeconds), request.ExpirationSeconds,
                $"La expiración del QR debe ser {PeyLimits.NoExpiration} (no expira) o al menos {PeyLimits.MinExpirationSeconds} segundos.");
        }

        ValidateCommon(request.NumRef, request.BankAccountId, request.Metadata);
    }

    public static void Validate(CreateCodiPushNotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0 || request.Amount > PeyLimits.MaxPushAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Amount), request.Amount,
                $"El monto del cobro por celular debe ser mayor a 0 y hasta {PeyLimits.MaxPushAmount:N0}.");
        }

        RequireCents(request.Amount, nameof(request.Amount));
        if (!IsDigits(request.PhoneNumber, PeyLimits.PhoneNumberLength))
        {
            throw new ArgumentException($"El celular debe tener {PeyLimits.PhoneNumberLength} dígitos.", nameof(request.PhoneNumber));
        }

        if (request.ExpirationSeconds < PeyLimits.MinExpirationSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ExpirationSeconds), request.ExpirationSeconds,
                $"La expiración del cobro por celular debe ser de al menos {PeyLimits.MinExpirationSeconds} segundos.");
        }

        ValidateCommon(request.NumRef, request.BankAccountId, request.Metadata);
    }

    public static void Validate(CreatePaymentLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Amount), request.Amount, "El monto del link de pago debe ser mayor a 0.");
        }

        RequireCents(request.Amount, nameof(request.Amount));
        RequireHttpsUrl(request.SuccessUrl, nameof(request.SuccessUrl));
        RequireHttpsUrl(request.BackUrl, nameof(request.BackUrl));
        if (request.ClientReferenceId is not null
            && (string.IsNullOrWhiteSpace(request.ClientReferenceId) || request.ClientReferenceId.Length > PeyLimits.MaxClientReferenceIdLength))
        {
            throw new ArgumentException(
                $"La referencia del link debe tener entre 1 y {PeyLimits.MaxClientReferenceIdLength} caracteres (o ir en null).",
                nameof(request.ClientReferenceId));
        }

        ValidateMetadata(request.Metadata);
    }

    public static void RequireId(string? id, string paramName)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("El identificador es obligatorio.", paramName);
        }
    }

    private static void ValidateCommon(int? numRef, string? bankAccountId, IReadOnlyDictionary<string, string>? metadata)
    {
        if (numRef is < 0 or > PeyLimits.MaxNumRef)
        {
            throw new ArgumentOutOfRangeException(nameof(numRef), numRef, "La referencia numérica debe tener hasta 7 dígitos.");
        }

        if (bankAccountId is not null && string.IsNullOrWhiteSpace(bankAccountId))
        {
            throw new ArgumentException("El id de la cuenta bancaria no puede ir vacío (usa null para la cuenta de la plataforma).", nameof(bankAccountId));
        }

        ValidateMetadata(metadata);
    }

    private static void ValidateMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        if (metadata.Count > PeyLimits.MaxMetadataPairs)
        {
            throw new ArgumentException($"La metadata admite hasta {PeyLimits.MaxMetadataPairs} pares.", nameof(metadata));
        }

        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrEmpty(key) || key.Length > PeyLimits.MaxMetadataKeyLength)
            {
                throw new ArgumentException($"Llave de metadata inválida '{key}' (1–{PeyLimits.MaxMetadataKeyLength} caracteres).", nameof(metadata));
            }

            if (value is null || value.Length > PeyLimits.MaxMetadataValueLength)
            {
                throw new ArgumentException($"El valor de metadata '{key}' debe tener hasta {PeyLimits.MaxMetadataValueLength} caracteres.", nameof(metadata));
            }
        }
    }

    private static void RequireText(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
        {
            throw new ArgumentException($"Debe tener entre 1 y {maxLength} caracteres.", paramName);
        }
    }

    private static void RequireCents(decimal amount, string paramName)
    {
        if (decimal.Round(amount, PeyLimits.AmountDecimals) != amount)
        {
            throw new ArgumentException($"El monto admite hasta {PeyLimits.AmountDecimals} decimales.", paramName);
        }
    }

    private static void RequireHttpsUrl(Uri? url, string paramName)
    {
        if (url is null || !url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Pey exige una URL absoluta con HTTPS.", paramName);
        }
    }

    private static bool IsDigits(string? value, int length) =>
        value is not null && value.Length == length && value.All(char.IsAsciiDigit);
}
