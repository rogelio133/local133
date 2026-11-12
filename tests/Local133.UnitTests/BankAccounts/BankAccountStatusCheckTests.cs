using Local133.Domain.BankAccounts;
using Local133.Domain.Pey;

namespace Local133.UnitTests.BankAccounts;

/// <summary>Lecturas del estatus CoDi en Pey (botón "Verificar estatus" y sondeo) y el freno entre consultas.</summary>
public class BankAccountStatusCheckTests
{
    private const string TestClabe = "012180015826225713";
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    [Theory]
    [InlineData(PeyCodiStatus.Pending, true)]
    [InlineData(PeyCodiStatus.Processing, true)]
    [InlineData(PeyCodiStatus.Active, false)]
    [InlineData(PeyCodiStatus.Error, false)]
    public void Esta_en_revision_solo_con_Pending_o_Processing(PeyCodiStatus status, bool expected)
    {
        Assert.Equal(expected, NewAccount(status).IsInReview);
    }

    [Fact]
    public void El_alta_cuenta_como_lectura_del_estatus()
    {
        Assert.Equal(Now, NewAccount().StatusCheckedAt);
    }

    [Fact]
    public void El_reintento_tambien_cuenta_como_lectura()
    {
        var account = NewAccount();
        var later = Now.AddDays(2);

        account.Resubmit("Titular", TestClabe, "Negocio", PeyEntity.Person, "01NUEVO", PeyCodiStatus.Pending, later);

        Assert.Equal(later, account.StatusCheckedAt);
    }

    [Fact]
    public void Sin_lectura_previa_se_puede_consultar()
    {
        var account = new BankAccount { CodiStatus = PeyCodiStatus.Pending };

        Assert.Null(account.TimeUntilNextStatusCheck(Now, Interval));
    }

    [Fact]
    public void Dentro_del_intervalo_devuelve_lo_que_falta()
    {
        var account = NewAccount();

        Assert.Equal(TimeSpan.FromSeconds(45), account.TimeUntilNextStatusCheck(Now.AddSeconds(15), Interval));
    }

    [Fact]
    public void Justo_al_cumplir_el_intervalo_ya_se_puede()
    {
        var account = NewAccount();

        Assert.Null(account.TimeUntilNextStatusCheck(Now + Interval, Interval));
        Assert.Null(account.TimeUntilNextStatusCheck(Now.AddHours(1), Interval));
    }

    [Fact]
    public void Un_reloj_atrasado_no_permite_consultar_antes_de_tiempo()
    {
        var account = NewAccount();

        Assert.Equal(Interval + TimeSpan.FromSeconds(5), account.TimeUntilNextStatusCheck(Now.AddSeconds(-5), Interval));
    }

    [Fact]
    public void Registra_la_lectura_aunque_el_estatus_no_cambie()
    {
        var account = NewAccount();
        var later = Now.AddMinutes(30);

        Assert.False(account.RecordStatusCheck(PeyCodiStatus.Pending, null, later));

        Assert.Equal(later, account.StatusCheckedAt);
        Assert.Equal(Now, account.StatusUpdatedAt);
    }

    [Fact]
    public void Registra_la_lectura_y_el_cambio_de_estatus()
    {
        var account = NewAccount();
        var later = Now.AddMinutes(30);

        Assert.True(account.RecordStatusCheck(PeyCodiStatus.Error, "Cuenta inexistente", later));

        Assert.Equal(PeyCodiStatus.Error, account.CodiStatus);
        Assert.Equal("Cuenta inexistente", account.ErrorMessage);
        Assert.Equal(later, account.StatusCheckedAt);
        Assert.Equal(later, account.StatusUpdatedAt);
    }

    [Fact]
    public void El_webhook_no_cuenta_como_lectura()
    {
        var account = NewAccount();

        account.UpdateStatus(PeyCodiStatus.Active, null, Now.AddMinutes(30));

        Assert.Equal(Now, account.StatusCheckedAt);
    }

    private static BankAccount NewAccount(PeyCodiStatus status = PeyCodiStatus.Pending) =>
        BankAccount.Submit(1, "Titular", TestClabe, "Negocio", PeyEntity.Person, "01PEY", status, Now);
}
