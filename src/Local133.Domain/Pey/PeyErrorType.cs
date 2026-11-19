namespace Local133.Domain.Pey;

/// <summary>
/// Valor de <c>error.type</c> en las respuestas de error de Pey
/// (<c>{ "error": { "message", "type", "requestId" } }</c>). En el cable va en mayúsculas con guion
/// bajo: <see cref="InvalidClabe"/> = <c>INVALID_CLABE</c>.
/// </summary>
public enum PeyErrorType
{
    /// <summary>Tipo ausente o que Pey agregó después: se clasifica por código HTTP.</summary>
    Unknown,

    InvalidRequest,
    InvalidAmount,
    InvalidParameter,

    /// <summary>Plural: lo usan los listados de transacciones (p. ej. <c>nextToken</c> inválido).</summary>
    InvalidParameters,

    IncompleteParameters,
    InvalidClabe,
    InvalidState,
    BankAccountNotFound,
    NotFound,
    Forbidden,
    AuthError,
    BadRequest,
    RequestParsingError,
    PeyConsistencyError,
    ConcurrentUpdate,
    ServiceUnavailable,
    FlagsUnavailable,
    InternalServerError,

    /// <summary>Links de pago: <c>successUrl</c>/<c>backUrl</c> inválidas (deben ser HTTPS).</summary>
    InvalidUrl,

    InvalidHasTip,
    InvalidClientReferenceId,

    /// <summary>Links de pago: solo se puede actualizar uno en <c>pending</c> (409).</summary>
    PaymentLinkNotPending,
}
