using System.Globalization;
using System.Security.Claims;
using Local133.Application.Admin;
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
/// Ventas y Plan. Las acciones del plan (regalar días, abonar a mano, anular un premio) son de la 6.2.
/// </summary>
[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
[Route("admin/clientes")]
[ApiExplorerSettings(IgnoreApi = true)]
public class CustomersController(
    AppDbContext db, CustomerDetailQuery details, IAdminPlanActions planActions, TimeProvider clock) : Controller
{
    /// <summary>Mensaje del resultado de una acción del plan (6.2).</summary>
    public const string PlanNoticeKey = "admin-plan-notice";

    /// <summary>La acción del plan no se pudo hacer.</summary>
    public const string PlanErrorKey = "admin-plan-error";

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

    [HttpPost("{id:int}/plan/regalar-dias")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GrantDays(
        int id, [Bind(Prefix = "Grant")] GrantDaysInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await PlanWithErrorsAsync(id, input, cancellationToken);
        }

        // Un rechazo del servicio (p. ej. sin prueba, cuando el formulario ni se muestra) va como aviso de la pestaña.
        return AfterAction(id, await planActions.GrantDaysAsync(id, input.Days!.Value, input.Note!, input.Notify, Actor, cancellationToken));
    }

    [HttpPost("{id:int}/plan/cargos/{chargeId:int}/abonar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreditCharge(
        int id, int chargeId, [Bind(Prefix = "Confirm")] PlanNoteInput input, CancellationToken cancellationToken) =>
        AfterAction(id, ModelState.IsValid
            ? await planActions.CreditChargeAsync(id, chargeId, input.Note!, Actor, cancellationToken)
            : InvalidNote());

    [HttpPost("{id:int}/plan/referidos/{referralId:int}/anular")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeReferral(
        int id, int referralId, [Bind(Prefix = "Confirm")] PlanNoteInput input, CancellationToken cancellationToken) =>
        AfterAction(id, ModelState.IsValid
            ? await planActions.RevokeReferralRewardAsync(id, referralId, input.Note!, Actor, cancellationToken)
            : InvalidNote());

    [HttpPost("{id:int}/plan/referidos/premios-pendientes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GrantPendingRewards(int id, CancellationToken cancellationToken) =>
        AfterAction(id, await planActions.GrantPendingReferralRewardsAsync(id, Actor, cancellationToken));

    private AdminActor Actor => new(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!,
        User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty);

    private AdminPlanResult InvalidNote() =>
        new AdminPlanResult.Rejected(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault()
            ?? "Escribe el motivo.");

    /// <summary>PRG: de regreso a la pestaña Plan con el mensaje del resultado.</summary>
    private IActionResult AfterAction(int id, AdminPlanResult result)
    {
        switch (result)
        {
            case AdminPlanResult.NotFound:
                return NotFound();
            case AdminPlanResult.Done done:
                TempData[PlanNoticeKey] = done.Message;
                break;
            case AdminPlanResult.Rejected rejected:
                TempData[PlanErrorKey] = rejected.Message;
                break;
        }

        return Redirect(Url.Content("~" + CustomerUrls.Tab(id, CustomerTab.Plan)));
    }

    private async Task<IActionResult> PlanWithErrorsAsync(int id, GrantDaysInput input, CancellationToken cancellationToken)
    {
        var model = await details.PlanAsync(id, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        model.Grant = input;
        return View(nameof(Plan), model);
    }

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
