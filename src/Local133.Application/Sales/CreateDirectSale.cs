using FluentValidation;
using Local133.Domain.Pey;
using Local133.Domain.Sales;

namespace Local133.Application.Sales;

/// <summary>
/// El vendedor (por su usuario de Identity) cobra un monto capturado: con un QR CoDi dinámico o con una notificación
/// push al celular del pagador.
/// </summary>
/// <param name="Concept">Concepto que escribe el vendedor; a Pey se envía como "{negocio} · {concepto}".</param>
/// <param name="Method"><see cref="ChargeMethod.DynamicQr"/> o <see cref="ChargeMethod.PushNotification"/>.</param>
/// <param name="PhoneNumber">Celular del pagador (solo push); se normaliza con <see cref="PhoneNumber.Normalize"/>.</param>
public sealed record CreateDirectSaleCommand(
    string UserId,
    decimal Amount,
    string? Concept,
    ChargeMethod Method = ChargeMethod.DynamicQr,
    string? PhoneNumber = null);

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
        RuleFor(c => c.Concept).ValidConceptText();
        this.ChargeMethodRules(c => c.Method, c => c.PhoneNumber);
    }
}

/// <summary>Reglas comunes a los cobros por monto y con carrito.</summary>
public static class SaleChargeRules
{
    /// <summary>Concepto que escribe el vendedor (<see cref="Sale.IsValidConceptText"/>).</summary>
    public static IRuleBuilderOptions<T, string?> ValidConceptText<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage(SaleConceptMessages.Required)
            .Must(c => c!.Trim().Length <= Sale.ConceptTextMaxLength).WithMessage(SaleConceptMessages.TooLong)
            .Must(Sale.IsValidConceptText).WithMessage(SaleConceptMessages.Invalid);

    /// <summary>Método (QR o celular) y, con celular, el número del pagador.</summary>
    public static void ChargeMethodRules<T>(
        this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, ChargeMethod>> method,
        System.Linq.Expressions.Expression<Func<T, string?>> phoneNumber)
    {
        var getMethod = method.Compile();
        validator.RuleFor(method)
            .Must(m => m is ChargeMethod.DynamicQr or ChargeMethod.PushNotification).WithMessage(SalePhoneMessages.InvalidMethod);
        validator.When(c => getMethod(c) == ChargeMethod.PushNotification, () =>
            validator.RuleFor(phoneNumber)
                .Cascade(CascadeMode.Stop)
                .Must(p => !string.IsNullOrWhiteSpace(p)).WithMessage(SalePhoneMessages.Required)
                .Must(p => PhoneNumber.IsValid(PhoneNumber.Normalize(p))).WithMessage(SalePhoneMessages.Invalid));
    }
}

public static class SalePhoneMessages
{
    public const string Required = "Escribe el celular de tu cliente.";

    public const string Invalid = "Escribe un celular de 10 dígitos, por ejemplo 55 1234 5678.";

    public const string InvalidMethod = "Elige cómo cobrar: con QR o al celular.";
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

/// <summary>Resultado de crear la venta y su QR (o su cobro al celular), por monto o con carrito.</summary>
public abstract record CreateSaleResult
{
    private CreateSaleResult()
    {
    }

    /// <summary>Venta creada con su QR.</summary>
    public sealed record Created(int SaleId) : CreateSaleResult;

    /// <summary>
    /// Datos inválidos; llave = propiedad del comando (<c>Amount</c>, <c>Concept</c>, <c>PhoneNumber</c>, <c>Lines</c>,
    /// <c>ExpectedTotal</c>…) o vacía para un error general.
    /// </summary>
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : CreateSaleResult;

    /// <summary>El vendedor no puede cobrar (no está activo o su cuenta bancaria no está activa).</summary>
    public sealed record NotAllowed : CreateSaleResult;

    /// <summary>La función es de Pro y el plan del vendedor no la tiene (cobro con carrito en Básico).</summary>
    public sealed record PlanRequired : CreateSaleResult;

    /// <summary>
    /// El plan venció (5.14): no genera cobros nuevos hasta que pague. Lo ya generado se abona siempre.
    /// </summary>
    public sealed record PlanExpired : CreateSaleResult;

    /// <summary>
    /// Falla de la pasarela; <see cref="Message"/> se puede mostrar al vendedor. <see cref="SaleId"/> viene solo cuando
    /// la venta se conservó (cancelada) porque el resultado es incierto: un push sin respuesta de Pey pudo llegarle al
    /// cliente, y si lo paga el pago se registra en esa venta.
    /// </summary>
    public sealed record GatewayFailed(string Message, int? SaleId = null) : CreateSaleResult;
}

/// <summary>Cobros del vendedor (implementado en Infrastructure: EF Core + <c>IPaymentGateway</c>).</summary>
public interface ISaleCharging
{
    Task<CreateSaleResult> CreateDirectSaleAsync(CreateDirectSaleCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Venta con carrito: los precios y nombres salen del catálogo vigente (se copian a <c>SaleItems</c>). Si el total
    /// no coincide con el que vio el vendedor (<see cref="CreateCartSaleCommand.ExpectedTotal"/>) no se cobra.
    /// </summary>
    Task<CreateSaleResult> CreateCartSaleAsync(CreateCartSaleCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela una venta pendiente del vendedor. Solo es local: CoDi no permite anular el QR, así que si aun así llega
    /// un pago la venta queda pagada.
    /// </summary>
    Task<CancelSaleResult> CancelSaleAsync(CancelSaleCommand command, CancellationToken cancellationToken = default);
}
