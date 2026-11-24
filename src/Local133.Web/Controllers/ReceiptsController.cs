using Local133.Application.Merchants;
using Local133.Application.Storage;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Local133.Web.Controllers;

/// <summary>
/// Recibo público de una venta pagada (<c>/recibo/{token}</c>): la liga que el vendedor comparte con su cliente. No
/// requiere sesión y nunca muestra el celular, la CLABE ni ids de Pey.
/// <para>
/// Marca (5.10), según el plan del vendedor <b>al abrir la liga</b>: con Pro lleva el logo del negocio (servido por
/// <c>/recibo/{token}/logo</c>); en el gratuito dice «Cobrado con Local133», con liga a la portada y el código de
/// invitación del vendedor.
/// </para>
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class ReceiptsController(
    AppDbContext db,
    IPlanAccess plans,
    IFileStorage storage,
    IOptions<PublicSiteOptions> site,
    TimeProvider clock) : Controller
{
    [HttpGet("recibo/{token}")]
    public async Task<IActionResult> Show(string token, CancellationToken cancellationToken)
    {
        SetPrivateHeaders();
        Response.Headers.CacheControl = "private, no-store";

        // Sin filtro multi-tenant: quien abre la liga puede tener sesión de otro vendedor (o de admin). El token es la
        // única llave.
        var sale = await FindReceiptSaleAsync(token, includeItems: true, cancellationToken);
        if (sale is null)
        {
            return NotFound();
        }

        var merchant = await db.Merchants.AsNoTracking()
            .Where(m => m.Id == sale.MerchantId)
            .Select(m => new { m.BusinessName, m.LogoKey, m.ReferralCode })
            .SingleAsync(cancellationToken);
        var businessName = merchant.BusinessName;
        var plan = await plans.GetForMerchantAsync(sale.MerchantId, cancellationToken);
        var branded = plan.Entitlements.HasBrandedReceipt;
        var payments = await db.Payments.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.SaleId == sale.Id)
            .OrderBy(p => p.PaidAt).ThenBy(p => p.Id)
            .Select(p => new { p.PaidAt, p.Amount, p.TrackingKey })
            .ToListAsync(cancellationToken);

        var today = MexicoTime.Today(clock.GetUtcNow());
        var paidTotal = payments.Sum(p => p.Amount);
        return View(new ReceiptViewModel(
            string.IsNullOrWhiteSpace(businessName) ? null : businessName.Trim(),
            sale.Folio,
            SaleQrViewModel.FormatAmount(paidTotal > 0 ? paidTotal : sale.Amount),
            Sale.ConceptTextOf(sale.Concept),
            sale.PaidAt is { } paidAt ? SaleFormats.DateTime(paidAt, today) : null,
            sale.Items.OrderBy(i => i.Id)
                .Select(i => new SaleItemView(i.Name, i.Quantity, SaleQrViewModel.FormatAmount(i.UnitPrice), SaleQrViewModel.FormatAmount(i.Amount)))
                .ToList(),
            payments
                .Select(p => new ReceiptPaymentView(SaleFormats.DateTime(p.PaidAt, today), SaleQrViewModel.FormatAmount(p.Amount), p.TrackingKey))
                .ToList())
        {
            LogoUrl = branded && merchant.LogoKey is { } logoKey ? LogoPathFor(token, logoKey) : null,
            PoweredByUrl = branded ? null : site.Value.LandingUrlFor(merchant.ReferralCode),
        });
    }

    /// <summary>
    /// Logo del negocio para el recibo con marca (5.10): solo si la venta tiene recibo y el plan del vendedor lo
    /// permite hoy. Cualquier otro caso responde 404.
    /// </summary>
    [HttpGet("recibo/{token}/logo")]
    public async Task<IActionResult> Logo(string token, CancellationToken cancellationToken)
    {
        SetPrivateHeaders();
        var sale = await FindReceiptSaleAsync(token, includeItems: false, cancellationToken);
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

        // Corta: si el vendedor baja al gratuito, el logo deja de servirse pronto.
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(stream, MerchantImageRules.LogoContentType);
    }

    /// <summary>Ruta del logo del recibo, con la versión de la imagen.</summary>
    public static string LogoPathFor(string token, string logoKey) =>
        $"/recibo/{token}/logo?v={MerchantImageRules.Version(logoKey)}";

    /// <summary>Liga privada: que no la indexen buscadores ni se filtre en el Referer de los enlaces que tenga.</summary>
    private void SetPrivateHeaders()
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    /// <summary>La venta con recibo de ese token (sin filtro multi-tenant), o <c>null</c>.</summary>
    private async Task<Sale?> FindReceiptSaleAsync(string token, bool includeItems, CancellationToken cancellationToken)
    {
        if (!Sale.IsValidReceiptToken(token))
        {
            return null;
        }

        var query = db.Sales.AsNoTracking().IgnoreQueryFilters();
        if (includeItems)
        {
            query = query.Include(s => s.Items);
        }

        var sale = await query.SingleOrDefaultAsync(s => s.ReceiptToken == token, cancellationToken);
        return sale is { HasReceipt: true } ? sale : null;
    }
}

/// <summary>Recibo público. Solo lo que el cliente necesita: quién cobró, cuánto, cuándo, qué y la clave de rastreo.</summary>
/// <param name="AmountText">Total pagado (suma de los pagos recibidos).</param>
public sealed record ReceiptViewModel(
    string? BusinessName,
    int Folio,
    string AmountText,
    string? ConceptText,
    string? PaidAtText,
    IReadOnlyList<SaleItemView> Items,
    IReadOnlyList<ReceiptPaymentView> Payments)
{
    public bool IsCart => Items.Count > 0;

    /// <summary>Logo del negocio (recibo con marca, Pro con logo), o <c>null</c>.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>
    /// Portada con el código de invitación del vendedor para «Cobrado con Local133» (gratuito), o <c>null</c> en Pro.
    /// </summary>
    public string? PoweredByUrl { get; init; }
}

/// <summary>Pago en el recibo público.</summary>
public sealed record ReceiptPaymentView(string PaidAtText, string AmountText, string? TrackingKey);
