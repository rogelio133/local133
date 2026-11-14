using Local133.Application.Products;
using Local133.Infrastructure.Products;
using SkiaSharp;

namespace Local133.IntegrationTests.Products;

/// <summary>Procesamiento de la foto con SkiaSharp (sin SQL Server: corre en CI).</summary>
public class ProductPhotoProcessorTests
{
    private readonly SkiaProductPhotoProcessor _processor = new();

    private async Task<ProcessedProductPhoto> ProcessOkAsync(byte[] upload)
    {
        var result = await _processor.ProcessAsync(new MemoryStream(upload));
        return Assert.IsType<ProductPhotoProcessingResult.Processed>(result).Photo;
    }

    private async Task<string> ProcessRejectedAsync(byte[] upload)
    {
        var result = await _processor.ProcessAsync(new MemoryStream(upload));
        return Assert.IsType<ProductPhotoProcessingResult.Rejected>(result).Message;
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task Acepta_JPEG_PNG_y_WebP_y_guarda_JPEG(SKEncodedImageFormat format)
    {
        var photo = await ProcessOkAsync(TestImages.Solid(300, 200, format));

        Assert.True(TestImages.IsJpeg(photo.Photo));
        Assert.True(TestImages.IsJpeg(photo.Thumbnail));
        using var decoded = TestImages.Decode(photo.Photo);
        Assert.Equal((300, 200), (decoded.Width, decoded.Height)); // una foto chica no se agranda
    }

    [Fact]
    public async Task Reduce_la_foto_grande_y_hace_miniatura_cuadrada()
    {
        var photo = await ProcessOkAsync(TestImages.Solid(3000, 2000));

        using var big = TestImages.Decode(photo.Photo);
        using var thumb = TestImages.Decode(photo.Thumbnail);
        Assert.Equal((ProductPhotoRules.PhotoMaxSize, 683), (big.Width, big.Height));
        Assert.Equal((ProductPhotoRules.ThumbnailSize, ProductPhotoRules.ThumbnailSize), (thumb.Width, thumb.Height));
    }

    [Fact]
    public async Task Endereza_la_foto_segun_la_orientacion_EXIF()
    {
        // Orientación 6: la cámara guardó la foto acostada; hay que girarla 90° en el sentido del reloj.
        var upload = TestImages.WithExif(TestImages.RedLeftBlueRight(400, 200), orientation: 6);

        var photo = await ProcessOkAsync(upload);

        using var decoded = TestImages.Decode(photo.Photo);
        Assert.Equal((200, 400), (decoded.Width, decoded.Height));
        // La mitad izquierda (roja) queda arriba y la derecha (azul) abajo.
        var top = decoded.GetPixel(100, 50);
        var bottom = decoded.GetPixel(100, 350);
        Assert.True(top.Red > 200 && top.Blue < 60, $"Arriba debería ser rojo: {top}");
        Assert.True(bottom.Blue > 200 && bottom.Red < 60, $"Abajo debería ser azul: {bottom}");
    }

    [Fact]
    public async Task Quita_los_metadatos_EXIF()
    {
        var upload = TestImages.WithExif(TestImages.Solid(400, 300), orientation: 1);
        Assert.True(TestImages.HasExif(upload));

        var photo = await ProcessOkAsync(upload);

        Assert.False(TestImages.HasExif(photo.Photo));
        Assert.False(TestImages.HasExif(photo.Thumbnail));
        Assert.False(TestImages.Contains(photo.Photo, "Ubicacion secreta"));
    }

    [Fact]
    public async Task La_transparencia_queda_en_blanco()
    {
        var photo = await ProcessOkAsync(TestImages.TransparentPng(100, 100));

        using var decoded = TestImages.Decode(photo.Photo);
        var pixel = decoded.GetPixel(50, 50);
        Assert.True(pixel.Red > 245 && pixel.Green > 245 && pixel.Blue > 245, $"Debería ser blanco: {pixel}");
    }

    [Fact]
    public async Task Rechaza_lo_que_no_es_imagen() =>
        Assert.Equal(ProductMessages.PhotoInvalid, await ProcessRejectedAsync("esto no es una foto"u8.ToArray()));

    [Fact]
    public async Task Rechaza_GIF() =>
        Assert.Equal(ProductMessages.PhotoInvalid, await ProcessRejectedAsync(TestImages.Gif));

    [Fact]
    public async Task Rechaza_archivo_vacio() =>
        Assert.Equal(ProductMessages.PhotoEmpty, await ProcessRejectedAsync([]));

    [Fact]
    public async Task Rechaza_JPEG_truncado()
    {
        var jpeg = TestImages.Solid(800, 600);

        Assert.Equal(ProductMessages.PhotoInvalid, await ProcessRejectedAsync(jpeg[..(jpeg.Length / 3)]));
    }

    [Fact]
    public async Task Rechaza_sin_decodificar_una_imagen_de_mas_de_50_megapixeles() =>
        Assert.Equal(ProductMessages.PhotoTooManyPixels, await ProcessRejectedAsync(TestImages.PngHeaderOnly(10_000, 5_001)));
}
