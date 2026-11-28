using Local133.Domain.Merchants;

namespace Local133.Web.Models;

/// <summary>
/// Vista previa del catálogo del vendedor (5.10). El público lo dibuja la portada desde el 2026-11-27, con
/// <see cref="Local133.Web.PublicPages.PublicStorefront"/>.
/// </summary>
/// <param name="ListPath">Página del catálogo (paginación y búsqueda).</param>
/// <param name="WhatsAppUrl">«Pedir por WhatsApp» al celular del negocio, o <c>null</c> si no tiene.</param>
/// <param name="MadeWithUrl">Portada con el código de invitación del vendedor («Hecho con Local133»).</param>
/// <param name="Grouped">
/// Los productos van agrupados por categoría (en su orden; sin categoría al final, en «Otros»): el vendedor tiene
/// productos con categoría.
/// </param>
public sealed record StorefrontViewModel(
    string BusinessName,
    string? Address,
    string? LogoUrl,
    string? CoverUrl,
    string? WhatsAppUrl,
    IReadOnlyList<StorefrontProduct> Products,
    PageInfo Page,
    string? Query,
    string ListPath,
    string MadeWithUrl,
    bool IsPreview,
    bool Grouped = false)
{
    /// <summary>Productos por página.</summary>
    public const int PageSize = 24;

    /// <summary>Largo máximo de la búsqueda.</summary>
    public const int QueryMaxLength = 80;

    /// <summary>Renglones de la dirección (se guardan con <c>\n</c>).</summary>
    public IReadOnlyList<string> AddressLines =>
        Address is null ? [] : Address.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public string UrlFor(int page)
    {
        var parameters = new List<string>();
        if (Query is not null)
        {
            parameters.Add("q=" + Uri.EscapeDataString(Query));
        }

        if (page > 1)
        {
            parameters.Add($"{PageInfo.QueryKey}={page}");
        }

        return parameters.Count == 0 ? ListPath : $"{ListPath}?{string.Join('&', parameters)}";
    }

    /// <summary>Mensaje con el que abre el chat de WhatsApp.</summary>
    public static string WhatsAppMessage(string businessName) =>
        $"Hola, vi el catálogo de {businessName} en Local133.";
}

/// <summary>Producto del catálogo público; <see cref="Category"/> solo viene si el catálogo va agrupado (<c>null</c> = «Otros»).</summary>
public sealed record StorefrontProduct(string Name, string? Description, string PriceText, string? ThumbnailUrl, string? Category = null);

/// <summary>Rutas públicas del catálogo (<c>StorefrontController</c>).</summary>
public static class StorefrontUrls
{
    public static string Index(string slug) => PublicSlug.PathFor(slug);

    public static string Logo(string slug, string version) => $"{Index(slug)}/logo?v={version}";

    public static string ShopPhoto(string slug, string version) => $"{Index(slug)}/foto?v={version}";

    public static string Thumbnail(string slug, int productId, string version) =>
        $"{Index(slug)}/productos/{productId}/miniatura?v={version}";

    public static string Photo(string slug, int productId, string version) =>
        $"{Index(slug)}/productos/{productId}/foto?v={version}";
}
