using Local133.Application.Merchants;
using Local133.Application.Products;
using Local133.Application.Storage;
using Local133.Application.Subscriptions;
using Local133.Domain.Merchants;
using Local133.Infrastructure.Persistence;
using Local133.Web.Hosting;
using Local133.Web.Models;
using Local133.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Local133.Web.Controllers;

/// <summary>
/// Catálogo público del vendedor (<c>/tienda/{slug}</c>, 5.10): nombre, logo, foto del local, productos vigentes con
/// precio y foto, y «Pedir por WhatsApp» al celular del negocio. Sin pedidos ni pagos en línea. Solo se ve si el
/// vendedor lo publicó, su plan lo permite (Pro) y no está suspendido ni dado de baja
/// (<see cref="Merchant.CanShowPublicCatalog(Domain.Subscriptions.PlanEntitlements)"/>); si no, la liga dice «Catálogo
/// no disponible por ahora» con <c>noindex</c>. Las imágenes se sirven aquí con la misma regla. Nunca muestra CLABE ni
/// ids de Pey.
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

    [HttpGet("")]
    public async Task<IActionResult> Show(string slug, string? q, [FromQuery(Name = PageInfo.QueryKey)] int? page, CancellationToken cancellationToken)
    {
        var merchant = await FindMerchantAsync(slug, cancellationToken);
        if (merchant is null || !await IsVisibleAsync(merchant, cancellationToken))
        {
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            Response.Headers.CacheControl = "no-cache";
            if (merchant is null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
            }

            return View("Unavailable", new StorefrontUnavailableViewModel(
                merchant?.BusinessName is { Length: > 0 } name ? name.Trim() : null));
        }

        Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        Response.Headers.CacheControl = "no-cache";
        var model = await StorefrontQuery.BuildAsync(
            db, merchant, q, page, StorefrontQuery.PublicImages(merchant.PublicSlug!), StorefrontUrls.Index(merchant.PublicSlug!),
            site.Value.LandingUrlFor(merchant.ReferralCode), isPreview: false, cancellationToken);
        return View(model);
    }

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
        var merchant = await FindMerchantAsync(slug, cancellationToken);
        if (merchant is null || !await IsVisibleAsync(merchant, cancellationToken))
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
        var merchant = await FindMerchantAsync(slug, cancellationToken);
        if (merchant is null || !await IsVisibleAsync(merchant, cancellationToken))
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

    /// <summary>Vendedor dueño del slug (sin distinguir mayúsculas), o <c>null</c>.</summary>
    private async Task<Merchant?> FindMerchantAsync(string slug, CancellationToken cancellationToken)
    {
        var normalized = slug.ToLowerInvariant();
        return PublicSlug.IsValid(normalized)
            ? await db.Merchants.AsNoTracking().SingleOrDefaultAsync(m => m.PublicSlug == normalized, cancellationToken)
            : null;
    }

    private async Task<bool> IsVisibleAsync(Merchant merchant, CancellationToken cancellationToken) =>
        merchant.PublicCatalogEnabled
        && merchant.CanShowPublicCatalog((await plans.GetForMerchantAsync(merchant.Id, cancellationToken)).Entitlements);
}
