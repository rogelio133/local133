namespace Local133.Domain.Merchants;

/// <summary>Pasos del onboarding del vendedor, en orden.</summary>
public enum OnboardingStepKind
{
    CreateAccount,
    RegisterBankAccount,
    BankReview,
    ReadyToCharge,
}

public enum OnboardingStepState
{
    Done,
    Current,
    Pending,
}

public sealed record OnboardingStep(OnboardingStepKind Kind, OnboardingStepState State);

/// <summary>
/// Checklist de activación (crear cuenta → cuenta bancaria → revisión de Banxico → listo para cobrar) calculado
/// a partir de <see cref="MerchantStatus"/>. Para <see cref="MerchantStatus.Suspended"/> y
/// <see cref="MerchantStatus.Disabled"/> el onboarding ya no aplica y la lista viene vacía.
/// </summary>
public static class OnboardingChecklist
{
    public static IReadOnlyList<OnboardingStep> For(MerchantStatus status) => status switch
    {
        MerchantStatus.PendingBankAccount => Steps(currentIndex: 1),
        MerchantStatus.BankAccountInReview => Steps(currentIndex: 2),
        MerchantStatus.Active => Steps(currentIndex: Kinds.Length),
        MerchantStatus.Suspended or MerchantStatus.Disabled => [],
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Estatus de vendedor desconocido."),
    };

    private static readonly OnboardingStepKind[] Kinds = Enum.GetValues<OnboardingStepKind>();

    /// <summary>Los pasos antes de <paramref name="currentIndex"/> están hechos; ese es el actual y los demás, pendientes.</summary>
    private static OnboardingStep[] Steps(int currentIndex) =>
        Kinds.Select((kind, i) => new OnboardingStep(kind, i < currentIndex
            ? OnboardingStepState.Done
            : i == currentIndex ? OnboardingStepState.Current : OnboardingStepState.Pending)).ToArray();
}
