using Local133.Application.Merchants;
using Local133.Infrastructure.Merchants;
using Local133.IntegrationTests.Products;
using SkiaSharp;

namespace Local133.IntegrationTests.Merchants;

/// <summary>Logo (PNG con transparencia) y foto del local (JPEG) con SkiaSharp (4.1; sin SQL Server: corre en CI).</summary>
public class MerchantImageProcessorTests
{
    private readonly SkiaMerchantImageProcessor _processor = new();

    private async Task<byte[]> ProcessOkAsync(MerchantImageKind kind, byte[] upload)
    {
        var result = await _processor.ProcessAsync(kind, new MemoryStream(upload));
        return Assert.IsType<MerchantImageProcessingResult.Processed>(result).Content;
    }

    private async Task<string> ProcessRejectedAsync(MerchantImageKind kind, byte[] upload)
    {
        var result = await _processor.ProcessAsync(kind, new MemoryStream(upload));
        return Assert.IsType<MerchantImageProcessingResult.Rejected>(result).Message;
    }

    /// <summary>Mitad izquierda transparente y mitad derecha roja.</summary>
    internal static byte[] HalfTransparentPng(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        using (var red = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(width / 2f, 0, width / 2f, height, red);
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static bool IsPng(byte[] data) =>
        data.Length > 8 && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G';

    [Fact]
    public async Task El_logo_conserva_la_transparencia_y_cabe_sin_recortar()
    {
        var logo = await ProcessOkAsync(MerchantImageKind.Logo, HalfTransparentPng(1000, 500));

        Assert.True(IsPng(logo));
        using var decoded = TestImages.Decode(logo);
        // Cabe completo en 512: proporción 2:1 sin recorte.
        Assert.Equal((MerchantImageRules.LogoMaxSize, 256), (decoded.Width, decoded.Height));
        Assert.Equal(0, decoded.GetPixel(50, 128).Alpha); // izquierda: transparente
        var right = decoded.GetPixel(450, 128);
        Assert.Equal((255, 255, 0), (right.Alpha, right.Red, right.Green));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task El_logo_chico_no_se_agranda_y_sale_en_PNG(SKEncodedImageFormat format)
    {
        var logo = await ProcessOkAsync(MerchantImageKind.Logo, TestImages.Solid(300, 120, format));

        Assert.True(IsPng(logo));
        using var decoded = TestImages.Decode(logo);
        Assert.Equal((300, 120), (decoded.Width, decoded.Height));
    }

    [Fact]
    public async Task La_foto_del_local_sale_en_JPEG_reducida_sin_EXIF_y_sobre_blanco()
    {
        var photo = await ProcessOkAsync(MerchantImageKind.ShopPhoto, TestImages.WithExif(TestImages.Solid(3000, 2000), orientation: 1));

        Assert.True(TestImages.IsJpeg(photo));
        Assert.False(TestImages.HasExif(photo));
        using var decoded = TestImages.Decode(photo);
        Assert.Equal((MerchantImageRules.ShopPhotoMaxSize, 853), (decoded.Width, decoded.Height));

        // Un PNG transparente queda sobre blanco en JPEG.
        var fromPng = await ProcessOkAsync(MerchantImageKind.ShopPhoto, TestImages.TransparentPng(200, 100));
        using var white = TestImages.Decode(fromPng);
        var pixel = white.GetPixel(10, 10);
        Assert.True(pixel.Red > 250 && pixel.Green > 250 && pixel.Blue > 250);
    }

    [Fact]
    public async Task Endereza_segun_la_orientacion_EXIF()
    {
        var upload = TestImages.WithExif(TestImages.RedLeftBlueRight(400, 200), orientation: 6);

        var photo = await ProcessOkAsync(MerchantImageKind.ShopPhoto, upload);

        using var decoded = TestImages.Decode(photo);
        Assert.Equal((200, 400), (decoded.Width, decoded.Height));
    }

    [Theory]
    [InlineData(MerchantImageKind.Logo)]
    [InlineData(MerchantImageKind.ShopPhoto)]
    public async Task Rechaza_lo_que_no_es_imagen_vacio_o_gigante(MerchantImageKind kind)
    {
        Assert.Equal(MerchantImageMessages.Empty, await ProcessRejectedAsync(kind, []));
        Assert.Equal(MerchantImageMessages.Invalid, await ProcessRejectedAsync(kind, "no soy imagen"u8.ToArray()));
        Assert.Equal(MerchantImageMessages.Invalid, await ProcessRejectedAsync(kind, TestImages.Gif));
        Assert.Equal(MerchantImageMessages.TooManyPixels, await ProcessRejectedAsync(kind, TestImages.PngHeaderOnly(10_000, 6_000)));
    }
}
