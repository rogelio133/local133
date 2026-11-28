namespace Local133.Web.PublicPages;

// Contratos de la API pública de solo lectura (/api/public, ajustes 2026-11-27): el recibo y el catálogo públicos se
// dibujan en la portada (Local133.LandingPage), que no tiene base, y piden sus datos al portal. Este archivo se enlaza
// en la portada (Compile Link): sin dependencias fuera de la BCL. Los textos (montos y fechas) ya vienen formateados.

/// <summary>Rutas de la API pública (portal) y de las páginas que la usan (portada).</summary>
public static class PublicPagesApi
{
    /// <summary>Largo del token del recibo: 32 caracteres hexadecimales en minúsculas (igual que <c>Sale.ReceiptTokenLength</c>).</summary>
    public const int ReceiptTokenLength = 32;

    /// <summary>Ruta del recibo en la portada.</summary>
    public const string ReceiptPathPrefix = "/recibo/";

    /// <summary>Ruta del catálogo en la portada (igual que <c>PublicSlug.PathPrefix</c>).</summary>
    public const string StorefrontPathPrefix = "/tienda/";

    /// <summary>Parámetro de la página del catálogo (igual que <c>PageInfo.QueryKey</c>).</summary>
    public const string PageQueryKey = "pagina";

    /// <summary>
    /// Encabezado con la llave compartida: solo el servidor de la portada llama a la API (el navegador nunca), y el
    /// portal rechaza con 401 la petición sin ella.
    /// </summary>
    public const string KeyHeader = "X-Local133-Key";

    /// <summary>
    /// Llave compartida, la misma en los dos sitios (<c>PublicApi:Key</c>; en Azure <c>PublicApi__Key</c>). Es un
    /// secreto: user-secrets en desarrollo, App Settings o Key Vault en Azure, nunca en appsettings.
    /// </summary>
    public const string KeyConfigKey = "PublicApi:Key";

    /// <summary>Datos del recibo: <c>GET /api/public/recibos/{token}</c> ⇒ <see cref="PublicReceipt"/> o 404.</summary>
    public static string Receipt(string token) => $"/api/public/recibos/{Uri.EscapeDataString(token)}";

    /// <summary>
    /// Datos del catálogo: <c>GET /api/public/tiendas/{slug}?q=&amp;pagina=</c> ⇒ <see cref="PublicStorefront"/> (también
    /// cuando no se puede ver, con <see cref="PublicStorefront.Available"/> en falso) o 404 si la liga no existe.
    /// </summary>
    public static string Storefront(string slug, string? query = null, int? page = null)
    {
        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(query))
        {
            parameters.Add("q=" + Uri.EscapeDataString(query));
        }

        if (page is > 1)
        {
            parameters.Add($"{PageQueryKey}={page.Value}");
        }

        var path = $"/api/public/tiendas/{Uri.EscapeDataString(slug)}";
        return parameters.Count == 0 ? path : $"{path}?{string.Join('&', parameters)}";
    }

    /// <summary>Tiene la forma de un token de recibo (no consulta nada).</summary>
    public static bool IsReceiptToken(string? token) =>
        token is { Length: ReceiptTokenLength } && token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>Recibo público: solo lo que el cliente necesita. Nunca el celular, la CLABE, el folio ni ids de Pey.</summary>
/// <param name="AmountText">Total pagado (suma de los pagos recibidos).</param>
/// <param name="LogoUrl">Logo del negocio (URL absoluta del portal), solo con recibo con marca (Pro) y logo.</param>
/// <param name="ReferralCode">
/// Código de invitación para «Cobrado con Local133» (Básico y vencido); <c>null</c> con recibo con marca.
/// </param>
public sealed record PublicReceipt(
    string? BusinessName,
    string AmountText,
    string? ConceptText,
    string? PaidAtText,
    IReadOnlyList<PublicReceiptItem> Items,
    IReadOnlyList<PublicReceiptPayment> Payments,
    string? LogoUrl,
    string? ReferralCode)
{
    public bool IsCart => Items.Count > 0;
}

/// <summary>Producto de un recibo de carrito (copia de la venta).</summary>
public sealed record PublicReceiptItem(string Name, int Quantity, string UnitPriceText, string AmountText);

/// <summary>Pago en el recibo.</summary>
public sealed record PublicReceiptPayment(string PaidAtText, string AmountText, string? TrackingKey);

/// <summary>
/// Catálogo público. Con <see cref="Available"/> en falso (sin publicar, sin Pro, suspendido o de baja) solo trae
/// <see cref="BusinessName"/>. Las imágenes son URLs absolutas del portal.
/// </summary>
/// <param name="Address">Dirección, con renglones separados por <c>\n</c>.</param>
/// <param name="WhatsAppUrl">«Pedir por WhatsApp» al celular del negocio, o <c>null</c> si no tiene.</param>
/// <param name="Page">Página mostrada (base 1, ya ajustada).</param>
/// <param name="ReferralCode">Código de invitación del vendedor («Catálogo hecho con Local133»).</param>
public sealed record PublicStorefront(
    bool Available,
    string? BusinessName,
    string? Address,
    string? LogoUrl,
    string? CoverUrl,
    string? WhatsAppUrl,
    IReadOnlyList<PublicStorefrontProduct> Products,
    int Page,
    int PageSize,
    int TotalCount,
    string? Query,
    string? ReferralCode)
{
    /// <summary>Productos por página (igual que <c>StorefrontViewModel.PageSize</c>).</summary>
    public const int DefaultPageSize = 24;

    /// <summary>Largo máximo de la búsqueda (igual que <c>StorefrontViewModel.QueryMaxLength</c>).</summary>
    public const int QueryMaxLength = 80;

    public int TotalPages => TotalCount == 0 || PageSize <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>Catálogo que no se puede ver.</summary>
    public static PublicStorefront Unavailable(string? businessName) =>
        new(false, businessName, null, null, null, null, [], 1, DefaultPageSize, 0, null, null);
}

/// <summary>Producto del catálogo público.</summary>
public sealed record PublicStorefrontProduct(string Name, string? Description, string PriceText, string? ThumbnailUrl);
