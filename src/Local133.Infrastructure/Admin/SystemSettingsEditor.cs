using System.Text.Json;
using Local133.Application.Admin;
using Local133.Domain.Audit;
using Local133.Domain.Settings;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Admin;

/// <summary>
/// Guarda los parámetros globales (6.2): valida con <see cref="SystemSettingsRules"/>, guarda solo si algo cambió y deja
/// en <c>AuditLogs</c> cada campo con su valor anterior y el nuevo. Cambiar un precio solo afecta a los links nuevos:
/// cada cargo guarda su monto.
/// </summary>
internal sealed class SystemSettingsEditor(
    AppDbContext db,
    TimeProvider clock,
    ILogger<SystemSettingsEditor> logger) : ISystemSettingsEditor
{
    public Task<int> AssignedFoundersAsync(CancellationToken cancellationToken = default) =>
        db.Subscriptions.IgnoreQueryFilters().CountAsync(s => s.FounderNumber != null, cancellationToken);

    public async Task<SettingsUpdateResult> UpdateAsync(
        SystemSettings values, AdminActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(actor);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Candado de la fila: dos admins que guardan a la vez no se pisan sin dejar rastro.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM [SystemSettings] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {SystemSettings.SingletonId}",
            cancellationToken);
        var settings = await db.SystemSettings.SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);

        var candidate = new SystemSettings();
        SystemSettingsRules.CopyValues(values, candidate);
        var errors = SystemSettingsRules.Validate(candidate, await AssignedFoundersAsync(cancellationToken));
        if (errors.Count > 0)
        {
            return new SettingsUpdateResult.Invalid(errors);
        }

        var changes = SystemSettingsRules.Changes(settings, candidate);
        if (changes.Count == 0)
        {
            return new SettingsUpdateResult.Unchanged();
        }

        SystemSettingsRules.CopyValues(candidate, settings);
        var termsFields = changes.Select(c => c.Field).Where(SystemSettingsRules.InTerms.Contains).ToList();
        db.AuditLogs.Add(AuditLog.Create(
            actor.UserId, actor.Email, AuditAction.SettingsUpdated, null,
            $"Cambió {(changes.Count == 1 ? "1 parámetro" : $"{changes.Count} parámetros")}: {string.Join(", ", changes.Select(c => $"{c.Field} {c.OldValue} → {c.NewValue}"))}.",
            null,
            JsonSerializer.Serialize(changes),
            clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Admin {ActorUserId} cambió los parámetros {Fields}", actor.UserId, changes.Select(c => c.Field));
        return new SettingsUpdateResult.Saved(changes, termsFields);
    }
}
