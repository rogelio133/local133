using Local133.Application.Products;
using Local133.Infrastructure.Imaging;
using SkiaSharp;

namespace Local133.Infrastructure.Products;

/// <summary>
/// Foto de producto con SkiaSharp: la carga con <see cref="SkiaImageLoader"/> (formato por el contenido, cota de
/// píxeles y orientación EXIF), la reduce a <see cref="ProductPhotoRules.PhotoMaxSize"/> px más una miniatura
/// cuadrada y la guarda como JPEG sobre fondo blanco. El codificador de Skia no escribe EXIF: la ubicación (GPS) y
/// el modelo del teléfono no se guardan.
/// </summary>
public sealed class SkiaProductPhotoProcessor : IProductPhotoProcessor
{
    /// <summary>Calidad JPEG de lo guardado (buen equilibrio entre peso y nitidez para fotos de producto).</summary>
    public const int JpegQuality = 80;

    public async Task<ProductPhotoProcessingResult> ProcessAsync(Stream upload, CancellationToken cancellationToken = default)
    {
        using var loaded = await SkiaImageLoader.LoadAsync(upload, ProductPhotoRules.MaxPixels, SKColors.White, cancellationToken);
        if (loaded.Bitmap is not { } oriented)
        {
            return new ProductPhotoProcessingResult.Rejected(loaded.Failure switch
            {
                ImageLoadFailure.Empty => ProductMessages.PhotoEmpty,
                ImageLoadFailure.TooManyPixels => ProductMessages.PhotoTooManyPixels,
                _ => ProductMessages.PhotoInvalid,
            });
        }

        var (photoWidth, photoHeight) = SkiaImageLoader.FitWithin(oriented.Width, oriented.Height, ProductPhotoRules.PhotoMaxSize);
        using var photo = SkiaImageLoader.Draw(
            oriented, new SKRectI(0, 0, oriented.Width, oriented.Height), photoWidth, photoHeight, SKColors.White);

        // Miniatura: el cuadrado central de la foto.
        var side = Math.Min(oriented.Width, oriented.Height);
        var left = (oriented.Width - side) / 2;
        var top = (oriented.Height - side) / 2;
        using var thumbnail = SkiaImageLoader.Draw(oriented, new SKRectI(left, top, left + side, top + side),
            ProductPhotoRules.ThumbnailSize, ProductPhotoRules.ThumbnailSize, SKColors.White);

        return new ProductPhotoProcessingResult.Processed(new ProcessedProductPhoto(
            SkiaImageLoader.Encode(photo, SKEncodedImageFormat.Jpeg, JpegQuality),
            SkiaImageLoader.Encode(thumbnail, SKEncodedImageFormat.Jpeg, JpegQuality)));
    }
}
