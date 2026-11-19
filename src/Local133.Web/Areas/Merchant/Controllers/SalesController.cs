using Local133.Application.Auth;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Areas.Merchant.Services;
using Local133.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>
/// Ventas (/app/ventas): resumen del día, historial con filtros y detalle de cada venta con sus productos, sus pagos
/// y el recibo para compartir. Solo lectura: lo ve cualquier vendedor, también suspendido o dado de baja.
/// </summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/ventas")]
[ApiExplorerSettings(IgnoreApi = true)]
public class SalesController(AppDbContext db, ReceiptLinks receiptLinks, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = SaleStatusFilters.QueryKey)] string? status,
        [FromQuery(Name = "desde")] string? from,
        [FromQuery(Name = "hasta")] string? to,
        [FromQuery(Name = PageInfo.QueryKey)] int? page,
        CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var model = await SalesHistoryQuery.BuildAsync(
            db, merchant, SaleStatusFilters.Parse(status), ParseDate(from), ParseDate(to), page, clock.GetUtcNow(),
            cancellationToken);
        return View(model);
    }

    /// <summary>Detalle. El filtro multi-tenant de EF Core hace que la venta de otro vendedor dé 404.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken)
    {
        var sale = await db.Sales.AsNoTracking().Include(s => s.Items).SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sale is null)
        {
            return NotFound();
        }

        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.SaleId == sale.Id)
            .OrderBy(p => p.PaidAt).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();
        var today = MexicoTime.Today(now);
        var status = sale.EffectiveStatus(now);
        var amountText = SaleQrViewModel.FormatAmount(sale.Amount);
        var model = new SaleDetailViewModel(
            sale.Id,
            sale.Folio,
            amountText,
            Sale.ConceptTextOf(sale.Concept, merchant.BusinessName),
            status,
            sale.Method,
            sale.Source,
            sale.PhoneNumber is null ? null : PhoneNumber.Mask(sale.PhoneNumber),
            SaleFormats.DateTime(sale.CreatedAt, today),
            sale.PaidAt is { } paidAt ? SaleFormats.DateTime(paidAt, today) : null,
            status == SaleStatus.Pending && sale.HasPaymentRequest,
            sale.Items.OrderBy(i => i.Id)
                .Select(i => new SaleItemView(i.Name, i.Quantity, SaleQrViewModel.FormatAmount(i.UnitPrice), SaleQrViewModel.FormatAmount(i.Amount)))
                .ToList(),
            payments
                .Select(p => new SalePaymentView(
                    SaleFormats.DateTime(p.PaidAt, today),
                    SaleQrViewModel.FormatAmount(p.Amount),
                    p.TrackingKey))
                .ToList(),
            sale.HasReceipt
                ? receiptLinks.ShareFor(Request, sale.ReceiptToken, merchant.BusinessName, sale.Folio, amountText,
                    sale.PhoneNumber, SalesUrls.CustomerWhatsApp(sale.Id))
                : null);
        return View(model);
    }

    /// <summary>
    /// Abre WhatsApp con el recibo en el chat del celular al que se cobró. Es una redirección para que el número
    /// completo no quede escrito en ninguna página (solo se muestra enmascarado). Sin recibo o sin celular ⇒ 404.
    /// </summary>
    /// <param name="destination">
    /// <c>app</c> = app de escritorio (<c>whatsapp://</c>), <c>web</c> = WhatsApp Web; sin valor, <c>wa.me</c> (celular).
    /// </param>
    [HttpGet("{id:int}/whatsapp")]
    public async Task<IActionResult> CustomerWhatsApp(
        int id, [FromQuery(Name = "destino")] string? destination, CancellationToken cancellationToken)
    {
        var sale = await db.Sales.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sale is null || !sale.HasReceipt || sale.PhoneNumber is null || !PhoneNumber.IsValid(sale.PhoneNumber))
        {
            return NotFound();
        }

        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var message = ReceiptLinks.Message(
            merchant.BusinessName, sale.Folio, SaleQrViewModel.FormatAmount(sale.Amount), receiptLinks.UrlFor(Request, sale.ReceiptToken));
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Redirect(ReceiptLinks.WhatsAppUrlFor(destination, message, sale.PhoneNumber));
    }

    /// <summary>Fecha <c>AAAA-MM-DD</c> (la que manda <c>input type=date</c>); cualquier otra cosa se ignora.</summary>
    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : null;
}
