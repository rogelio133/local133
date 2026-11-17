using Local133.Domain.Merchants;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Inicio del vendedor (/app): saludo, estatus y checklist de activación.</summary>
public sealed record MerchantHomeViewModel(
    string DisplayName, MerchantStatus Status, IReadOnlyList<OnboardingStepViewModel> Steps, string? BankAccountRejection = null,
    DaySummaryViewModel? Today = null)
{
    public const string DefaultRejection = "Banxico no pudo registrarla para CoDi.";

    /// <param name="bankAccountRejection">Motivo del rechazo de su cuenta bancaria, si Pey la rechazó.</param>
    /// <param name="today">Resumen del día (vendedores que ya cobran); <c>null</c> durante el onboarding.</param>
    public static MerchantHomeViewModel From(
        Domain.Merchants.Merchant merchant, string? userName, string? bankAccountRejection = null, DaySummaryViewModel? today = null) => new(
        string.IsNullOrWhiteSpace(merchant.BusinessName) ? userName ?? "vendedor" : merchant.BusinessName,
        merchant.Status,
        OnboardingChecklist.For(merchant.Status).Select(OnboardingStepViewModel.From).ToArray(),
        bankAccountRejection,
        today);

    public bool IsOnboarding => Status is MerchantStatus.PendingBankAccount or MerchantStatus.BankAccountInReview;

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
