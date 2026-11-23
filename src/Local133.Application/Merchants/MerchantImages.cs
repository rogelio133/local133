namespace Local133.Application.Merchants;

/// <summary>Qué imagen de «Mi información» (4.1) se sube, se quita o se sirve.</summary>
public enum MerchantImageKind
{
    /// <summary>Logo del negocio: PNG que conserva la transparencia y cabe completo en <see cref="MerchantImageRules.LogoMaxSize"/>.</summary>
    Logo,

    /// <summary>Foto del local o del puesto: JPEG de hasta <see cref="MerchantImageRules.ShopPhotoMaxSize"/> px.</summary>
    ShopPhoto,
}

/// <summary>Reglas del logo y la foto del local (las comparten el servidor y el formulario).</summary>
public static class MerchantImageRules
{
    /// <summary>Tamaño máximo del archivo que sube el vendedor (10 MB, como las fotos de producto).</summary>
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>Límite de la petición: la imagen más el token antiforgery.</summary>
    public const long MaxRequestBytes = MaxUploadBytes + (2 * 1024 * 1024);

    /// <summary>El logo cabe completo (sin recortar) en un cuadro de este lado, en píxeles.</summary>
    public const int LogoMaxSize = 512;

    /// <summary>Lado mayor de la foto del local, en píxeles.</summary>
    public const int ShopPhotoMaxSize = 1280;

    /// <summary>Más píxeles que esto se rechaza sin decodificar (igual que las fotos de producto).</summary>
    public const long MaxPixels = 50_000_000;

    /// <summary>Formatos aceptados (se detectan por el contenido, no por la extensión).</summary>
    public const string AcceptedMimeTypes = "image/jpeg,image/png,image/webp";

    public const string LogoContentType = "image/png";

    public const string ShopPhotoContentType = "image/jpeg";

    public static string ContentType(MerchantImageKind kind) =>
        kind == MerchantImageKind.Logo ? LogoContentType : ShopPhotoContentType;

    /// <summary>Llave base nueva (sin extensión): <c>merchants/{id}/logo-{guid}</c> o <c>merchants/{id}/local-{guid}</c>.</summary>
    public static string NewKey(int merchantId, MerchantImageKind kind) =>
        $"merchants/{merchantId}/{(kind == MerchantImageKind.Logo ? "logo" : "local")}-{Guid.NewGuid():N}";

    /// <summary>Llave del archivo a partir de la llave base guardada en el vendedor.</summary>
    public static string FileKey(MerchantImageKind kind, string key) =>
        kind == MerchantImageKind.Logo ? $"{key}.png" : $"{key}.jpg";

    /// <summary>Versión para la URL (<c>?v=</c>): el guid del final de la llave, que cambia con cada imagen nueva.</summary>
    public static string Version(string key) => key[(key.LastIndexOf('-') + 1)..];
}

/// <summary>Textos de las imágenes de «Mi información».</summary>
public static class MerchantImageMessages
{
    public const string Required = "Elige una imagen.";

    public const string Empty = "La imagen está vacía. Elige otra.";

    public const string TooLarge = "La imagen pesa más de 10 MB. Elige una más ligera.";

    public const string Invalid = "No pudimos leer la imagen. Usa una imagen JPG, PNG o WebP.";

    public const string TooManyPixels = "La imagen es demasiado grande. Usa una de menos de 50 megapíxeles.";
}

/// <summary>Imagen que subió el vendedor.</summary>
public sealed record MerchantImageUpload(Stream Content, long Length);

/// <summary>Resultado de procesar el logo o la foto del local.</summary>
public abstract record MerchantImageProcessingResult
{
    private MerchantImageProcessingResult()
    {
    }

    /// <summary>Lista para guardar, sin metadatos (PNG para el logo, JPEG para la foto).</summary>
    public sealed record Processed(byte[] Content) : MerchantImageProcessingResult;

    /// <summary>No es una imagen aceptada; <see cref="Message"/> se muestra al vendedor.</summary>
    public sealed record Rejected(string Message) : MerchantImageProcessingResult;
}

/// <summary>Convierte la imagen subida (JPEG, PNG o WebP) en el logo o la foto del local que se guardan (Infrastructure: SkiaSharp).</summary>
public interface IMerchantImageProcessor
{
    Task<MerchantImageProcessingResult> ProcessAsync(MerchantImageKind kind, Stream upload, CancellationToken cancellationToken = default);
}
