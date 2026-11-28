using Local133.Application.Auth;
using Local133.Application.Subscriptions;
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
/// Ventas (/app/ventas): resumen del día, historial con filtros y búsqueda, detalle de cada venta con sus productos,
/// sus pagos y el recibo para compartir; corte de caja diario (Básico y Pro) y exportación a Excel (Pro, 5.9). Solo
/// lectura: lo ve cualquier vendedor, también suspendido, dado de baja o con el plan vencido.
/// </summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/ventas")]
[ApiExplorerSettings(IgnoreApi = true)]
public class SalesController(AppDbContext db, IPlanAccess plans, ReceiptLinks receiptLinks, TimeProvider clock) : Controller
{
    /// <summary>Mensaje (TempData) que la lista muestra una vez (p. ej. una exportación demasiado grande).</summary>
    public const string ListNoticeKey = "SalesListNotice";

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = SaleStatusFilters.QueryKey)] string? status,
        [FromQuery(Name = "desde")] string? from,
        [FromQuery(Name = "hasta")] string? to,
        [FromQuery(Name = SaleSearch.QueryKey)] string? query,
        [FromQuery(Name = PageInfo.QueryKey)] int? page,
        CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        var model = await SalesHistoryQuery.BuildAsync(
            db, merchant, plan, Filters(status, from, to, query), page, clock.GetUtcNow(), cancellationToken);
        return View(model);
    }

    /// <summary>
    /// Exportación a Excel (5.9, Pro) con los mismos filtros que la lista, sin paginar. En Básico o vencido ⇒ 403 (el botón
    /// no aparece; la regla se aplica aquí). Más de <see cref="SalesWorkbook.MaxRows"/> ventas ⇒ regresa a la lista
    /// con un aviso para acotar las fechas.
    /// </summary>
    [HttpGet("exportar")]
    public async Task<IActionResult> Export(
        [FromQuery(Name = SaleStatusFilters.QueryKey)] string? status,
        [FromQuery(Name = "desde")] string? from,
        [FromQuery(Name = "hasta")] string? to,
        [FromQuery(Name = SaleSearch.QueryKey)] string? query,
        CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        if (!plan.Entitlements.CanExportHistory)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var now = clock.GetUtcNow();
        var filtered = SalesHistoryQuery.Filter(db, merchant.Id, Filters(status, from, to, query), now);
        if (await filtered.Sales.CountAsync(cancellationToken) > SalesWorkbook.MaxRows)
        {
            TempData[ListNoticeKey] = SalesWorkbook.TooManyRowsMessage;
            return Redirect(Url.Content("~" + SalesUrls.List) + Request.QueryString);
        }

        var sales = await filtered.Sales
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Select(s => new
            {
                s.Folio,
                s.CreatedAt,
                s.Concept,
                s.Method,
                s.Source,
                s.Status,
                s.ExpiresAt,
                s.Amount,
                s.PaidAt,
                Collected = db.Payments.Where(p => p.SaleId == s.Id).Sum(p => (decimal?)p.Amount) ?? 0m,
                Items = s.Items.OrderBy(i => i.Id).Select(i => new { i.Name, i.Quantity }).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var rows = sales.Select(s => new SalesExportRow(
            s.Folio,
            MexicoTime.ToLocal(s.CreatedAt).DateTime,
            Sale.ConceptTextOf(s.Concept),
            SaleChannelText.Method(s.Method),
            SaleChannelText.Source(s.Source),
            SaleStatusText.Label(SalesHistoryQuery.EffectiveStatus(s.Status, s.ExpiresAt, now)),
            s.Amount,
            s.Collected,
            s.PaidAt is { } paidAt ? MexicoTime.ToLocal(paidAt).DateTime : null,
            s.Items.Count == 0 ? null : string.Join("; ", s.Items.Select(i => $"{i.Quantity:N0} × {i.Name}"))));

        var stream = new MemoryStream();
        SalesWorkbook.Write(stream, rows);
        stream.Position = 0;
        Response.Headers.CacheControl = "no-store";
        return File(stream, SalesWorkbook.ContentType, SalesWorkbook.FileName(filtered.Filters, MexicoTime.Today(now)));
    }

    /// <summary>
    /// Corte de caja de un día (5.9; Básico y Pro; <c>?fecha=AAAA-MM-DD</c>, sin fecha o con una futura ⇒ hoy en
    /// México). Con el plan vencido responde 200 con el aviso de elegir plan (5.14).
    /// </summary>
    [HttpGet("corte")]
    public async Task<IActionResult> CashCut([FromQuery(Name = "fecha")] string? date, CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        if (!plan.Entitlements.HasDailyCashCut)
        {
            return View("CashCutLocked", PlanExpiredViewModel.For(plan, clock.GetUtcNow(),
                "Elige un plan para volver a ver tu corte de caja. Tus ventas siguen en tu historial."));
        }

        var now = clock.GetUtcNow();
        var today = MexicoTime.Today(now);
        var day = ParseDate(date) is { } parsed && parsed < today ? parsed : today;
        return View(await CashCutQuery.BuildAsync(db, merchant, day, now, cancellationToken));
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
        var now = clock.GetUtcNow();
        var today = MexicoTime.Today(now);

        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.SaleId == sale.Id)
            .OrderBy(p => p.PaidAt).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        var status = sale.EffectiveStatus(now);
        var amountText = SaleQrViewModel.FormatAmount(sale.Amount);
        var model = new SaleDetailViewModel(
            sale.Id,
            sale.Folio,
            amountText,
            Sale.ConceptTextOf(sale.Concept),
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
                ? receiptLinks.ShareFor(sale.ReceiptToken, merchant.BusinessName, sale.Folio, amountText,
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

        // En todos los planes, también vencido (5.14): el recibo es de una venta que ya existe.
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var message = ReceiptLinks.Message(
            merchant.BusinessName, sale.Folio, SaleQrViewModel.FormatAmount(sale.Amount), receiptLinks.UrlFor(sale.ReceiptToken));
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Redirect(ReceiptLinks.WhatsAppUrlFor(destination, message, sale.PhoneNumber));
    }

    private static SalesHistoryFilters Filters(string? status, string? from, string? to, string? query) =>
        new(SaleStatusFilters.Parse(status), ParseDate(from), ParseDate(to), query);

    /// <summary>Primer día que se acepta en los filtros (evita fechas absurdas como el año 1).</summary>
    private static readonly DateOnly MinDate = new(2020, 1, 1);

    /// <summary>Fecha <c>AAAA-MM-DD</c> (la que manda <c>input type=date</c>); cualquier otra cosa se ignora.</summary>
    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) && date >= MinDate
            ? date
            : null;
}
