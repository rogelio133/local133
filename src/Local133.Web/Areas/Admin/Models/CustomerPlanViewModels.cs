using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Local133.Domain.Audit;
using Local133.Domain.Subscriptions;
using Local133.Application.Admin;

namespace Local133.Web.Areas.Admin.Models;

/// <summary>Rutas de las acciones del plan de un cliente (6.2).</summary>
public static class CustomerPlanUrls
{
    public static string GrantDays(int id) => $"{CustomerUrls.Tab(id, CustomerTab.Plan)}/regalar-dias";

    public static string CreditCharge(int id, int chargeId) =>
        $"{CustomerUrls.Tab(id, CustomerTab.Plan)}/cargos/{chargeId.ToString(CultureInfo.InvariantCulture)}/abonar";

    public static string RevokeReferral(int id, int referralId) =>
        $"{CustomerUrls.Tab(id, CustomerTab.Plan)}/referidos/{referralId.ToString(CultureInfo.InvariantCulture)}/anular";

    public static string GrantPendingRewards(int id) => $"{CustomerUrls.Tab(id, CustomerTab.Plan)}/referidos/premios-pendientes";
}

/// <summary>Formulario «Regalar días».</summary>
public sealed class GrantDaysInput
{
    [Display(Name = "Días de Pro")]
    [Required(ErrorMessage = "Escribe cuántos días.")]
    [Range(1, AdminPlanLimits.MaxGrantDays, ErrorMessage = "Regala entre 1 y 365 días.")]
    public int? Days { get; set; }

    [Display(Name = "Motivo (solo lo ve el admin)")]
    [Required(ErrorMessage = "Escribe el motivo.")]
    [StringLength(AdminPlanLimits.NoteMaxLength, ErrorMessage = "El motivo admite hasta 500 caracteres.")]
    public string? Note { get; set; }

    /// <summary>Sin marcar no llega el campo: el valor por defecto es <c>false</c> y la pantalla la muestra marcada.</summary>
    [Display(Name = "Avisar al vendedor (en el sitio y por correo)")]
    public bool Notify { get; set; }
}

/// <summary>Motivo de una acción confirmada en un modal (abonar a mano, anular un premio).</summary>
public sealed class PlanNoteInput
{
    [Display(Name = "Motivo")]
    [Required(ErrorMessage = "Escribe el motivo.")]
    [StringLength(AdminPlanLimits.NoteMaxLength, ErrorMessage = "El motivo admite hasta 500 caracteres.")]
    public string? Note { get; set; }
}

/// <summary>Un periodo de Pro en la pestaña Plan.</summary>
/// <param name="LastDayText">Último día con Pro del periodo (fin excluido menos un instante).</param>
public sealed record PlanPeriodRow(
    int Id, SubscriptionPeriodKind Kind, string StartsText, string LastDayText, int Days, bool IsCurrent, bool IsFuture,
    int? ChargeId, int? ReferralId);

/// <summary>Una transacción que Pey reportó en el link de un cargo.</summary>
public sealed record PlanPaymentRow(
    string TransactionId, string? Method, string AmountText, string BaseText, string? FeeText, string PaidText);

/// <summary>Un cargo (link de pago) de la suscripción.</summary>
public sealed record PlanChargeRow(
    int Id,
    string CreatedText,
    SubscriptionPackage Package,
    string ListPriceText,
    string? DiscountText,
    string AmountText,
    SubscriptionChargeStatus Status,
    bool IsFounderPrice,
    string? PaidText,
    bool CreditedManually,
    string? PeyPaymentLinkId,
    string? FailureReason,
    IReadOnlyList<PlanPaymentRow> Payments)
{
    /// <summary>Se puede abonar a mano: cualquier cargo que no esté pagado.</summary>
    public bool CanCredit => Status != SubscriptionChargeStatus.Paid;
}

public enum ReferralAdminState
{
    NotPaid,
    PendingReward,
    Rewarded,
    Skipped,
    Revoked,
}

/// <summary>Un vendedor que se registró con el código de este cliente.</summary>
public sealed record ReferralAdminRow(
    int Id,
    int ReferredMerchantId,
    string ReferredName,
    string RegisteredText,
    string? QualifiedText,
    ReferralAdminState State,
    string StateText,
    string? RewardPeriodText,
    bool CanRevoke);

/// <summary>Quién invitó a este cliente.</summary>
public sealed record ReferredByView(int ReferrerMerchantId, string ReferrerName, string Code, string StateText);

/// <summary>Un cambio del admin en la bitácora.</summary>
public sealed record AuditRow(string WhenText, string ActorEmail, AuditAction Action, string Summary, string? Note);

/// <summary>Pestaña Plan: resumen (6.1), periodos, cargos, referidos, acciones y bitácora (6.2).</summary>
public sealed class CustomerPlanViewModel
{
    public required CustomerHeaderViewModel Header { get; init; }

    public required PlanStatus Status { get; init; }

    public string? PlanDateText { get; init; }

    public int? DaysLeft { get; init; }

    public string? TrialStartedText { get; init; }

    public string? ProUntilText { get; init; }

    public string? GraceUntilText { get; init; }

    public bool IsFounder { get; init; }

