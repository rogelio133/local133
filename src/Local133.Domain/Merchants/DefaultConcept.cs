using Local133.Domain.Sales;

namespace Local133.Domain.Merchants;

/// <summary>
/// Concepto por defecto del vendedor (<see cref="Merchant.DefaultConcept"/>, ajustes 2026-11-26): opcional, con las
/// mismas reglas que el concepto de un cobro (<see cref="Sale.IsValidConceptText"/>). Cobrar › Monto lo precarga y el
/// vendedor lo puede cambiar en cada cobro.
/// </summary>
public static class DefaultConcept
{
    public const int MaxLength = Sale.ConceptTextMaxLength;

    public const string Pattern = Sale.ConceptTextPattern;

    /// <summary>Sin espacios en los extremos; vacío ⇒ <c>null</c>. No valida.</summary>
    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Vacío es válido (no hay concepto por defecto); si no, debe ser un concepto de cobro válido.</summary>
    public static bool IsValid(string? value) => Normalize(value) is not { } concept || Sale.IsValidConceptText(concept);
}
