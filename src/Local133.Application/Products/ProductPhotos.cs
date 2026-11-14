namespace Local133.Application.Products;

/// <summary>Reglas de la foto de un producto (las comparten el servidor y el formulario).</summary>
public static class ProductPhotoRules
{
    /// <summary>Tamaño máximo del archivo que sube el vendedor (10 MB).</summary>
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>Límite de la petición de alta/edición: la foto más los demás campos del formulario.</summary>
    public const long MaxRequestBytes = MaxUploadBytes + (2 * 1024 * 1024);

    /// <summary>Lado mayor de la foto guardada, en píxeles.</summary>
    public const int PhotoMaxSize = 1024;

    /// <summary>Lado de la miniatura cuadrada de la lista, en píxeles.</summary>
    public const int ThumbnailSize = 240;

    /// <summary>Más píxeles que esto se rechaza sin decodificar (protección contra imágenes «bomba»; un PNG o WebP no se puede decodificar reducido).</summary>
    public const long MaxPixels = 50_000_000;

    /// <summary>Formatos aceptados (se detectan por el contenido, no por la extensión).</summary>
    public const string AcceptedMimeTypes = "image/jpeg,image/png,image/webp";

    /// <summary>Las fotos se guardan siempre como JPEG.</summary>
    public const string StoredContentType = "image/jpeg";

    /// <summary>Llave de la foto grande a partir de <c>Product.PhotoKey</c>.</summary>
    public static string PhotoFileKey(string photoKey) => $"{photoKey}.jpg";

    /// <summary>Llave de la miniatura a partir de <c>Product.PhotoKey</c>.</summary>
    public static string ThumbnailFileKey(string photoKey) => $"{photoKey}-thumb.jpg";
}

/// <summary>Foto procesada lista para guardar: JPEG sin metadatos, orientada y redimensionada.</summary>
public sealed record ProcessedProductPhoto(byte[] Photo, byte[] Thumbnail);

/// <summary>Resultado de procesar la foto que subió el vendedor.</summary>
public abstract record ProductPhotoProcessingResult
{
    private ProductPhotoProcessingResult()
    {
    }

    public sealed record Processed(ProcessedProductPhoto Photo) : ProductPhotoProcessingResult;

    /// <summary>No es una imagen aceptada; <see cref="Message"/> se muestra al vendedor.</summary>
    public sealed record Rejected(string Message) : ProductPhotoProcessingResult;
}

/// <summary>Convierte la foto subida (JPEG, PNG o WebP) en la foto y la miniatura que se guardan (Infrastructure: ImageSharp).</summary>
public interface IProductPhotoProcessor
{
    Task<ProductPhotoProcessingResult> ProcessAsync(Stream upload, CancellationToken cancellationToken = default);
}
