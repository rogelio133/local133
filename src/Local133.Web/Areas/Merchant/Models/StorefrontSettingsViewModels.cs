using System.ComponentModel.DataAnnotations;
using Local133.Application.Merchants;
using Local133.Domain.Merchants;
using Local133.Web.Models;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>URLs de «Catálogo en línea» (rutas de <c>StorefrontSettingsController</c>).</summary>
public static class StorefrontSettingsUrls
{
    public const string Index = "/app/tienda";

    public const string Publish = "/app/tienda/publicar";

    public const string Unpublish = "/app/tienda/despublicar";

    public const string Preview = "/app/tienda/vista-previa";
}

/// <summary>
/// Liga del catálogo que escribe el vendedor. El cliente valida ya normalizado (<c>storefront-slug.js</c> lo normaliza
/// al escribir); el servidor vuelve a normalizar con <see cref="PublicSlug.Normalize"/>.
/// </summary>
public sealed class StorefrontSlugInput
{
    [Required(ErrorMessage = PublicCatalogMessages.SlugRequired)]
    [StringLength(PublicSlug.MaxLength, MinimumLength = PublicSlug.MinLength, ErrorMessage = PublicCatalogMessages.InvalidSlug)]
    [RegularExpression(PublicSlug.Pattern, ErrorMessage = PublicCatalogMessages.InvalidSlug)]
    [Display(Name = "Tu liga")]
    public string? Slug { get; set; }
}

/// <summary>«Catálogo en línea» (/app/tienda, 5.10).</summary>
public sealed class StorefrontSettingsViewModel
{
    public required StorefrontSlugInput Input { get; init; }

    /// <summary>Base de la liga en la portada, p. ej. <c>https://local133.com.mx/tienda/</c> (se muestra antes del campo).</summary>
    public required string UrlPrefix { get; init; }

    /// <summary>El plan permite publicar (Pro, prueba, gracia u onboarding).</summary>
    public bool CanPublish { get; init; }

    /// <summary>Dado de baja: solo lectura.</summary>
    public bool IsDisabled { get; init; }

    /// <summary>Suspendido: publicado o no, la liga dice «no disponible» mientras dure.</summary>
    public bool IsSuspended { get; init; }

    public bool HasBusinessName { get; init; }

    /// <summary>Publicado por el vendedor (aunque su plan ya no lo muestre).</summary>
    public bool IsPublished { get; init; }

    /// <summary>Liga absoluta del catálogo publicado, o <c>null</c>.</summary>
    public string? Url { get; init; }

    /// <summary>Celular del negocio enmascarado (botón de WhatsApp), o <c>null</c> si no tiene.</summary>
    public string? MaskedPhone { get; init; }

    public int ProductCount { get; init; }

    /// <summary>Aviso de Pro en el onboarding, Básico o vencido.</summary>
    public ProRequiredViewModel? ProRequired { get; init; }

    /// <summary>Mensaje para compartir la liga (solo publicado).</summary>
    public string? ShareMessage { get; init; }

    public string? WhatsAppUrl => ShareMessage is null ? null : ReceiptLinks.WhatsAppUrl(ShareMessage);

    public string? WhatsAppAppUrl => ShareMessage is null ? null : ReceiptLinks.WhatsAppAppUrl(ShareMessage);

    public string? WhatsAppWebUrl => ShareMessage is null ? null : ReceiptLinks.WhatsAppWebUrl(ShareMessage);

    /// <summary>Puede publicar, cambiar la liga o dejar de publicar.</summary>
    public bool CanEdit => CanPublish && !IsDisabled && HasBusinessName;

    /// <summary>«Mira el catálogo de Tacos Don Ramón:» + la liga.</summary>
    public static string MessageFor(string businessName, string url) => $"Mira el catálogo de {businessName}:\n{url}";
}
