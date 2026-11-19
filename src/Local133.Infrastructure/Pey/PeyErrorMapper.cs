using Local133.Application.Payments;
using Local133.Domain.Pey;

namespace Local133.Infrastructure.Pey;

/// <summary>Convierte una respuesta de error de Pey en la excepción de <see cref="PaymentGatewayException"/> que corresponde.</summary>
internal static class PeyErrorMapper
{
    public static PaymentGatewayException Map(int statusCode, PeyErrorType type, string? requestId, string? gatewayMessage)
    {
        var typeName = type == PeyErrorType.Unknown ? "sin tipo" : PeyJson.ToWireName(type);
        var message = $"Pey respondió HTTP {statusCode} ({typeName}): {gatewayMessage ?? "sin mensaje"}";

        return type switch
        {
            PeyErrorType.InvalidClabe => new InvalidClabeException(message, statusCode, requestId, gatewayMessage),

            PeyErrorType.InvalidRequest or PeyErrorType.InvalidAmount or PeyErrorType.InvalidParameter
                or PeyErrorType.InvalidParameters or PeyErrorType.IncompleteParameters or PeyErrorType.BadRequest
                or PeyErrorType.RequestParsingError or PeyErrorType.InvalidUrl or PeyErrorType.InvalidHasTip
                or PeyErrorType.InvalidClientReferenceId
                => new PaymentGatewayValidationException(message, type, statusCode, requestId, gatewayMessage),

            PeyErrorType.NotFound or PeyErrorType.BankAccountNotFound
                => new PaymentGatewayNotFoundException(message, type, statusCode, requestId, gatewayMessage),

            PeyErrorType.AuthError or PeyErrorType.Forbidden
                => new PaymentGatewayAuthenticationException(message, type, statusCode, requestId, gatewayMessage),

            PeyErrorType.InvalidState or PeyErrorType.PeyConsistencyError or PeyErrorType.ConcurrentUpdate
                or PeyErrorType.PaymentLinkNotPending
                => new PaymentGatewayStateException(message, type, statusCode, requestId, gatewayMessage),

            PeyErrorType.ServiceUnavailable or PeyErrorType.FlagsUnavailable or PeyErrorType.InternalServerError
                => new PaymentGatewayUnavailableException(message, type, statusCode, requestId, gatewayMessage),

            // Sin tipo reconocible (p. ej. respuesta del gateway de AWS o un tipo nuevo): por código HTTP.
            _ => statusCode switch
            {
                400 or 422 => new PaymentGatewayValidationException(message, type, statusCode, requestId, gatewayMessage),
                401 or 403 => new PaymentGatewayAuthenticationException(message, type, statusCode, requestId, gatewayMessage),
                404 => new PaymentGatewayNotFoundException(message, type, statusCode, requestId, gatewayMessage),
                409 => new PaymentGatewayStateException(message, type, statusCode, requestId, gatewayMessage),
                408 or 429 or >= 500 => new PaymentGatewayUnavailableException(message, type, statusCode, requestId, gatewayMessage),
                _ => new PaymentGatewayException(message, type, statusCode, requestId, gatewayMessage),
            },
        };
    }
}
