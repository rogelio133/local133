namespace Local133.Domain.Subscriptions;

/// <summary>
/// Lo que permite cada estado del plan (§2 del diseño de suscripciones). Es solo la parte del plan:
/// <c>MerchantStatus</c> manda sobre él (un vendedor suspendido o dado de baja no cobra en ninguno), y la 5.5 aplica
/// los dos juntos en el servidor. En el onboarding el catálogo es editable y el cobro lo frena el estatus del vendedor.
/// </summary>
public sealed record PlanEntitlements(
    bool CanEditCatalog,
    bool CanChargeWithCart,
    bool CanShareReceipt,
    bool HasFullHistory,
    bool CanExportHistory,
    bool HasDailyCashCut,
    bool CanPublishCatalog,
    bool HasMonthlyChargeLimit)
{
    public static PlanEntitlements Pro { get; } = new(
        CanEditCatalog: true,
        CanChargeWithCart: true,
        CanShareReceipt: true,
        HasFullHistory: true,
        CanExportHistory: true,
        HasDailyCashCut: true,
        CanPublishCatalog: true,
        HasMonthlyChargeLimit: false);

    public static PlanEntitlements Free { get; } = new(
        CanEditCatalog: false,
        CanChargeWithCart: false,
        CanShareReceipt: false,
        HasFullHistory: false,
        CanExportHistory: false,
        HasDailyCashCut: false,
        CanPublishCatalog: false,
        HasMonthlyChargeLimit: true);

    public static PlanEntitlements For(PlanTier tier) => tier switch
    {
        PlanTier.Onboarding or PlanTier.Trial or PlanTier.Pro or PlanTier.Grace => Pro,
        PlanTier.Free => Free,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Estado del plan desconocido."),
    };
}
