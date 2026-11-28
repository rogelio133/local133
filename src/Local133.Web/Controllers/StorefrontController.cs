using Local133.Application.Merchants;
using Local133.Application.Products;
using Local133.Application.Storage;
using Local133.Application.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Hosting;
using Local133.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Local133.Web.Controllers;

/// <summary>
/// Catálogo público del vendedor (5.10). Desde el 2026-11-27 lo dibuja la portada (<c>{App:LandingUrl}/tienda/{slug}</c>)
/// con los datos de <see cref="PublicPagesApiController"/>; aquí quedan la redirección de la liga vieja y las imágenes
/// públicas, que solo se sirven si el catálogo se ve (<see cref="StorefrontQuery.IsVisibleAsync"/>).
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("tienda/{slug}")]
public class StorefrontController(
    AppDbContext db,
    IPlanAccess plans,
    IFileStorage storage,
    IOptions<PublicSiteOptions> site) : Controller
{
    /// <summary>Caché de las imágenes públicas: corta, porque el vendedor puede dejar de publicar.</summary>
    public const string ImageCacheControl = "public, max-age=3600";

    /// <summary>Liga vieja del portal ⇒ 301 al catálogo de la portada, con la búsqueda y la página.</summary>
    [HttpGet("")]
    public IActionResult Show(string slug) =>
        RedirectPermanent(site.Value.StorefrontUrlFor(Uri.EscapeDataString(slug)) + Request.QueryString);

    [HttpGet("logo")]
    public Task<IActionResult> Logo(string slug, CancellationToken cancellationToken) =>
        MerchantImageAsync(slug, MerchantImageKind.Logo, cancellationToken);

    [HttpGet("foto")]
    public Task<IActionResult> ShopPhoto(string slug, CancellationToken cancellationToken) =>
        MerchantImageAsync(slug, MerchantImageKind.ShopPhoto, cancellationToken);

    [HttpGet("productos/{id:int}/foto")]
    public Task<IActionResult> ProductPhoto(string slug, int id, CancellationToken cancellationToken) =>
        ProductImageAsync(slug, id, ProductPhotoRules.PhotoFileKey, cancellationToken);

    [HttpGet("productos/{id:int}/miniatura")]
    public Task<IActionResult> ProductThumbnail(string slug, int id, CancellationToken cancellationToken) =>
        ProductImageAsync(slug, id, ProductPhotoRules.ThumbnailFileKey, cancellationToken);

    private async Task<IActionResult> MerchantImageAsync(string slug, MerchantImageKind kind, CancellationToken cancellationToken)
    {
        var merchant = await StorefrontQuery.FindMerchantAsync(db, slug, cancellationToken);
        if (merchant is null || !await StorefrontQuery.IsVisibleAsync(merchant, plans, cancellationToken))
        {
            return NotFound();
        }

        var key = kind == MerchantImageKind.Logo ? merchant.LogoKey : merchant.ShopPhotoKey;
        return key is null
            ? NotFound()
            : await FileAsync(MerchantImageRules.FileKey(kind, key), MerchantImageRules.ContentType(kind), cancellationToken);
    }

    private async Task<IActionResult> ProductImageAsync(
        string slug, int id, Func<string, string> fileKey, CancellationToken cancellationToken)
    {
        var merchant = await StorefrontQuery.FindMerchantAsync(db, slug, cancellationToken);
        if (merchant is null || !await StorefrontQuery.IsVisibleAsync(merchant, plans, cancellationToken))
        {
            return NotFound();
        }

        var photoKey = await db.Products.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.Id == id && p.MerchantId == merchant.Id && !p.IsDeleted)
            .Select(p => p.PhotoKey)
            .SingleOrDefaultAsync(cancellationToken);
        return photoKey is null
            ? NotFound()
            : await FileAsync(fileKey(photoKey), ProductPhotoRules.StoredContentType, cancellationToken);
    }

    private async Task<IActionResult> FileAsync(string key, string contentType, CancellationToken cancellationToken)
    {
        var stream = await storage.OpenReadAsync(key, cancellationToken);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = ImageCacheControl;
        return File(stream, contentType);
    }
}
