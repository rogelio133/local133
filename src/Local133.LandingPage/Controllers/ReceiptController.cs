using Local133.LandingPage.Models;
using Local133.LandingPage.Services;
using Local133.Web.PublicPages;
using Local133.LandingPage.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Local133.LandingPage.Controllers;

/// <summary>
/// Recibo público de una venta pagada (<c>/recibo/{token}</c>): la liga que el vendedor comparte con su cliente. Vive en
/// la portada desde el 2026-11-27, sin encabezado ni pie, y pide sus datos al portal
/// (<see cref="PublicPagesClient.GetReceiptAsync"/>). La liga vieja del portal redirige aquí.
/// </summary>
public class ReceiptController(PublicPagesClient api, ILogger<ReceiptController> logger) : Controller
{
    [HttpGet("recibo/{token}")]
    [EnableRateLimiting(PublicPagesRateLimit.ReceiptPolicy)]
    public async Task<IActionResult> Show(string token, CancellationToken cancellationToken)
    {
        // Liga privada: que no la indexen buscadores ni se filtre en el Referer de los enlaces que tenga.
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.CacheControl = "private, no-store";

        // Un token con otra forma ni siquiera se consulta.
        PublicReceipt? receipt = null;
        if (PublicPagesApi.IsReceiptToken(token))
        {
            try
            {
                receipt = await api.GetReceiptAsync(token, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "El portal no respondió el recibo");
                return Message(PublicPageMessageKind.ReceiptError, StatusCodes.Status503ServiceUnavailable);
            }
        }

        return receipt is null
            ? Message(PublicPageMessageKind.ReceiptNotFound, StatusCodes.Status404NotFound)
            : View(new ReceiptPageViewModel(receipt));
    }

    private ViewResult Message(PublicPageMessageKind kind, int statusCode)
    {
        Response.StatusCode = statusCode;
        return View("PublicMessage", new PublicPageMessageViewModel(kind));
    }
}
