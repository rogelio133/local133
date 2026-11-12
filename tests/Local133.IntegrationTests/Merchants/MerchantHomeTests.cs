using System.Net;
using Local133.Domain.Merchants;
using Local133.Infrastructure.Persistence;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Merchants;

/// <summary>Paso 1.5: el Inicio del vendedor (/app) refleja su estatus real con el checklist de onboarding.</summary>
public class MerchantHomeTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private const string Email = "vendedor@prueba.mx";

    [Fact]
    public async Task Recien_registrado_tiene_en_curso_la_cuenta_bancaria()
    {
        var html = await HomeAsync(Email);

        Assert.Contains("data-merchant-status=\"PendingBankAccount\"", html);
        Assert.Contains("Hola, vendedor@prueba.mx", html);
        AssertStep(html, OnboardingStepKind.CreateAccount, OnboardingStepState.Done);
        AssertStep(html, OnboardingStepKind.RegisterBankAccount, OnboardingStepState.Current);
        AssertStep(html, OnboardingStepKind.BankReview, OnboardingStepState.Pending);
        AssertStep(html, OnboardingStepKind.ReadyToCharge, OnboardingStepState.Pending);
        Assert.Contains("Paso 2 de 4", html);
        Assert.Contains("href=\"/app/cuenta-bancaria\">Registrar mi CLABE", html);
    }

    [Fact]
    public async Task En_revision_tiene_en_curso_la_revision_de_Banxico()
    {
        var html = await HomeAsync(Email, MerchantStatus.BankAccountInReview);

        AssertStep(html, OnboardingStepKind.RegisterBankAccount, OnboardingStepState.Done);
        AssertStep(html, OnboardingStepKind.BankReview, OnboardingStepState.Current);
        Assert.Contains("Paso 3 de 4", html);
        Assert.Contains("hasta 5 días hábiles", html);
        Assert.Contains("href=\"/app/cuenta-bancaria\">Ver estatus", html);
    }

    [Fact]
    public async Task Activo_ve_el_boton_de_cobrar_sin_checklist()
    {
        var html = await HomeAsync(Email, MerchantStatus.Active);

        Assert.Contains("Ya puedes cobrar", html);
        Assert.Contains("class=\"btn btn-primary btn-lg w-100\" href=\"/app/cobrar\"", html); // no la pestaña: el botón
        Assert.DoesNotContain("data-step=", html);
    }

    [Fact]
    public async Task Suspendido_ve_el_aviso_sin_checklist()
    {
        var html = await HomeAsync(Email, MerchantStatus.Suspended);

        Assert.Contains("Tu cuenta está suspendida", html);
        Assert.DoesNotContain("data-step=", html);
        Assert.DoesNotContain("Ya puedes cobrar", html);
    }

    [Fact]
    public async Task Cada_vendedor_ve_solo_su_negocio()
    {
        await CreateMerchantAsync("uno@prueba.mx");
        await CreateMerchantAsync("dos@prueba.mx");
        await UpdateMerchantAsync("uno@prueba.mx", m => { m.BusinessName = "Tacos Uno"; m.Status = MerchantStatus.Active; });
        await UpdateMerchantAsync("dos@prueba.mx", m => m.BusinessName = "Jugos Dos");

        var uno = await GetHomeAsync("uno@prueba.mx");
        var dos = await GetHomeAsync("dos@prueba.mx");

        Assert.Contains("Hola, Tacos Uno", uno);
        Assert.Contains("data-merchant-status=\"Active\"", uno);
        Assert.DoesNotContain("Jugos Dos", uno);

        Assert.Contains("Hola, Jugos Dos", dos);
        Assert.Contains("data-merchant-status=\"PendingBankAccount\"", dos);
        Assert.DoesNotContain("Tacos Uno", dos);
    }

    private async Task<string> HomeAsync(string email, MerchantStatus? status = null)
    {
        await CreateMerchantAsync(email);
        if (status is { } s)
        {
            await UpdateMerchantAsync(email, m => m.Status = s);
        }
        return await GetHomeAsync(email);
    }

    private async Task<string> GetHomeAsync(string email)
    {
        var client = CreateClient();
        Assert.Equal("/app", Location(await LoginAsync(client, email)));

        var response = await client.GetAsync("/app");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private async Task UpdateMerchantAsync(string email, Action<Merchant> change)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var merchant = await db.Merchants.SingleAsync(m => m.UserId == userId);
        change(merchant);
        await db.SaveChangesAsync();
    }

    private static void AssertStep(string html, OnboardingStepKind kind, OnboardingStepState state) =>
        Assert.Contains($"data-step=\"{kind}\" data-state=\"{state}\"", html);
}
