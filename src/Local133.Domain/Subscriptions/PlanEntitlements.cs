namespace Local133.Domain.Subscriptions;

/// <summary>
/// Lo que permite cada estado del plan (§2 del diseño de planes Básico y Pro, 2026-11-26). Es solo la parte del plan:
/// <c>MerchantStatus</c> manda sobre él (un vendedor suspendido o dado de baja no cobra en ninguno).
/// <list type="bullet">
/// <item>Prueba y Pro: todo.</item>
/// <item>Onboarding: todo Pro (el cobro lo frena el estatus del vendedor) salvo publicar el catálogo en línea.</item>
/// <item>Básico: cobra por monto o celular, historial y corte; sin carrito, catálogo editable, Excel, catálogo en
/// línea ni recibo con logo.</item>
/// <item>Gracia: lo del plan que pagó.</item>
/// <item>Vencido: no genera cobros (lo aplica la 5.14); solo lee su historial y comparte recibos de lo anterior.</item>
/// </list>
/// <see cref="HasBrandedReceipt"/> (5.10): el recibo público lleva el logo del negocio; sin él dice «Cobrado con Local133».
/// </summary>
/// <param name="CanShareReceipt">Del gratuito: solo el vencido lo tiene en falso hasta que la 5.14 lo retire.</param>
/// <param name="HasFullHistory">Del gratuito: solo el vencido lo tiene en falso hasta que la 5.14 lo retire.</param>
/// <param name="HasMonthlyChargeLimit">
/// Del gratuito: solo el vencido lo tiene hasta que la 5.14 lo retire (entonces el vencido deja de cobrar con
/// <see cref="CanCharge"/>).
/// </param>
public sealed record PlanEntitlements(
    bool CanCharge,
    bool CanEditCatalog,
    bool CanChargeWithCart,
    bool CanShareReceipt,
    bool HasFullHistory,
    bool CanExportHistory,
    bool HasDailyCashCut,
    bool CanPublishCatalog,
    bool HasMonthlyChargeLimit,
    bool HasBrandedReceipt)
{
    private static readonly PlanEntitlements ProSet = new(
        CanCharge: true,
        CanEditCatalog: true,
        CanChargeWithCart: true,
        CanShareReceipt: true,
        HasFullHistory: true,
        CanExportHistory: true,
        HasDailyCashCut: true,
        CanPublishCatalog: true,
        HasMonthlyChargeLimit: false,
        HasBrandedReceipt: true);

    private static readonly PlanEntitlements OnboardingSet = ProSet with { CanPublishCatalog = false };

    private static readonly PlanEntitlements BasicSet = new(
        CanCharge: true,
        CanEditCatalog: false,
        CanChargeWithCart: false,
        CanShareReceipt: true,
        HasFullHistory: true,
        CanExportHistory: false,
        HasDailyCashCut: true,
        CanPublishCatalog: false,
        HasMonthlyChargeLimit: false,
        HasBrandedReceipt: false);

    /// <summary>
    /// Hasta la 5.14 el vencido conserva los límites del gratuito (historial de 30 días, recibo sin WhatsApp y límite
    /// mensual), que es lo que hoy se aplica en el servidor; la 5.14 los quita y aplica <see cref="CanCharge"/>.
    /// </summary>
    private static readonly PlanEntitlements ExpiredSet = BasicSet with
    {
        CanCharge = false,
        HasDailyCashCut = false,
        CanShareReceipt = false,
        HasFullHistory = false,
        HasMonthlyChargeLimit = true,
    };

    /// <summary>Lo que tiene un plan vigente (también en su gracia).</summary>
    public static PlanEntitlements ForPlan(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Pro => ProSet,
        SubscriptionPlan.Basic => BasicSet,
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Plan desconocido."),
    };

    public static PlanEntitlements For(PlanStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return status.Tier switch
        {
            PlanTier.Onboarding => OnboardingSet,
            PlanTier.Trial or PlanTier.Pro => ProSet,
            PlanTier.Basic => BasicSet,
            PlanTier.Grace => ForPlan(status.Plan
                ?? throw new ArgumentException("La gracia lleva el plan que se pagó.", nameof(status))),
            PlanTier.Expired => ExpiredSet,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status.Tier, "Estado del plan desconocido."),
        };
    }
}
