using Local133.LandingPage.Models;
using Local133.LandingPage.Services;
using Local133.Web.PublicPages;
using Local133.LandingPage.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Local133.LandingPage.Controllers;

/// <summary>
/// Catálogo público del vendedor (<c>/tienda/{slug}</c>, 5.10): nombre, logo, foto del local, productos vigentes con
/// precio y foto, y «Pedir por WhatsApp». Vive en la portada desde el 2026-11-27, con la barra rosa del logo arriba y sin
/// pie, y pide sus datos al portal (<see cref="PublicPagesClient.GetStorefrontAsync"/>), que decide si se ve. Si no se
/// ve, «Catálogo no disponible por ahora» con <c>noindex</c> (404 si la liga no existe). La liga vieja del portal
/// redirige aquí.
/// </summary>
public class StorefrontController(PublicPagesClient api, ILogger<StorefrontController> logger) : Controller
{
    [HttpGet("tienda/{slug}")]
    [EnableRateLimiting(PublicPagesRateLimit.StorefrontPolicy)]
    public async Task<IActionResult> Show(
        string slug, string? q, [FromQuery(Name = PublicPagesApi.PageQueryKey)] int? page, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";

        // La liga no distingue mayúsculas (el portal también normaliza).
        slug = slug.ToLowerInvariant();
        var query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (query is { Length: > PublicStorefront.QueryMaxLength })
        {
            query = query[..PublicStorefront.QueryMaxLength];
        }

        PublicStorefront? storefront;
        try
        {
            storefront = await api.GetStorefrontAsync(slug, query, page, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "El portal no respondió el catálogo {Slug}", slug);
            return Message(new PublicPageMessageViewModel(PublicPageMessageKind.StorefrontError), StatusCodes.Status503ServiceUnavailable);
        }

        if (storefront is not { Available: true })
        {
            return Message(
                new PublicPageMessageViewModel(PublicPageMessageKind.StorefrontUnavailable, storefront?.BusinessName),
                storefront is null ? StatusCodes.Status404NotFound : StatusCodes.Status200OK);
        }

        Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        return View(new StorefrontPageViewModel(storefront, PublicPagesApi.StorefrontPathPrefix + Uri.EscapeDataString(slug)));
    }

    private ViewResult Message(PublicPageMessageViewModel model, int statusCode)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.StatusCode = statusCode;
        return View("PublicMessage", model);
    }
}
