using System.Security.Claims;
using Local133.Application.Admin;
using Local133.Application.Auth;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Admin.Models;
using Local133.Web.Areas.Admin.Services;
using Local133.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Areas.Admin.Controllers;

/// <summary>
/// Bitácora de webhooks de Pey (/admin/webhooks, 6.4): lista con filtros, detalle con el payload enmascarado y
/// reproceso de un evento (fallido, sin procesar o ignorado) con su motivo, que queda en <c>AuditLogs</c>.
/// </summary>
[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
[Route("admin/webhooks")]
[ApiExplorerSettings(IgnoreApi = true)]
public class WebhooksController(AppDbContext db, IAdminWebhookActions actions, TimeProvider clock) : Controller
{
    /// <summary>Mensaje del resultado del reproceso.</summary>
    public const string NoticeKey = "admin-webhook-notice";

    /// <summary>El reproceso no dejó el evento procesado (o no se pudo hacer).</summary>
    public const string ErrorKey = "admin-webhook-error";

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "q")] string? q,
        [FromQuery(Name = WebhookStatusFilters.QueryKey)] string? status,
        [FromQuery(Name = WebhookTypeFilters.QueryKey)] string? type,
        [FromQuery(Name = "desde")] string? from,
        [FromQuery(Name = "hasta")] string? to,
        [FromQuery(Name = PageInfo.QueryKey)] int? page,
        CancellationToken cancellationToken)
    {
        var filters = new WebhookFilters(
            AdminDates.Query(q, WebhookFilters.QueryMaxLength),
            WebhookStatusFilters.Parse(status),
            WebhookTypeFilters.Parse(type),
            AdminDates.Parse(from),
            AdminDates.Parse(to));
        return View(await WebhookLogQuery.ListAsync(db, filters, page, clock.GetUtcNow(), cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken) =>
        await WebhookLogQuery.DetailAsync(db, id, clock.GetUtcNow(), cancellationToken) is { } model ? View(model) : NotFound();

    [HttpPost("{id:int}/reprocesar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reprocess(
        int id, [Bind(Prefix = "Confirm")] PlanNoteInput input, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid
            ? await actions.ReprocessAsync(id, input.Note!, Actor, cancellationToken)
            : new AdminPlanResult.Rejected(
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Escribe el motivo.");
        switch (result)
        {
            case AdminPlanResult.NotFound:
                return NotFound();
            case AdminPlanResult.Done done:
                TempData[NoticeKey] = done.Message;
                break;
            case AdminPlanResult.Rejected rejected:
                TempData[ErrorKey] = rejected.Message;
                break;
        }

        return Redirect(Url.Content("~" + WebhookUrls.Detail(id)));
    }

    private AdminActor Actor => new(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!,
        User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty);
}
