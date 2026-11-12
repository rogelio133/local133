using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;

namespace Local133.UnitTests.Merchants;

/// <summary>Transiciones del vendedor por el estatus CoDi de su cuenta bancaria y cuándo puede capturarla.</summary>
public class MerchantBankAccountStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(MerchantStatus.PendingBankAccount, PeyCodiStatus.Pending, MerchantStatus.BankAccountInReview, true)]
    [InlineData(MerchantStatus.PendingBankAccount, PeyCodiStatus.Processing, MerchantStatus.BankAccountInReview, true)]
    [InlineData(MerchantStatus.PendingBankAccount, PeyCodiStatus.Active, MerchantStatus.Active, true)]
    [InlineData(MerchantStatus.PendingBankAccount, PeyCodiStatus.Error, MerchantStatus.PendingBankAccount, false)]
    [InlineData(MerchantStatus.BankAccountInReview, PeyCodiStatus.Pending, MerchantStatus.BankAccountInReview, false)]
    [InlineData(MerchantStatus.BankAccountInReview, PeyCodiStatus.Processing, MerchantStatus.BankAccountInReview, false)]
    [InlineData(MerchantStatus.BankAccountInReview, PeyCodiStatus.Active, MerchantStatus.Active, true)]
    [InlineData(MerchantStatus.BankAccountInReview, PeyCodiStatus.Error, MerchantStatus.PendingBankAccount, true)]
    public void Durante_el_onboarding_sigue_el_estatus_de_la_cuenta(
        MerchantStatus from, PeyCodiStatus codiStatus, MerchantStatus expected, bool changed)
    {
        var merchant = new Merchant { Status = from };

        Assert.Equal(changed, merchant.ApplyBankAccountStatus(codiStatus));
        Assert.Equal(expected, merchant.Status);
    }

    [Theory]
    [InlineData(MerchantStatus.Active)]
    [InlineData(MerchantStatus.Suspended)]
    [InlineData(MerchantStatus.Disabled)]
    public void Fuera_del_onboarding_no_cambia(MerchantStatus status)
    {
        foreach (var codiStatus in Enum.GetValues<PeyCodiStatus>())
        {
            var merchant = new Merchant { Status = status };
            Assert.False(merchant.ApplyBankAccountStatus(codiStatus));
            Assert.Equal(status, merchant.Status);
        }
    }

    [Fact]
    public void Estatus_CoDi_desconocido_lanza() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Merchant { Status = MerchantStatus.PendingBankAccount }.ApplyBankAccountStatus((PeyCodiStatus)99));

    [Fact]
    public void Sin_cuenta_y_pendiente_puede_capturarla() =>
        Assert.True(new Merchant { Status = MerchantStatus.PendingBankAccount }.CanSubmitBankAccount(null));

    [Fact]
    public void Con_la_cuenta_rechazada_puede_volver_a_capturarla() =>
        Assert.True(new Merchant { Status = MerchantStatus.PendingBankAccount }.CanSubmitBankAccount(Account(PeyCodiStatus.Error)));

    [Theory]
    [InlineData(PeyCodiStatus.Pending)]
    [InlineData(PeyCodiStatus.Processing)]
    [InlineData(PeyCodiStatus.Active)]
    public void Con_una_cuenta_vigente_no_puede_capturar_otra(PeyCodiStatus codiStatus) =>
        Assert.False(new Merchant { Status = MerchantStatus.PendingBankAccount }.CanSubmitBankAccount(Account(codiStatus)));

    [Theory]
    [InlineData(MerchantStatus.BankAccountInReview)]
    [InlineData(MerchantStatus.Active)]
    [InlineData(MerchantStatus.Suspended)]
    [InlineData(MerchantStatus.Disabled)]
    public void Fuera_de_PendingBankAccount_no_puede_capturarla(MerchantStatus status)
    {
        var merchant = new Merchant { Status = status };
        Assert.False(merchant.CanSubmitBankAccount(null));
        Assert.False(merchant.CanSubmitBankAccount(Account(PeyCodiStatus.Error)));
    }

    [Fact]
    public void Reenvio_reemplaza_datos_y_limpia_el_error()
    {
        var account = Account(PeyCodiStatus.Pending);
        account.UpdateStatus(PeyCodiStatus.Error, "CLABE no registrada en CoDi", Now);
        Assert.Equal("CLABE no registrada en CoDi", account.ErrorMessage);

        var later = Now.AddDays(1);
        account.Resubmit(" Otro Titular ", "032180000118359719", " Otro negocio ", PeyEntity.Business, "pey-2", PeyCodiStatus.Pending, later);

        Assert.Equal("Otro Titular", account.HolderName);
        Assert.Equal("Otro negocio", account.Alias);
        Assert.Equal("032180000118359719", account.Clabe);
        Assert.Equal(PeyEntity.Business, account.Entity);
        Assert.Equal("pey-2", account.PeyBankAccountId);
        Assert.Equal(PeyCodiStatus.Pending, account.CodiStatus);
        Assert.Null(account.ErrorMessage);
        Assert.Equal(later, account.SubmittedAt);
        Assert.Equal(later, account.StatusUpdatedAt);
    }

    [Fact]
    public void UpdateStatus_solo_conserva_el_mensaje_en_Error_y_avisa_si_cambio()
    {
        var account = Account(PeyCodiStatus.Pending);

        Assert.False(account.UpdateStatus(PeyCodiStatus.Pending, "ignorado", Now.AddHours(1)));
        Assert.Equal(Now, account.StatusUpdatedAt);

        Assert.True(account.UpdateStatus(PeyCodiStatus.Active, "ignorado", Now.AddHours(2)));
        Assert.Null(account.ErrorMessage);
        Assert.Equal(Now.AddHours(2), account.StatusUpdatedAt);

        Assert.True(account.UpdateStatus(PeyCodiStatus.Error, new string('x', BankAccount.ErrorMessageMaxLength + 50), Now.AddHours(3)));
        Assert.Equal(BankAccount.ErrorMessageMaxLength, account.ErrorMessage!.Length);
    }

    [Fact]
    public void Submit_rechaza_una_CLABE_invalida() =>
        Assert.Throws<ArgumentException>(() =>
            BankAccount.Submit(1, "Titular", "012180015826225710", "Negocio", PeyEntity.Person, "pey-1", PeyCodiStatus.Pending, Now));

    private static BankAccount Account(PeyCodiStatus codiStatus) =>
        BankAccount.Submit(1, "Titular de prueba", "012180015826225713", "Tacos", PeyEntity.Person, "pey-1", codiStatus, Now);
}
