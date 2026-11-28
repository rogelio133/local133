using Local133.Application.Merchants;
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
/// Recibo público de una venta pagada. Desde el 2026-11-27 lo dibuja la portada (<c>{App:LandingUrl}/recibo/{token}</c>)
/// con los datos de <see cref="PublicPagesApiController"/>; aquí quedan la redirección de la liga vieja (las que ya se
/// compartieron por WhatsApp) y el logo del recibo con marca (5.10).
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class ReceiptsController(
    AppDbContext db,
    IPlanAccess plans,
    IFileStorage storage,
    IOptions<PublicSiteOptions> site) : Controller
{
    /// <summary>Liga vieja del portal ⇒ 301 al recibo de la portada (ahí se valida el token).</summary>
    [HttpGet("recibo/{token}")]
    public IActionResult Show(string token)
    {
        SetPrivateHeaders();
        return RedirectPermanent(site.Value.ReceiptUrlFor(Uri.EscapeDataString(token)));
    }

    /// <summary>
    /// Logo del negocio para el recibo con marca (5.10): solo si la venta tiene recibo y el plan del vendedor lo
    /// permite hoy. Cualquier otro caso responde 404.
    /// </summary>
    [HttpGet("recibo/{token}/logo")]
    public async Task<IActionResult> Logo(string token, CancellationToken cancellationToken)
    {
        SetPrivateHeaders();
        var sale = await PublicReceiptQuery.FindSaleAsync(db, token, includeItems: false, cancellationToken);
        if (sale is null)
        {
            return NotFound();
        }

        var logoKey = await db.Merchants.AsNoTracking()
            .Where(m => m.Id == sale.MerchantId)
            .Select(m => m.LogoKey)
            .SingleAsync(cancellationToken);
        if (logoKey is null || !(await plans.GetForMerchantAsync(sale.MerchantId, cancellationToken)).Entitlements.HasBrandedReceipt)
        {
            return NotFound();
        }

        var stream = await storage.OpenReadAsync(MerchantImageRules.FileKey(MerchantImageKind.Logo, logoKey), cancellationToken);
        if (stream is null)
        {
            return NotFound();
        }

        // Corta: si el vendedor pasa a Básico o vence, el logo deja de servirse pronto.
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(stream, MerchantImageRules.LogoContentType);
    }

    /// <summary>Liga privada: que no la indexen buscadores ni se filtre en el Referer de los enlaces que tenga.</summary>
    private void SetPrivateHeaders()
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
