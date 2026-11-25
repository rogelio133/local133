using Local133.Application.Auth;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Admin.Models;
using Local133.Web.Areas.Admin.Services;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Areas.Admin.Controllers;

/// <summary>
/// Transacciones globales del admin (/admin/transacciones, 6.4): ventas de todos los vendedores con su detalle, y los
/// pagos de la suscripción. Solo lectura: no llama a Pey.
/// </summary>
[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
[Route("admin/transacciones")]
[ApiExplorerSettings(IgnoreApi = true)]
public class TransactionsController(AppDbContext db, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "q")] string? q,
        [FromQuery(Name = SaleStatusFilters.QueryKey)] string? status,
        [FromQuery(Name = SaleMethodFilters.QueryKey)] string? method,
        [FromQuery(Name = "desde")] string? from,
        [FromQuery(Name = "hasta")] string? to,
        [FromQuery(Name = TransactionFilters.MerchantKey)] int? merchantId,
        [FromQuery(Name = PageInfo.QueryKey)] int? page,
        CancellationToken cancellationToken)
    {
        var filters = new TransactionFilters(
            AdminDates.Query(q, TransactionFilters.QueryMaxLength),
            SaleStatusFilters.Parse(status),
            SaleMethodFilters.Parse(method),
            AdminDates.Parse(from),
            AdminDates.Parse(to),
            merchantId is > 0 ? merchantId : null);
        return View(await TransactionListQuery.SalesAsync(db, filters, page, clock.GetUtcNow(), cancellationToken));
    }

    [HttpGet("ventas/{id:int}")]
    public async Task<IActionResult> Sale(int id, CancellationToken cancellationToken) =>
        await TransactionListQuery.SaleAsync(db, id, clock.GetUtcNow(), cancellationToken) is { } model ? View(model) : NotFound();

    [HttpGet("suscripciones")]
    public async Task<IActionResult> Subscriptions(
        [FromQuery(Name = "q")] string? q,
        [FromQuery(Name = SubscriptionPaymentMethodFilters.QueryKey)] string? method,
        [FromQuery(Name = "desde")] string? from,
        [FromQuery(Name = "hasta")] string? to,
        [FromQuery(Name = PageInfo.QueryKey)] int? page,
        CancellationToken cancellationToken)
    {
        var filters = new SubscriptionPaymentFilters(
            AdminDates.Query(q, SubscriptionPaymentFilters.QueryMaxLength),
            SubscriptionPaymentMethodFilters.Parse(method),
            AdminDates.Parse(from),
            AdminDates.Parse(to));
        return View(await TransactionListQuery.SubscriptionPaymentsAsync(db, filters, page, clock.GetUtcNow(), cancellationToken));
    }
}
