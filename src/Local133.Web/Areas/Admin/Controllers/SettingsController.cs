using System.Security.Claims;
using System.Text.Json;
using Local133.Application.Admin;
using Local133.Application.Auth;
using Local133.Domain.Audit;
using Local133.Domain.Common;
using Local133.Domain.Settings;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Admin.Models;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Admin.Controllers;

/// <summary>
/// Parámetros globales (/admin/parametros, 6.2): precios, fundador, prueba, gracia, límites del gratuito, uso justo,
/// referidos, IVA y expiración del QR. Cada guardado queda en <c>AuditLogs</c>; si cambia un número de los Términos,
/// se avisa que hay que publicar otra versión.
/// </summary>
[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
[Route("admin/parametros")]
[ApiExplorerSettings(IgnoreApi = true)]
public class SettingsController(AppDbContext db, ISystemSettingsEditor editor, TimeProvider clock) : Controller
{
    public const string NoticeKey = "admin-settings-notice";

    /// <summary>Campos de los Términos que cambiaron (separados por coma), para la advertencia.</summary>
    public const string TermsWarningKey = "admin-settings-terms";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);
        return View(await BuildAsync(SettingsInput.From(settings), cancellationToken));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([Bind(Prefix = "Input")] SettingsInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await BuildAsync(input, cancellationToken));
        }

        var actor = new AdminActor(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty);
        switch (await editor.UpdateAsync(input.ToSettings(), actor, cancellationToken))
        {
            case SettingsUpdateResult.Invalid invalid:
                foreach (var error in invalid.Errors)
                {
                    ModelState.AddModelError($"Input.{error.Field}", error.Message);
                }

                return View(nameof(Index), await BuildAsync(input, cancellationToken));

            case SettingsUpdateResult.Saved saved:
                TempData[NoticeKey] = saved.Changes.Count == 1
                    ? "Listo: se guardó 1 cambio."
                    : $"Listo: se guardaron {saved.Changes.Count} cambios.";
                if (saved.TermsFieldsChanged.Count > 0)
                {
                    TempData[TermsWarningKey] = string.Join(", ", saved.TermsFieldsChanged.Select(SettingsLabels.Label));
                }

                break;

            default:
                TempData[NoticeKey] = "No había cambios que guardar.";
                break;
        }

        return Redirect(Url.Content("~/admin/parametros"));
    }

    private async Task<SettingsViewModel> BuildAsync(SettingsInput input, CancellationToken cancellationToken)
    {
        var today = MexicoTime.Today(clock.GetUtcNow());
        var recent = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.SettingsUpdated)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Take(SettingsViewModel.RecentChanges)
            .ToListAsync(cancellationToken);
        return new SettingsViewModel
        {
            Input = input,
            AssignedFounders = await editor.AssignedFoundersAsync(cancellationToken),
            Recent = recent.Select(a => new SettingsChangeRow(
                    SaleFormats.DateTime(a.OccurredAt, today),
                    a.ActorEmail,
                    a.Details is { } details ? JsonSerializer.Deserialize<List<SettingsChange>>(details) ?? [] : []))
                .ToList(),
        };
    }
}
