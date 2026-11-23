using Local133.Application.Merchants;
using Local133.Infrastructure.Imaging;
using SkiaSharp;

namespace Local133.Infrastructure.Merchants;

/// <summary>
/// Logo y foto del local de «Mi información» (4.1) con SkiaSharp, cargados con <see cref="SkiaImageLoader"/>:
/// <list type="bullet">
///   <item>logo: sobre fondo transparente, cabe completo (sin recortar ni agrandar) en
///   <see cref="MerchantImageRules.LogoMaxSize"/> px y se guarda como PNG, que conserva la transparencia;</item>
///   <item>foto del local: sobre blanco, con el lado mayor de <see cref="MerchantImageRules.ShopPhotoMaxSize"/> px
///   como máximo, en JPEG.</item>
/// </list>
/// Ninguno lleva EXIF.
/// </summary>
public sealed class SkiaMerchantImageProcessor : IMerchantImageProcessor
{
    public const int JpegQuality = 80;

    public async Task<MerchantImageProcessingResult> ProcessAsync(
        MerchantImageKind kind, Stream upload, CancellationToken cancellationToken = default)
    {
        var isLogo = kind == MerchantImageKind.Logo;
        var background = isLogo ? SKColors.Transparent : SKColors.White;
        using var loaded = await SkiaImageLoader.LoadAsync(upload, MerchantImageRules.MaxPixels, background, cancellationToken);
        if (loaded.Bitmap is not { } oriented)
        {
            return new MerchantImageProcessingResult.Rejected(loaded.Failure switch
            {
                ImageLoadFailure.Empty => MerchantImageMessages.Empty,
                ImageLoadFailure.TooManyPixels => MerchantImageMessages.TooManyPixels,
                _ => MerchantImageMessages.Invalid,
            });
        }

        var maxSize = isLogo ? MerchantImageRules.LogoMaxSize : MerchantImageRules.ShopPhotoMaxSize;
        var (width, height) = SkiaImageLoader.FitWithin(oriented.Width, oriented.Height, maxSize);
        using var image = SkiaImageLoader.Draw(oriented, new SKRectI(0, 0, oriented.Width, oriented.Height), width, height, background);
        return new MerchantImageProcessingResult.Processed(isLogo
            ? SkiaImageLoader.Encode(image, SKEncodedImageFormat.Png, 100)
            : SkiaImageLoader.Encode(image, SKEncodedImageFormat.Jpeg, JpegQuality));
    }
}
