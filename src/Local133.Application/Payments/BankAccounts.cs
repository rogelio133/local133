using Local133.Domain.Pey;

namespace Local133.Application.Payments;

/// <summary>Alta de una cuenta bancaria en Pey.</summary>
/// <param name="Alias">Nombre corto (se usa el nombre del negocio). 1–255 caracteres.</param>
/// <param name="HolderName">Nombre legal completo del titular. 1–255 caracteres.</param>
/// <param name="Clabe">CLABE de 18 dígitos.</param>
/// <param name="Entity">Persona física o moral.</param>
public sealed record RegisterBankAccountRequest(string Alias, string HolderName, string Clabe, PeyEntity Entity);

/// <summary>Respuesta del alta: id de Pey (ULID) y estatus inicial (normalmente <c>Pending</c>).</summary>
public sealed record RegisteredBankAccount(string Id, PeyCodiStatus CodiStatus);

/// <summary>Cuenta bancaria registrada en Pey.</summary>
/// <param name="ErrorMessage">Motivo del rechazo; solo cuando <see cref="CodiStatus"/> es <c>Error</c>.</param>
/// <param name="TaxId">RFC; Pey solo lo devuelve si se dio al registrar (nuestro alta no lo envía).</param>
public sealed record GatewayBankAccount(
    string Id,
    string? Alias,
    string? HolderName,
    string? Clabe,
    PeyEntity? Entity,
    string? TaxId,
    PeyCodiStatus CodiStatus,
    string? ErrorMessage,
    DateTimeOffset? CreatedAt);
