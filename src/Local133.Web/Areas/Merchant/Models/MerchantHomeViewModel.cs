using Local133.Domain.Merchants;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Inicio del vendedor (/app): saludo, estatus y checklist de activación.</summary>
public sealed record MerchantHomeViewModel(
    string DisplayName, MerchantStatus Status, IReadOnlyList<OnboardingStepViewModel> Steps, string? BankAccountRejection = null,
    DaySummaryViewModel? Today = null, PlanSummaryViewModel? Plan = null, int TrialDays = 0,
    ChargeUsageViewModel? Usage = null, bool ProfileIncomplete = false, bool TrialStartsOnFirstCharge = false)
{
    /// <summary>Paso opcional de Inicio (4.1): falta el nombre del negocio o el teléfono en «Mi información».</summary>
    public const string ProfileStepTitle = "Completa tu información";

    public const string ProfileStepDescription = "Agrega el nombre de tu negocio y tu teléfono.";

    public const string DefaultRejection = "Banxico no pudo registrarla para CoDi.";

    /// <param name="bankAccountRejection">Motivo del rechazo de su cuenta bancaria, si Pey la rechazó.</param>
    /// <param name="today">Resumen del día (vendedores que ya cobran); <c>null</c> durante el onboarding.</param>
    /// <param name="plan">Tarjeta del plan; <c>null</c> en el onboarding y para un vendedor dado de baja.</param>
    /// <param name="trialDays">Días de la prueba de Pro, para el aviso del checklist.</param>
    public static MerchantHomeViewModel From(
        Domain.Merchants.Merchant merchant, string? userName, string? bankAccountRejection = null, DaySummaryViewModel? today = null,
        PlanSummaryViewModel? plan = null, int trialDays = 0) => new(
        // Primer nombre de la persona (4.0); sin él, el negocio y luego el correo, como antes.
        merchant.GreetingName ?? userName ?? "vendedor",
        merchant.Status,
        OnboardingChecklist.For(merchant.Status).Select(OnboardingStepViewModel.From).ToArray(),
        bankAccountRejection,
        today,
        merchant.Status == MerchantStatus.Disabled ? null : plan,
        trialDays);

    public bool IsOnboarding => Status is MerchantStatus.PendingBankAccount or MerchantStatus.BankAccountInReview;

    /// <summary>Aviso del último paso del checklist: la prueba empieza con el primer cobro (5.13).</summary>
    public string? TrialNotice => TrialDays > 0 ? $"Haz tu primer cobro: ese día empiezan tus {TrialDays} días de Pro." : null;

    /// <summary>
    /// Vendedor activo que aún no cobra (<c>TrialStartsOnFirstCharge</c>, plan en <c>Onboarding</c>): la tarjeta «Ya
    /// puedes cobrar» le recuerda que su prueba empieza con su primer cobro.
    /// </summary>
    public string? FirstChargeTrialText => TrialStartsOnFirstCharge && Status == MerchantStatus.Active && TrialDays > 0
        ? $"Tu prueba de {TrialDays} días de Pro empieza con tu primer cobro."
        : null;

    /// <summary>Número (base 1) del paso en curso.</summary>
    public int CurrentStepNumber => Steps.Select((s, i) => (s, i)).First(x => x.s.State == OnboardingStepState.Current).i + 1;

    public int DoneCount => Steps.Count(s => s.State == OnboardingStepState.Done);
}

public sealed record OnboardingStepViewModel(OnboardingStepKind Kind, OnboardingStepState State, string Label, string Description)
{
    public static OnboardingStepViewModel From(OnboardingStep step)
    {
        var (label, description) = step.Kind switch
        {
            OnboardingStepKind.CreateAccount => ("Crear tu cuenta", "Tu correo ya está registrado."),
            OnboardingStepKind.RegisterBankAccount => ("Registrar tu cuenta bancaria (CLABE)", "Registra la CLABE donde recibirás tus cobros."),
            OnboardingStepKind.BankReview => ("Revisión de Banxico", "Puede tardar hasta 5 días hábiles; te avisaremos por correo."),
            OnboardingStepKind.ReadyToCharge => ("¡Listo para cobrar con CoDi!", "Genera un QR y recibe el dinero directo en tu cuenta."),
            _ => throw new ArgumentOutOfRangeException(nameof(step), step.Kind, "Paso de onboarding desconocido."),
        };
        return new OnboardingStepViewModel(step.Kind, step.State, label, description);
    }
}
