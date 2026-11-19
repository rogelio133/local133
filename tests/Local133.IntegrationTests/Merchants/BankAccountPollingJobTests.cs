using Local133.Application.Payments;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Infrastructure.Jobs;
using Local133.Infrastructure.Persistence;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Merchants;

/// <summary>Paso 2.3: sondeo de cuentas bancarias en revisión (job de Hangfire cada 30 min, aquí llamado directo).</summary>
public class BankAccountPollingJobTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private const string TestClabe = "012180015826225713";

    [Fact]
    public async Task Consulta_solo_las_cuentas_en_revision_las_menos_recientes_primero_y_aplica_el_resultado()
    {
        var now = DateTimeOffset.UtcNow;
        var neverChecked = await AccountAsync("a@prueba.mx", PeyCodiStatus.Pending, checkedAt: null);
        var old = await AccountAsync("b@prueba.mx", PeyCodiStatus.Processing, checkedAt: now.AddHours(-2));
        var recent = await AccountAsync("c@prueba.mx", PeyCodiStatus.Pending, checkedAt: now.AddMinutes(-5));
        await AccountAsync("d@prueba.mx", PeyCodiStatus.Active, checkedAt: null);
        await AccountAsync("e@prueba.mx", PeyCodiStatus.Error, checkedAt: null);
        Factory.Gateway.LookupStatus = PeyCodiStatus.Active;

        var checkedCount = await RunJobAsync();

        Assert.Equal(3, checkedCount);
        Assert.Equal([neverChecked.PeyId, old.PeyId, recent.PeyId], Factory.Gateway.BankAccountLookups.ToArray());
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var id in new[] { neverChecked.MerchantId, old.MerchantId, recent.MerchantId })
        {
            Assert.Equal(MerchantStatus.Active, (await db.Merchants.SingleAsync(m => m.Id == id)).Status);
            var account = await db.BankAccounts.SingleAsync(b => b.MerchantId == id);
            Assert.Equal(PeyCodiStatus.Active, account.CodiStatus);
            Assert.True(account.StatusCheckedAt >= now.AddSeconds(-1));
        }

        // Un correo por cada vendedor que pasó a activo; ninguno al que ya lo estaba ni al rechazado.
        Assert.Equal(["a@prueba.mx", "b@prueba.mx", "c@prueba.mx"], ActivationEmails().Select(e => e.To).Order().ToArray());
        Assert.All(ActivationEmails(), e => Assert.Equal(ActivationEmailLink, e.Link));

        // La siguiente corrida ya no las consulta (están activas) y no repite correos.
        Assert.Equal(0, await RunJobAsync());
        Assert.Equal(3, ActivationEmails().Count);
    }

    [Theory]
    [InlineData(PeyCodiStatus.Pending)]
    [InlineData(PeyCodiStatus.Processing)]
    [InlineData(PeyCodiStatus.Error)]
    public async Task Sin_activacion_no_hay_correo(PeyCodiStatus lookup)
    {
        await AccountAsync("a@prueba.mx", PeyCodiStatus.Pending, checkedAt: null);
        Factory.Gateway.LookupStatus = lookup;

        await RunJobAsync();

        Assert.Empty(ActivationEmails());
    }

    [Fact]
    public async Task Un_rechazo_detectado_regresa_al_vendedor_a_capturar_su_CLABE()
    {
        var account = await AccountAsync("a@prueba.mx", PeyCodiStatus.Processing, checkedAt: null);
        Factory.Gateway.LookupStatus = PeyCodiStatus.Error;
        Factory.Gateway.LookupErrorMessage = "La CLABE no pertenece al titular";

        await RunJobAsync();

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(MerchantStatus.PendingBankAccount, (await db.Merchants.SingleAsync(m => m.Id == account.MerchantId)).Status);
        Assert.Equal("La CLABE no pertenece al titular",
            (await db.BankAccounts.SingleAsync(b => b.MerchantId == account.MerchantId)).ErrorMessage);
    }

    [Fact]
    public async Task Si_Pey_falla_sigue_con_las_demas_cuentas_y_no_guarda_nada()
    {
        await AccountAsync("a@prueba.mx", PeyCodiStatus.Pending, checkedAt: null);
        await AccountAsync("b@prueba.mx", PeyCodiStatus.Pending, checkedAt: null);
        Factory.Gateway.LookupException = new PaymentGatewayUnavailableException("timeout");

        var checkedCount = await RunJobAsync();

        Assert.Equal(0, checkedCount);
        Assert.Equal(2, Factory.Gateway.BankAccountLookups.Count);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.All(await db.BankAccounts.ToListAsync(), b =>
        {
            Assert.Equal(PeyCodiStatus.Pending, b.CodiStatus);
            Assert.Null(b.StatusCheckedAt);
        });
    }

    [Fact]
    public async Task Sin_cuentas_en_revision_no_llama_a_Pey()
    {
        await AccountAsync("a@prueba.mx", PeyCodiStatus.Active, checkedAt: null);

        Assert.Equal(0, await RunJobAsync());
        Assert.Empty(Factory.Gateway.BankAccountLookups);
    }

    private async Task<int> RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BankAccountPollingJob>().RunAsync(CancellationToken.None);
    }

    private async Task<(int MerchantId, string PeyId)> AccountAsync(string email, PeyCodiStatus status, DateTimeOffset? checkedAt)
    {
        await CreateMerchantAsync(email);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var merchant = await db.Merchants.SingleAsync(m => m.UserId == userId);
        merchant.Status = status switch
        {
            PeyCodiStatus.Active => MerchantStatus.Active,
            PeyCodiStatus.Error => MerchantStatus.PendingBankAccount,
            _ => MerchantStatus.BankAccountInReview,
        };
        var peyId = $"01PEY{merchant.Id:D10}";
        var account = BankAccount.Submit(merchant.Id, "Ramón Orraca Lascurain", TestClabe, "Puesto", PeyEntity.Person,
            peyId, status, DateTimeOffset.UtcNow.AddDays(-1));
        account.StatusCheckedAt = checkedAt;
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();
        return (merchant.Id, peyId);
    }
}
