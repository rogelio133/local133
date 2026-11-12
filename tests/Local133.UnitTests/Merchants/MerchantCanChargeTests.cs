using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;

namespace Local133.UnitTests.Merchants;

/// <summary>Solo un vendedor activo con su cuenta bancaria activa en CoDi puede generar cobros.</summary>
public class MerchantCanChargeTests
{
    private const string TestClabe = "012180015826225713";

    public static TheoryData<MerchantStatus, PeyCodiStatus> AllCombinations()
    {
        var data = new TheoryData<MerchantStatus, PeyCodiStatus>();
        foreach (var status in Enum.GetValues<MerchantStatus>())
        {
            foreach (var codiStatus in Enum.GetValues<PeyCodiStatus>())
            {
                data.Add(status, codiStatus);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void Puede_cobrar_solo_activo_y_con_la_cuenta_activa(MerchantStatus status, PeyCodiStatus codiStatus)
    {
        var merchant = new Merchant { Id = 7, Status = status };
        var account = BankAccount.Submit(7, "Titular", TestClabe, "Puesto", PeyEntity.Person, "01PEY", codiStatus, DateTimeOffset.UtcNow);

        var expected = status == MerchantStatus.Active && codiStatus == PeyCodiStatus.Active;
        Assert.Equal(expected, merchant.CanCharge(account));
    }

    [Theory]
    [InlineData(MerchantStatus.PendingBankAccount)]
    [InlineData(MerchantStatus.Active)]
    public void Sin_cuenta_bancaria_no_puede_cobrar(MerchantStatus status) =>
        Assert.False(new Merchant { Status = status }.CanCharge(null));
}
