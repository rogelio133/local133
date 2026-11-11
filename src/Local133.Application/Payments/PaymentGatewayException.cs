using Local133.Domain.Pey;

namespace Local133.Application.Payments;

/// <summary>
/// La pasarela respondió con error o con algo que no se pudo interpretar. Las subclases agrupan los
/// <c>error.type</c> de Pey por lo que debe hacer quien llama.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(
        string message,
        PeyErrorType errorType = PeyErrorType.Unknown,
        int? statusCode = null,
        string? requestId = null,
        string? gatewayMessage = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorType = errorType;
        StatusCode = statusCode;
        RequestId = requestId;
        GatewayMessage = gatewayMessage;
    }

    /// <summary><c>error.type</c> de Pey (<see cref="PeyErrorType.Unknown"/> si no vino o no se reconoce).</summary>
    public PeyErrorType ErrorType { get; }

    /// <summary>Código HTTP; <c>null</c> si no hubo respuesta (red, timeout).</summary>
    public int? StatusCode { get; }

    /// <summary><c>error.requestId</c> de Pey, para soporte.</summary>
    public string? RequestId { get; }

    /// <summary>Mensaje original de Pey (en inglés); no mostrar tal cual al vendedor.</summary>
    public string? GatewayMessage { get; }

    /// <summary>Reintentar más tarde puede funcionar.</summary>
    public virtual bool IsTransient => false;
}

/// <summary>
/// Petición rechazada por datos inválidos (<c>INVALID_REQUEST</c>, <c>INVALID_AMOUNT</c>, <c>INVALID_PARAMETER(S)</c>,
/// <c>INCOMPLETE_PARAMETERS</c>, <c>BAD_REQUEST</c>, <c>REQUEST_PARSING_ERROR</c>).
/// </summary>
public class PaymentGatewayValidationException(
    string message, PeyErrorType errorType, int? statusCode, string? requestId, string? gatewayMessage)
    : PaymentGatewayException(message, errorType, statusCode, requestId, gatewayMessage);

/// <summary><c>INVALID_CLABE</c>: dígito verificador incorrecto o el banco no tiene CoDi.</summary>
public sealed class InvalidClabeException(string message, int? statusCode, string? requestId, string? gatewayMessage)
    : PaymentGatewayValidationException(message, PeyErrorType.InvalidClabe, statusCode, requestId, gatewayMessage);

/// <summary><c>NOT_FOUND</c> / <c>BANK_ACCOUNT_NOT_FOUND</c>: no existe o pertenece a otra API key.</summary>
public sealed class PaymentGatewayNotFoundException(
    string message, PeyErrorType errorType, int? statusCode, string? requestId, string? gatewayMessage)
    : PaymentGatewayException(message, errorType, statusCode, requestId, gatewayMessage);

/// <summary><c>AUTH_ERROR</c> / <c>FORBIDDEN</c> o 401/403: API key inválida o sin permisos (error de configuración).</summary>
public sealed class PaymentGatewayAuthenticationException(
    string message, PeyErrorType errorType, int? statusCode, string? requestId, string? gatewayMessage)
    : PaymentGatewayException(message, errorType, statusCode, requestId, gatewayMessage);

/// <summary>
/// La operación no procede en el estado actual (<c>INVALID_STATE</c>: p. ej. API key sin CoDi o cuenta no
/// <c>ACTIVE</c>; <c>PEY_CONSISTENCY_ERROR</c>; <c>CONCURRENT_UPDATE</c>, este último reintentable).
/// </summary>
public sealed class PaymentGatewayStateException(
    string message, PeyErrorType errorType, int? statusCode, string? requestId, string? gatewayMessage)
    : PaymentGatewayException(message, errorType, statusCode, requestId, gatewayMessage)
{
    public override bool IsTransient => ErrorType == PeyErrorType.ConcurrentUpdate;
}

/// <summary>
/// Pey no está disponible: <c>SERVICE_UNAVAILABLE</c>, <c>FLAGS_UNAVAILABLE</c>, <c>INTERNAL_SERVER_ERROR</c>, otro 5xx/429,
/// error de red o timeout (ya agotados los reintentos). En un alta (POST) el resultado es incierto:
/// Pey pudo haberla procesado.
/// </summary>
public sealed class PaymentGatewayUnavailableException(
    string message,
    PeyErrorType errorType = PeyErrorType.Unknown,
    int? statusCode = null,
    string? requestId = null,
    string? gatewayMessage = null,
    Exception? innerException = null)
    : PaymentGatewayException(message, errorType, statusCode, requestId, gatewayMessage, innerException)
{
    public override bool IsTransient => true;
}
