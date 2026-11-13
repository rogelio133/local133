using FluentValidation;
using Local133.Domain.Pey;
using Local133.Domain.Sales;

namespace Local133.Application.Sales;

/// <summary>El vendedor (por su usuario de Identity) cobra un monto capturado con un QR CoDi dinámico.</summary>
/// <param name="Concept">Concepto que escribe el vendedor; a Pey se envía como "{negocio} · {concepto}".</param>
public sealed record CreateDirectSaleCommand(string UserId, decimal Amount, string? Concept);

/// <summary>Validación en servidor (la del formulario en cliente es solo de apoyo).</summary>
public sealed class CreateDirectSaleCommandValidator : AbstractValidator<CreateDirectSaleCommand>
{
    public CreateDirectSaleCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Amount)
            .GreaterThanOrEqualTo(Sale.MinAmount).WithMessage(SaleAmountMessages.TooLow)
            .LessThanOrEqualTo(Sale.MaxAmount).WithMessage(SaleAmountMessages.TooHigh)
            .Must(a => decimal.Round(a, PeyLimits.AmountDecimals) == a).WithMessage(SaleAmountMessages.TooManyDecimals);
        RuleFor(c => c.Concept)
            .Cascade(CascadeMode.Stop)
            .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage(SaleConceptMessages.Required)
            .Must(c => c!.Trim().Length <= Sale.ConceptTextMaxLength).WithMessage(SaleConceptMessages.TooLong)
            .Must(Sale.IsValidConceptText).WithMessage(SaleConceptMessages.Invalid);
    }
}

public static class SaleConceptMessages
{
    public const string Required = "Escribe el concepto del cobro.";

    public const string TooLong = "El concepto admite hasta 100 caracteres.";

    // Espacios de no separación entre los signos: en móvil la lista no se parte en dos renglones.
    public const string Invalid = "Usa solo letras, números, espacios y estos signos: . , - # / ( )";
}

public static class SaleAmountMessages
{
    public const string Required = "Escribe el monto a cobrar.";

    public const string Invalid = "Escribe un monto válido, sin signos y con hasta 2 decimales (por ejemplo 150.50).";

    public const string TooLow = "El monto mínimo es $0.01.";

    public const string TooHigh = "El monto máximo es $999,999.99.";

    public const string TooManyDecimals = "El monto admite hasta 2 decimales.";
}

/// <summary>Resultado de crear la venta y su QR.</summary>
public abstract record CreateDirectSaleResult
{
    private CreateDirectSaleResult()
    {
    }

    /// <summary>Venta creada con su QR.</summary>
    public sealed record Created(int SaleId) : CreateDirectSaleResult;

    /// <summary>Datos inválidos; llave = propiedad del comando (<c>Amount</c>, <c>Concept</c>) o vacía para un error general.</summary>
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : CreateDirectSaleResult;

    /// <summary>El vendedor no puede cobrar (no está activo o su cuenta bancaria no está activa).</summary>
    public sealed record NotAllowed : CreateDirectSaleResult;

    /// <summary>Falla de la pasarela; <see cref="Message"/> se puede mostrar al vendedor.</summary>
    public sealed record GatewayFailed(string Message) : CreateDirectSaleResult;
}

/// <summary>Cobros del vendedor (implementado en Infrastructure: EF Core + <c>IPaymentGateway</c>).</summary>
public interface ISaleCharging
{
    Task<CreateDirectSaleResult> CreateDirectSaleAsync(CreateDirectSaleCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela una venta pendiente del vendedor. Solo es local: CoDi no permite anular el QR, así que si aun así llega
    /// un pago la venta queda pagada.
    /// </summary>
    Task<CancelSaleResult> CancelSaleAsync(CancelSaleCommand command, CancellationToken cancellationToken = default);
}
