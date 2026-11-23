using SkiaSharp;

namespace Local133.Infrastructure.Imaging;

/// <summary>Por qué no se pudo cargar una imagen subida.</summary>
internal enum ImageLoadFailure
{
    /// <summary>El archivo está vacío.</summary>
    Empty,

    /// <summary>No es JPEG, PNG ni WebP, o está dañado o truncado.</summary>
    Invalid,

    /// <summary>Tiene más píxeles que el máximo (imagen «bomba»).</summary>
    TooManyPixels,
}

/// <summary>Imagen cargada y enderezada (<see cref="Bitmap"/>) o el motivo del rechazo (<see cref="Failure"/>).</summary>
internal sealed class ImageLoadResult : IDisposable
{
    private ImageLoadResult(SKBitmap? bitmap, ImageLoadFailure? failure)
    {
        Bitmap = bitmap;
        Failure = failure;
    }

    public SKBitmap? Bitmap { get; }

    public ImageLoadFailure? Failure { get; }

    public static ImageLoadResult Loaded(SKBitmap bitmap) => new(bitmap, null);

    public static ImageLoadResult Failed(ImageLoadFailure failure) => new(null, failure);

    public void Dispose() => Bitmap?.Dispose();
}

/// <summary>
/// Carga común de las imágenes que suben los vendedores (fotos de producto, logo y foto del local) con SkiaSharp:
/// detecta el formato por el contenido (JPEG, PNG o WebP), rechaza imágenes gigantes antes de decodificarlas (los
/// JPEG grandes se decodifican ya reducidos) y endereza la imagen según su EXIF (fotos del celular), sobre el fondo
/// que se pida: blanco para lo que se guarda en JPEG, transparente para el logo en PNG.
/// </summary>
internal static class SkiaImageLoader
{
    /// <summary>Lado máximo al decodificar: los JPEG grandes se decodifican reducidos (menos memoria).</summary>
    public const int DecodeMaxSize = 2048;

    private static readonly SKEncodedImageFormat[] AcceptedFormats =
        [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp];

    /// <summary>Muestreo con mipmaps: reducir de 2048 a 240 px sin dientes de sierra.</summary>
    public static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public static async Task<ImageLoadResult> LoadAsync(
        Stream upload, long maxPixels, SKColor background, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);

        using var buffer = new MemoryStream();
        await upload.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            return ImageLoadResult.Failed(ImageLoadFailure.Empty);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var data = SKData.CreateCopy(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        using var codec = SKCodec.Create(data);
        // null: formato desconocido (texto, PDF, HEIC…) o encabezado dañado.
        if (codec is null || !AcceptedFormats.Contains(codec.EncodedFormat))
        {
            return ImageLoadResult.Failed(ImageLoadFailure.Invalid);
        }

        var (width, height) = (codec.Info.Width, codec.Info.Height);
        if (width <= 0 || height <= 0)
        {
            return ImageLoadResult.Failed(ImageLoadFailure.Invalid);
        }

        if ((long)width * height > maxPixels)
        {
            return ImageLoadResult.Failed(ImageLoadFailure.TooManyPixels);
        }

        using var decoded = Decode(codec, width, height);
        if (decoded is null)
        {
            return ImageLoadResult.Failed(ImageLoadFailure.Invalid);
        }

        return ImageLoadResult.Loaded(Orient(decoded, codec.EncodedOrigin, background));
    }

    /// <summary>Copia la región <paramref name="source"/> escalada a <paramref name="width"/>×<paramref name="height"/>, sobre <paramref name="background"/>.</summary>
    public static SKBitmap Draw(SKBitmap bitmap, SKRectI source, int width, int height, SKColor background)
    {
        var result = new SKBitmap(NewInfo(width, height));
        using var canvas = new SKCanvas(result);
        canvas.Clear(background);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, source, new SKRect(0, 0, width, height), Sampling);
        return result;
    }

    /// <summary>Tamaño para que el lado mayor no pase de <paramref name="maxSize"/>, sin agrandar.</summary>
    public static (int Width, int Height) FitWithin(int width, int height, int maxSize)
    {
        var scale = Math.Min(1f, (float)maxSize / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>Codifica sin EXIF (el codificador de Skia no lo escribe: ni la ubicación ni el modelo del teléfono).</summary>
    public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var encoded = bitmap.Encode(format, quality)
            ?? throw new InvalidOperationException($"SkiaSharp no pudo codificar la imagen ({format}).");
        return encoded.ToArray();
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

    /// <summary>Aplica la orientación EXIF (las cámaras guardan la foto «acostada» y anotan cómo girarla).</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin, SKColor background)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;

        var result = new SKBitmap(NewInfo(width, height));
        using var canvas = new SKCanvas(result);
        canvas.Clear(background);
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

    private static SKImageInfo NewInfo(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
}
