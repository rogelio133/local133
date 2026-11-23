using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Controllers;

/// <summary>
/// Recibo público de una venta pagada (<c>/recibo/{token}</c>): la liga que el vendedor comparte con su cliente. No
/// requiere sesión y nunca muestra el celular, la CLABE ni ids de Pey.
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class ReceiptsController(AppDbContext db, TimeProvider clock) : Controller
{
    [HttpGet("recibo/{token}")]
    public async Task<IActionResult> Show(string token, CancellationToken cancellationToken)
    {
        // Liga privada: que no la indexen buscadores ni se filtre en el Referer de los enlaces que tenga.
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.CacheControl = "private, no-store";

        if (!Sale.IsValidReceiptToken(token))
        {
            return NotFound();
        }

        // Sin filtro multi-tenant: quien abre la liga puede tener sesión de otro vendedor (o de admin). El token es la
        // única llave.
        var sale = await db.Sales.AsNoTracking().IgnoreQueryFilters()
            .Include(s => s.Items)
            .SingleOrDefaultAsync(s => s.ReceiptToken == token, cancellationToken);
        if (sale is null || !sale.HasReceipt)
        {
            return NotFound();
        }

        var businessName = await db.Merchants.AsNoTracking()
            .Where(m => m.Id == sale.MerchantId)
            .Select(m => m.BusinessName)
            .SingleAsync(cancellationToken);
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
                .ToList()));
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
}

/// <summary>Pago en el recibo público.</summary>
public sealed record ReceiptPaymentView(string PaidAtText, string AmountText, string? TrackingKey);
