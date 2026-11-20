using System.Net;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Subscriptions;
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

    // ---- Plan (5.3) ----

    [Fact]
    public async Task En_el_onboarding_avisa_que_la_prueba_empieza_al_activarse_y_no_muestra_plan()
    {
        var html = await HomeAsync(Email, MerchantStatus.BankAccountInReview);

        Assert.Contains("data-trial-notice", html);
        Assert.Contains("Al activarse empiezan tus 30 días de prueba de Pro", html);
        Assert.DoesNotContain("data-plan-tier", html);
    }

    [Fact]
    public async Task En_la_prueba_muestra_los_dias_que_quedan()
    {
        var now = DateTimeOffset.UtcNow;
        var html = await HomeWithPlanAsync(MerchantStatus.Active, (SubscriptionPeriodKind.Trial, now.AddDays(-7), MexicoTime.AddDays(now, 23)));

        Assert.Contains("data-plan-tier=\"Trial\"", html);
        Assert.Contains("Prueba: te quedan 23 días", html);
        Assert.Contains("Tienes todo Pro hasta el", html);
        Assert.DoesNotContain("data-trial-notice", html);
    }

    [Fact]
    public async Task Con_un_periodo_pagado_vigente_muestra_Pro()
    {
        var now = DateTimeOffset.UtcNow;
        var html = await HomeWithPlanAsync(MerchantStatus.Active,
            (SubscriptionPeriodKind.Trial, now.AddDays(-60), now.AddDays(-30)),
            (SubscriptionPeriodKind.Paid, now.AddDays(-30), now.AddDays(10)));

        Assert.Contains("data-plan-tier=\"Pro\"", html);
        Assert.Contains("Plan Pro", html);
        Assert.Contains("Vigente hasta el", html);
    }

    [Fact]
    public async Task En_la_gracia_muestra_cuando_vencio_y_hasta_cuando_puede_pagar()
    {
        var now = DateTimeOffset.UtcNow;
        var html = await HomeWithPlanAsync(MerchantStatus.Active,
            (SubscriptionPeriodKind.Trial, now.AddDays(-60), now.AddDays(-30)),
            (SubscriptionPeriodKind.Paid, now.AddDays(-30), now.AddDays(-2)));

        Assert.Contains("data-plan-tier=\"Grace\"", html);
        Assert.Contains("Tu plan venció el", html);
        Assert.Contains("para pagar sin perder Pro", html);
    }

    [Fact]
    public async Task Al_terminar_la_prueba_muestra_el_plan_gratuito()
    {
        var now = DateTimeOffset.UtcNow;
        var html = await HomeWithPlanAsync(MerchantStatus.Active, (SubscriptionPeriodKind.Trial, now.AddDays(-40), now.AddDays(-10)));

        Assert.Contains("data-plan-tier=\"Free\"", html);
        Assert.Contains("Plan gratuito", html);
    }

    [Fact]
    public async Task Dado_de_baja_no_muestra_plan()
    {
        var now = DateTimeOffset.UtcNow;
        var html = await HomeWithPlanAsync(MerchantStatus.Disabled, (SubscriptionPeriodKind.Trial, now.AddDays(-7), now.AddDays(23)));

        Assert.Contains("Tu cuenta está dada de baja", html);
        Assert.DoesNotContain("data-plan-tier", html);
    }

    [Fact]
    public async Task El_plan_de_otro_vendedor_no_se_mezcla()
    {
        var now = DateTimeOffset.UtcNow;
        await CreateMerchantAsync("otro@prueba.mx");
        await UpdateMerchantAsync("otro@prueba.mx", m => m.Status = MerchantStatus.Active);
        await SeedPlanAsync("otro@prueba.mx", (SubscriptionPeriodKind.Trial, now.AddDays(-7), now.AddDays(23)));

        var html = await HomeAsync(Email, MerchantStatus.Active);

        // Activo sin periodos propios (datos de prueba): no hay tarjeta de plan, ni la del otro vendedor.
        Assert.DoesNotContain("data-plan-tier", html);
    }

    private async Task<string> HomeWithPlanAsync(
        MerchantStatus status, params (SubscriptionPeriodKind Kind, DateTimeOffset Start, DateTimeOffset End)[] periods)
    {
        await CreateMerchantAsync(Email);
        await UpdateMerchantAsync(Email, m => m.Status = status);
        await SeedPlanAsync(Email, periods);
        return await GetHomeAsync(Email);
    }

    /// <summary>Suscripción con los periodos indicados (sin las reglas de encadenamiento: es el estado a probar).</summary>
    private async Task SeedPlanAsync(string email, params (SubscriptionPeriodKind Kind, DateTimeOffset Start, DateTimeOffset End)[] periods)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var merchantId = await db.Merchants.Where(m => m.UserId == userId).Select(m => m.Id).SingleAsync();
        var last = periods.MaxBy(p => p.End);
        db.Subscriptions.Add(new Subscription
        {
            MerchantId = merchantId,
            TrialStartedAt = periods[0].Start,
            ProUntil = last.End,
            LastPeriodKind = last.Kind,
            Periods = periods.Select(p => new SubscriptionPeriod
            {
                MerchantId = merchantId, Kind = p.Kind, StartsAt = p.Start, EndsAt = p.End, CreatedAt = p.Start,
            }).ToList(),
        });
        await db.SaveChangesAsync();
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
