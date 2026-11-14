using Local133.Application.Products;
using SkiaSharp;

namespace Local133.Infrastructure.Products;

/// <summary>
/// Foto de producto con SkiaSharp: detecta el formato por el contenido (JPEG, PNG o WebP), rechaza imágenes
/// gigantes antes de decodificarlas (los JPEG grandes se decodifican ya reducidos), endereza la foto según su EXIF
/// (fotos del celular), la reduce a <see cref="ProductPhotoRules.PhotoMaxSize"/> px más una miniatura cuadrada y la
/// guarda como JPEG sobre fondo blanco. El codificador de Skia no escribe EXIF: la ubicación (GPS) y el modelo del
/// teléfono no se guardan.
/// </summary>
public sealed class SkiaProductPhotoProcessor : IProductPhotoProcessor
{
    /// <summary>Calidad JPEG de lo guardado (buen equilibrio entre peso y nitidez para fotos de producto).</summary>
    public const int JpegQuality = 80;

    /// <summary>Lado máximo al decodificar: los JPEG grandes se decodifican reducidos (menos memoria).</summary>
    private const int DecodeMaxSize = 2048;

    private static readonly SKEncodedImageFormat[] AcceptedFormats =
        [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp];

    /// <summary>Muestreo con mipmaps: reducir de 2048 a 240 px sin dientes de sierra.</summary>
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public async Task<ProductPhotoProcessingResult> ProcessAsync(Stream upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        using var buffer = new MemoryStream();
        await upload.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            return new ProductPhotoProcessingResult.Rejected(ProductMessages.PhotoEmpty);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var data = SKData.CreateCopy(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        using var codec = SKCodec.Create(data);
        // null: formato desconocido (texto, PDF, HEIC…) o encabezado dañado.
        if (codec is null || !AcceptedFormats.Contains(codec.EncodedFormat))
        {
            return new ProductPhotoProcessingResult.Rejected(ProductMessages.PhotoInvalid);
        }

        var (width, height) = (codec.Info.Width, codec.Info.Height);
        if (width <= 0 || height <= 0)
        {
            return new ProductPhotoProcessingResult.Rejected(ProductMessages.PhotoInvalid);
        }

        if ((long)width * height > ProductPhotoRules.MaxPixels)
        {
            return new ProductPhotoProcessingResult.Rejected(ProductMessages.PhotoTooManyPixels);
        }

        using var decoded = Decode(codec, width, height);
        if (decoded is null)
        {
            return new ProductPhotoProcessingResult.Rejected(ProductMessages.PhotoInvalid);
        }

        using var oriented = Orient(decoded, codec.EncodedOrigin);
        var scale = Math.Min(1f, (float)ProductPhotoRules.PhotoMaxSize / Math.Max(oriented.Width, oriented.Height));
        var photoWidth = Math.Max(1, (int)Math.Round(oriented.Width * scale));
        var photoHeight = Math.Max(1, (int)Math.Round(oriented.Height * scale));
        using var photo = Draw(oriented, new SKRectI(0, 0, oriented.Width, oriented.Height), photoWidth, photoHeight);

        // Miniatura: el cuadrado central de la foto.
        var side = Math.Min(oriented.Width, oriented.Height);
        var left = (oriented.Width - side) / 2;
        var top = (oriented.Height - side) / 2;
        using var thumbnail = Draw(oriented, new SKRectI(left, top, left + side, top + side),
            ProductPhotoRules.ThumbnailSize, ProductPhotoRules.ThumbnailSize);

        return new ProductPhotoProcessingResult.Processed(new ProcessedProductPhoto(EncodeJpeg(photo), EncodeJpeg(thumbnail)));
    }

    /// <summary>Decodifica en sRGB; si el formato lo permite (JPEG), directamente a un tamaño reducido.</summary>
    private static SKBitmap? Decode(SKCodec codec, int width, int height)
    {
        var desiredScale = Math.Min(1f, (float)DecodeMaxSize / Math.Max(width, height));
        var size = desiredScale < 1f ? codec.GetScaledDimensions(desiredScale) : new SKSizeI(width, height);
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap(info);
        // Una imagen truncada (IncompleteInput) o dañada se rechaza: quedaría con franjas grises.
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    /// <summary>Aplica la orientación EXIF (las cámaras guardan la foto «acostada» y anotan cómo girarla) sobre fondo blanco.</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;

        var result = new SKBitmap(NewInfo(width, height));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // espejo horizontal
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // 180°
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft: // espejo vertical
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // transpuesta
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop: // 90° en el sentido del reloj (lo más común en celulares)
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // transversa
                canvas.Translate(width, height);
                canvas.RotateDegrees(-90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftBottom: // 90° en contra del reloj
                canvas.Translate(0, height);
                canvas.RotateDegrees(-90);
                break;
        }

        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, Sampling);
        return result;
    }

    /// <summary>Copia la región <paramref name="source"/> escalada a <paramref name="width"/>×<paramref name="height"/>, sobre blanco.</summary>
    private static SKBitmap Draw(SKBitmap bitmap, SKRectI source, int width, int height)
    {
        var result = new SKBitmap(NewInfo(width, height));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, source, new SKRect(0, 0, width, height), Sampling);
        return result;
    }

    private static SKImageInfo NewInfo(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());

    private static byte[] EncodeJpeg(SKBitmap bitmap)
    {
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, JpegQuality)
            ?? throw new InvalidOperationException("SkiaSharp no pudo codificar el JPEG.");
        return encoded.ToArray();
    }
}
