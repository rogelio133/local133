using FluentValidation;
using Local133.Domain.Sales;

namespace Local133.Application.Sales;

/// <summary>Producto del carrito: id del catálogo del vendedor y cantidad.</summary>
public sealed record CartLine(int ProductId, int Quantity);

/// <summary>
/// El vendedor (por su usuario de Identity) cobra los productos de su catálogo, con QR o al celular del pagador.
/// </summary>
/// <param name="ExpectedTotal">Total que vio el vendedor; si los precios cambiaron desde entonces no se cobra.</param>
/// <param name="Concept">Concepto (el formulario lo propone con los productos y el vendedor puede editarlo).</param>
public sealed record CreateCartSaleCommand(
    string UserId,
    IReadOnlyList<CartLine> Lines,
    decimal ExpectedTotal,
    string? Concept,
    ChargeMethod Method = ChargeMethod.DynamicQr,
    string? PhoneNumber = null);

public sealed class CreateCartSaleCommandValidator : AbstractValidator<CreateCartSaleCommand>
{
    public CreateCartSaleCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Lines)
            .Cascade(CascadeMode.Stop)
            .Must(l => l is { Count: > 0 }).WithMessage(SaleCartMessages.Empty)
            .Must(l => l.Count <= SaleCart.MaxLines).WithMessage(SaleCartMessages.TooManyLines)
            .Must(l => l.Select(x => x.ProductId).Distinct().Count() == l.Count).WithMessage(SaleCartMessages.Duplicated)
            .Must(l => l.All(x => x.ProductId > 0 && SaleItem.IsValidQuantity(x.Quantity))).WithMessage(SaleCartMessages.QuantityInvalid);
        RuleFor(c => c.ExpectedTotal)
            .Must(Sale.IsValidAmount).WithMessage(SaleCartMessages.TotalInvalid);
        RuleFor(c => c.Concept).ValidConceptText();
        this.ChargeMethodRules(c => c.Method, c => c.PhoneNumber);
    }
}

public static class SaleCartMessages
{
    public const string Empty = "Agrega al menos un producto a la venta.";

    public const string TooManyLines = "Una venta admite hasta 50 productos distintos.";

    public const string Duplicated = "Un producto aparece dos veces en la venta. Revisa tu venta.";

    public const string QuantityInvalid = "La cantidad de cada producto debe ser de 1 a 999.";

    public const string TotalInvalid = "El total de la venta debe estar entre $0.01 y $999,999.99.";

    public const string ProductUnavailable = "Uno de los productos ya no está en tu catálogo; revisa tu venta.";

    public const string PricesChanged = "Cambió el precio de algún producto. Revisa el total antes de cobrar.";

    public const string TotalTooHigh = "El total máximo de una venta es $999,999.99.";
}