    public int? FounderNumber { get; init; }

    public SubscriptionPackage? LastPackage { get; init; }

    public string? LockedAnnualPriceText { get; init; }

    /// <summary>Tiene suscripción (la prueba empezó): se le pueden regalar días.</summary>
    public bool HasSubscription { get; init; }

    public IReadOnlyList<PlanPeriodRow> Periods { get; init; } = [];

    public IReadOnlyList<PlanChargeRow> Charges { get; init; } = [];

    public ReferredByView? ReferredBy { get; init; }

    public IReadOnlyList<ReferralAdminRow> Referrals { get; init; } = [];

    /// <summary>Premios en los últimos 12 meses (sin los anulados).</summary>
    public int RewardsInWindow { get; init; }

    public int RewardsPerYear { get; init; }

    public bool HasPendingRewards => Referrals.Any(r => r.State == ReferralAdminState.PendingReward);

    public IReadOnlyList<AuditRow> Audit { get; init; } = [];

    /// <summary>«Avisar al vendedor» va marcada por defecto.</summary>
    public GrantDaysInput Grant { get; set; } = new() { Notify = true };

    public PlanNoteInput Confirm { get; set; } = new();
}

/// <summary>Textos e insignias de la pestaña Plan y de la bitácora.</summary>
public static class PlanLabels
{
    public static string PeriodKind(SubscriptionPeriodKind kind) => kind switch
    {
        SubscriptionPeriodKind.Trial => "Prueba",
        SubscriptionPeriodKind.Paid => "Pagado",
        SubscriptionPeriodKind.ReferralReward => "Premio de referido",
        SubscriptionPeriodKind.AdminGrant => "Regalo del admin",
        SubscriptionPeriodKind.Conversion => "Conversión a Pro",
        SubscriptionPeriodKind.Transition => "Transición",
        _ => kind.ToString(),
    };

    public static string PeriodBadge(SubscriptionPeriodKind kind) => kind switch
    {
        SubscriptionPeriodKind.Trial => "text-bg-info",
        SubscriptionPeriodKind.Paid => "text-bg-primary",
        SubscriptionPeriodKind.ReferralReward => "text-bg-success",
        _ => "text-bg-warning",
    };

    public static string ChargeStatus(SubscriptionChargeStatus status) => status switch
    {
        SubscriptionChargeStatus.Pending => "Pendiente",
        SubscriptionChargeStatus.Processing => "Procesando",
        SubscriptionChargeStatus.Paid => "Pagado",
        SubscriptionChargeStatus.Partial => "Incompleto",
        SubscriptionChargeStatus.Failed => "Falló al crear",
        _ => status.ToString(),
    };

    public static string ChargeBadge(SubscriptionChargeStatus status) => status switch
    {
        SubscriptionChargeStatus.Paid => "text-bg-success",
        SubscriptionChargeStatus.Partial => "text-bg-danger",
        SubscriptionChargeStatus.Processing => "text-bg-info",
        SubscriptionChargeStatus.Failed => "text-bg-dark",
        _ => "text-bg-light border",
    };

    public static string ReferralBadge(ReferralAdminState state) => state switch
    {
        ReferralAdminState.Rewarded => "text-bg-success",
        ReferralAdminState.PendingReward => "text-bg-info",
        ReferralAdminState.Skipped => "text-bg-warning",
        ReferralAdminState.Revoked => "text-bg-danger",
        _ => "text-bg-light border",
    };

    public static (ReferralAdminState State, string Text) ReferralState(Referral referral, int rewardsPerYear) => referral switch
    {
        { QualifiedAt: null } => (ReferralAdminState.NotPaid, "Aún no paga"),
        { RevokedAt: not null } => (ReferralAdminState.Revoked, "Premio anulado"),
        { RewardedAt: not null } => (ReferralAdminState.Rewarded, "Premio otorgado"),
        { SkippedReason: ReferralSkipReason.RewardCap } => (ReferralAdminState.Skipped,
            $"Sin premio (tope de {rewardsPerYear.ToString(CultureInfo.InvariantCulture)} en 12 meses)"),
        { SkippedReason: ReferralSkipReason.ReferrerDisabled } => (ReferralAdminState.Skipped, "Sin premio (estaba dado de baja)"),
        { SkippedReason: not null } => (ReferralAdminState.Skipped, "Sin premio"),
        _ => (ReferralAdminState.PendingReward, "Premio pendiente"),
    };

    public static string Audit(AuditAction action) => action switch
    {
        AuditAction.SettingsUpdated => "Parámetros",
        AuditAction.ProDaysGranted => "Días regalados",
        AuditAction.ChargeCreditedManually => "Abono manual",
        AuditAction.ReferralRewardRevoked => "Premio anulado",
        AuditAction.PendingReferralRewardsGranted => "Premios pendientes",
        AuditAction.PlanTransition => "Transición a Básico",
        AuditAction.PlanChange => "Cambio de planes",
        _ => action.ToString(),
    };
}

/// <summary>Modal que pide el motivo antes de una acción del plan (abonar a mano, anular un premio).</summary>
public sealed record PlanConfirmModalViewModel(
    string Id, string Title, string Message, string Action, string SubmitText, string SubmitClass);
