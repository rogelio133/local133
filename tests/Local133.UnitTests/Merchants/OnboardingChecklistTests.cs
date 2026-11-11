using Local133.Domain.Merchants;
using static Local133.Domain.Merchants.OnboardingStepState;

namespace Local133.UnitTests.Merchants;

public class OnboardingChecklistTests
{
    public static TheoryData<MerchantStatus, OnboardingStepState[]> ExpectedStates => new()
    {
        { MerchantStatus.PendingBankAccount, [Done, Current, Pending, Pending] },
        { MerchantStatus.BankAccountInReview, [Done, Done, Current, Pending] },
        { MerchantStatus.Active, [Done, Done, Done, Done] },
        { MerchantStatus.Suspended, [] },
        { MerchantStatus.Disabled, [] },
    };

    [Theory]
    [MemberData(nameof(ExpectedStates))]
    public void Estados_de_los_pasos_segun_el_estatus(MerchantStatus status, OnboardingStepState[] expected)
    {
        var steps = OnboardingChecklist.For(status);

        Assert.Equal(expected, steps.Select(s => s.State));
    }

    [Theory]
    [InlineData(MerchantStatus.PendingBankAccount)]
    [InlineData(MerchantStatus.BankAccountInReview)]
    [InlineData(MerchantStatus.Active)]
    public void Pasos_en_orden(MerchantStatus status)
    {
        Assert.Equal(
            [OnboardingStepKind.CreateAccount, OnboardingStepKind.RegisterBankAccount, OnboardingStepKind.BankReview, OnboardingStepKind.ReadyToCharge],
            OnboardingChecklist.For(status).Select(s => s.Kind));
    }

    [Theory]
    [InlineData(MerchantStatus.PendingBankAccount)]
    [InlineData(MerchantStatus.BankAccountInReview)]
    public void Durante_el_onboarding_hay_exactamente_un_paso_en_curso(MerchantStatus status)
    {
        Assert.Single(OnboardingChecklist.For(status), s => s.State == Current);
    }

    [Fact]
    public void Todos_los_estatus_estan_contemplados()
    {
        foreach (var status in Enum.GetValues<MerchantStatus>())
        {
            Assert.NotNull(OnboardingChecklist.For(status));
        }
        Assert.Equal(Enum.GetValues<MerchantStatus>().Length, ExpectedStates.Count);
    }

    [Fact]
    public void Estatus_desconocido_lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OnboardingChecklist.For((MerchantStatus)99));
    }
}
