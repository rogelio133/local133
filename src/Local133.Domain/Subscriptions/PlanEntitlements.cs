namespace Local133.Domain.Subscriptions;

/// <summary>
/// Lo que permite cada estado del plan (§2 del diseño de planes Básico y Pro, 2026-11-26). Es solo la parte del plan:
/// <c>MerchantStatus</c> manda sobre él (un vendedor suspendido o dado de baja no cobra en ninguno). En todos los
/// estados se ve el historial completo y se comparte el recibo (WhatsApp y «Compartir»).
/// <list type="bullet">
/// <item>Prueba y Pro: todo.</item>
/// <item>Onboarding: todo Pro (el cobro lo frena el estatus del vendedor) salvo publicar el catálogo en línea.</item>
/// <item>Básico: cobra por monto o celular, con historial y corte; sin carrito, catálogo editable, Excel, catálogo en
/// línea ni recibo con logo.</item>
/// <item>Gracia: lo del plan que pagó.</item>
/// <item>Vencido: no genera cobros ni ve el corte; lee su historial, comparte recibos de lo anterior y ve su catálogo
/// en solo lectura (5.14).</item>
/// </list>
/// <see cref="HasBrandedReceipt"/> (5.10): el recibo público lleva el logo del negocio; sin él dice «Cobrado con Local133».
/// </summary>
/// <param name="CanCharge">Genera cobros nuevos (monto, celular o carrito). Lo ya generado se abona siempre.</param>
public sealed record PlanEntitlements(
    bool CanCharge,
    bool CanEditCatalog,
    bool CanChargeWithCart,
    bool CanExportHistory,
    bool HasDailyCashCut,
    bool CanPublishCatalog,
    bool HasBrandedReceipt)
{
    private static readonly PlanEntitlements ProSet = new(
        CanCharge: true,
        CanEditCatalog: true,
        CanChargeWithCart: true,
        CanExportHistory: true,
        HasDailyCashCut: true,
        CanPublishCatalog: true,
        HasBrandedReceipt: true);

    private static readonly PlanEntitlements OnboardingSet = ProSet with { CanPublishCatalog = false };

    private static readonly PlanEntitlements BasicSet = new(
        CanCharge: true,
        CanEditCatalog: false,
        CanChargeWithCart: false,
        CanExportHistory: false,
        HasDailyCashCut: true,
        CanPublishCatalog: false,
        HasBrandedReceipt: false);

    private static readonly PlanEntitlements ExpiredSet = BasicSet with { CanCharge = false, HasDailyCashCut = false };

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
