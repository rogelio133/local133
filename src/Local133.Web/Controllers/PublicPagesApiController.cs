using Local133.Application.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Hosting;
using Local133.Web.Models;
using Local133.Web.PublicPages;
using Local133.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Local133.Web.Controllers;

/// <summary>
/// API pública de solo lectura (ajustes 2026-11-27) con la que la portada (<c>Local133.LandingPage</c>, sin base) dibuja
/// el recibo y el catálogo públicos. Expone lo mismo que esas páginas ya mostraban: nunca el celular del cliente, la
/// CLABE, el folio ni ids de Pey. Las reglas (plan, marca, visibilidad) se deciden aquí; las imágenes las sigue
/// sirviendo el portal, con URLs absolutas. Solo la llama el servidor de la portada, con la llave compartida
/// (<see cref="PublicApiKeyFilter"/>); a quien no la trae, 401.
/// </summary>
[ApiController]
[AllowAnonymous]
[TypeFilter(typeof(PublicApiKeyFilter))]
[Route("api/public")]
[ApiExplorerSettings(IgnoreApi = true)]
public class PublicPagesApiController(
    AppDbContext db,
    IPlanAccess plans,
    IOptions<PublicSiteOptions> site,
    TimeProvider clock) : ControllerBase
{
    /// <summary>Recibo de una venta pagada; 404 si el token no existe, tiene otra forma o la venta no está pagada.</summary>
    [HttpGet("recibos/{token}")]
    public async Task<ActionResult<PublicReceipt>> Receipt(string token, CancellationToken cancellationToken)
    {
        SetPrivateHeaders();
        Response.Headers.CacheControl = "private, no-store";

        var sale = await PublicReceiptQuery.FindSaleAsync(db, token, includeItems: true, cancellationToken);
        if (sale is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        return await PublicReceiptQuery.BuildAsync(
            db, plans, sale, site.Value.BaseUrlFor(Request), clock.GetUtcNow(), cancellationToken);
    }

    /// <summary>
    /// Catálogo de un vendedor. 404 si la liga no existe; si existe pero no se ve, 200 con <c>available: false</c> y el
    /// nombre del negocio (la portada dice «Catálogo no disponible por ahora»).
    /// </summary>
    [HttpGet("tiendas/{slug}")]
    public async Task<ActionResult<PublicStorefront>> Storefront(
        string slug, string? q, [FromQuery(Name = PageInfo.QueryKey)] int? page, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";

        var merchant = await StorefrontQuery.FindMerchantAsync(db, slug, cancellationToken);
        if (merchant is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (!await StorefrontQuery.IsVisibleAsync(merchant, plans, cancellationToken))
        {
            return PublicStorefront.Unavailable(merchant.BusinessName is { Length: > 0 } name ? name.Trim() : null);
        }

        var model = await StorefrontQuery.BuildAsync(
            db, merchant, q, page, StorefrontQuery.PublicImages(merchant.PublicSlug!, site.Value.BaseUrlFor(Request)),
            StorefrontUrls.Index(merchant.PublicSlug!), madeWithUrl: string.Empty, isPreview: false,
            cancellationToken);
        return StorefrontQuery.ToPublic(model, merchant.ReferralCode);
    }

    /// <summary>Datos de una liga privada: que no se indexen ni se filtren en el Referer.</summary>
    private void SetPrivateHeaders()
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
