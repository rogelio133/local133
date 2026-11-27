namespace Local133.Application.Merchants;

/// <summary>
/// Publicar o dejar de publicar el catálogo público (<c>/tienda/{slug}</c>, 5.10). Solo en Pro, con nombre del negocio
/// y por un vendedor que no esté dado de baja. El slug es único entre vendedores.
/// </summary>
public interface IPublicCatalogSettings
{
    /// <param name="slug">Lo que escribió el vendedor; se normaliza con <c>PublicSlug.Normalize</c>.</param>
    Task<PublishCatalogResult> PublishAsync(int merchantId, string? slug, CancellationToken cancellationToken = default);

    /// <summary>Deja de publicar (conserva el slug). Idempotente.</summary>
    Task<UnpublishCatalogResult> UnpublishAsync(int merchantId, CancellationToken cancellationToken = default);
}

public abstract record PublishCatalogResult
{
    private PublishCatalogResult()
    {
    }

    public sealed record Published(string Slug) : PublishCatalogResult;

    /// <summary>El slug no cumple las reglas, una vez normalizado.</summary>
    public sealed record InvalidSlug : PublishCatalogResult;

    /// <summary>Otro vendedor ya tiene ese slug.</summary>
    public sealed record SlugTaken(string Slug) : PublishCatalogResult;

    /// <summary>El plan no permite publicar (onboarding, Básico o vencido).</summary>
    public sealed record PlanRequired : PublishCatalogResult;

    /// <summary>Falta el nombre del negocio, que es el título del catálogo.</summary>
    public sealed record BusinessNameRequired : PublishCatalogResult;

    /// <summary>Vendedor dado de baja (solo lectura) o inexistente.</summary>
    public sealed record NotAllowed : PublishCatalogResult;
}

public abstract record UnpublishCatalogResult
{
    private UnpublishCatalogResult()
    {
    }

    public sealed record Unpublished : UnpublishCatalogResult;

    public sealed record NotAllowed : UnpublishCatalogResult;
}

/// <summary>Textos del catálogo público.</summary>
public static class PublicCatalogMessages
{
    public const string SlugRequired = "Escribe cómo quieres que termine tu liga.";

    public const string InvalidSlug =
        "Usa de 3 a 40 letras sin acentos, números o guiones (por ejemplo, tacos-don-ramon).";

    public const string SlugTaken = "Esa liga ya la usa otro negocio. Prueba con otra.";

    public const string PlanRequired = "Publicar tu catálogo es parte de Pro.";

    /// <summary>En el onboarding (5.13): todavía no empieza su prueba.</summary>
    public const string TrialRequired = "Se activa con tu prueba, que empieza con tu primer cobro.";

    public const string BusinessNameRequired = "Primero escribe el nombre de tu negocio en «Mi información».";

    public const string NotAllowed = "Tu cuenta está dada de baja: no puedes cambiar tu catálogo.";

    public const string Published = "¡Listo! Tu catálogo ya está publicado.";

    public const string Unpublished = "Tu catálogo ya no está publicado. La liga muestra «Catálogo no disponible por ahora».";

    public const string Unavailable = "Catálogo no disponible por ahora";
}
