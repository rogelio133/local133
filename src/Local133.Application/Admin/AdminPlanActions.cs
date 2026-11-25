using Local133.Domain.Audit;
using Local133.Domain.Settings;

namespace Local133.Application.Admin;

/// <summary>Límites de las acciones del admin sobre el plan (6.2).</summary>
public static class AdminPlanLimits
{
    /// <summary>Máximo de días que se regalan de una vez.</summary>
    public const int MaxGrantDays = 365;

    public const int NoteMaxLength = AuditLog.NoteMaxLength;
}

/// <summary>Quién hace el cambio en el admin (queda en <c>AuditLogs</c>).</summary>
public sealed record AdminActor(string UserId, string Email);

/// <summary>
/// Acciones del admin sobre el plan de un vendedor (6.2). Cada una va en una transacción con el candado de la
/// suscripción y deja su registro en <c>AuditLogs</c>. Sin filtro multi-tenant: el admin no tiene alcance.
/// </summary>
public interface IAdminPlanActions
{
    /// <summary>Regala días de Pro (periodo <c>AdminGrant</c>, encadenado como cualquier periodo).</summary>
    /// <param name="notify">Aviso en el sitio y correo al vendedor. El motivo nunca le llega.</param>
    Task<AdminPlanResult> GrantDaysAsync(
        int merchantId, int days, string note, bool notify, AdminActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abona a mano un cargo que ya existe y no está pagado: cuenta igual que un pago confirmado por Pey (periodo,
    /// fundador, anual congelado, premio del referido y correo «Recibimos tu pago»).
    /// </summary>
    Task<AdminPlanResult> CreditChargeAsync(
        int merchantId, int chargeId, string note, AdminActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Anula el premio de un referido de <paramref name="merchantId"/> (quien invitó): si estaba pendiente ya no se da;
    /// si se dio, se quitan los días que todavía no se usan y se recorre lo que venía después.
    /// </summary>
    Task<AdminPlanResult> RevokeReferralRewardAsync(
        int merchantId, int referralId, string note, AdminActor actor, CancellationToken cancellationToken = default);

    /// <summary>Da los premios de referidos que se quedaron pendientes (con el tope y la política de siempre).</summary>
    Task<AdminPlanResult> GrantPendingReferralRewardsAsync(
        int merchantId, AdminActor actor, CancellationToken cancellationToken = default);
}

public abstract record AdminPlanResult
{
    private AdminPlanResult()
    {
    }

    /// <param name="Message">Qué pasó, para mostrarlo en la pantalla.</param>
    public sealed record Done(string Message) : AdminPlanResult;

    /// <summary>El vendedor, el cargo o el referido no existe (o no es de ese vendedor).</summary>
    public sealed record NotFound : AdminPlanResult;

    /// <summary>No se puede hacer en el estado actual (sin prueba, cargo pagado, premio ya anulado…).</summary>
    public sealed record Rejected(string Message) : AdminPlanResult;
}

/// <summary>Edición de los parámetros globales (6.2), con auditoría de cada campo que cambia.</summary>
public interface ISystemSettingsEditor
{
    /// <summary>Lugares de fundador ya asignados (no se puede bajar de ahí).</summary>
    Task<int> AssignedFoundersAsync(CancellationToken cancellationToken = default);

    /// <param name="values">Los valores nuevos (se copian con <c>SystemSettingsRules.CopyValues</c>).</param>
    Task<SettingsUpdateResult> UpdateAsync(SystemSettings values, AdminActor actor, CancellationToken cancellationToken = default);
}

public abstract record SettingsUpdateResult
{
    private SettingsUpdateResult()
    {
    }

    /// <param name="TermsFieldsChanged">Cambiaron parámetros que aparecen en los Términos: hay que publicar otra versión.</param>
    public sealed record Saved(IReadOnlyList<SettingsChange> Changes, IReadOnlyList<string> TermsFieldsChanged) : SettingsUpdateResult;

    public sealed record Unchanged : SettingsUpdateResult;

    public sealed record Invalid(IReadOnlyList<SettingsError> Errors) : SettingsUpdateResult;
}
