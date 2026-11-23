using System.Globalization;
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
/// Clientes del admin (/admin/clientes, 6.1): listado con búsqueda y filtros, y detalle con pestañas General, Cuenta,
/// Ventas y Plan. Solo lectura.
/// </summary>
[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
[Route("admin/clientes")]
[ApiExplorerSettings(IgnoreApi = true)]
public class CustomersController(AppDbContext db, CustomerDetailQuery details, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "q")] string? q,
        [FromQuery(Name = MerchantStatusFilters.QueryKey)] string? status,
        [FromQuery(Name = PlanTierFilters.QueryKey)] string? plan,
        [FromQuery(Name = ExpiryFilters.QueryKey)] string? expiry,
        [FromQuery(Name = "desde")] string? from,
        [FromQuery(Name = "hasta")] string? to,
        [FromQuery(Name = PageInfo.QueryKey)] int? page,
        CancellationToken cancellationToken) =>
        View(await BuildListAsync(q, status, plan, expiry, from, to, page, cancellationToken));

    /// <summary>Fragmento con el conteo, la lista y la paginación (búsqueda sin recargar, <c>admin-customer-search.js</c>).</summary>
    [HttpGet("resultados")]
    public async Task<IActionResult> Results(
        [FromQuery(Name = "q")] string? q,
        [FromQuery(Name = MerchantStatusFilters.QueryKey)] string? status,
        [FromQuery(Name = PlanTierFilters.QueryKey)] string? plan,
        [FromQuery(Name = ExpiryFilters.QueryKey)] string? expiry,
        [FromQuery(Name = "desde")] string? from,
        [FromQuery(Name = "hasta")] string? to,
        [FromQuery(Name = PageInfo.QueryKey)] int? page,
        CancellationToken cancellationToken) =>
        PartialView("_CustomerResults", await BuildListAsync(q, status, plan, expiry, from, to, page, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> General(int id, CancellationToken cancellationToken) =>
        Show(await details.GeneralAsync(id, cancellationToken));

    [HttpGet("{id:int}/cuenta")]
    public async Task<IActionResult> Account(int id, CancellationToken cancellationToken) =>
        Show(await details.AccountAsync(id, cancellationToken));

    [HttpGet("{id:int}/ventas")]
    public async Task<IActionResult> Sales(
        int id,
        [FromQuery(Name = SaleStatusFilters.QueryKey)] string? status,
        [FromQuery(Name = PageInfo.QueryKey)] int? page,
        CancellationToken cancellationToken) =>
        Show(await details.SalesAsync(id, SaleStatusFilters.Parse(status), page, cancellationToken));

    [HttpGet("{id:int}/plan")]
    public async Task<IActionResult> Plan(int id, CancellationToken cancellationToken) =>
        Show(await details.PlanAsync(id, cancellationToken));

    private IActionResult Show(object? model) => model is null ? NotFound() : View(model);

    private Task<CustomerListViewModel> BuildListAsync(
        string? q, string? status, string? plan, string? expiry, string? from, string? to, int? page,
        CancellationToken cancellationToken)
    {
        var text = q?.Trim();
        if (text is { Length: > CustomerFilters.QueryMaxLength })
        {
            text = text[..CustomerFilters.QueryMaxLength];
        }

        var filters = new CustomerFilters(
            string.IsNullOrEmpty(text) ? null : text,
            MerchantStatusFilters.Parse(status),
            PlanTierFilters.Parse(plan),
            ExpiryFilters.Parse(expiry),
            ParseDate(from),
            ParseDate(to));
        return CustomerListQuery.BuildAsync(db, filters, page, clock.GetUtcNow(), cancellationToken);
    }

    /// <summary>Fecha <c>AAAA-MM-DD</c> (la que manda <c>input type=date</c>); cualquier otra cosa se ignora.</summary>
    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
