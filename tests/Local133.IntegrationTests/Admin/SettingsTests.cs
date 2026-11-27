using System.Net;
using System.Text.RegularExpressions;
using Local133.Application.Auth;
using Local133.Domain.Audit;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Admin;

/// <summary>6.2: parámetros globales en el admin (/admin/parametros), con validación, auditoría y aviso de los Términos.</summary>
public partial class SettingsTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    private const string AdminEmail = "admin@prueba.mx";
    private const string Url = "/admin/parametros";

    [Fact]
    public async Task Un_vendedor_no_entra_a_los_parametros()
    {
        var client = await ActiveMerchantAsync(Email);

        var get = await client.GetAsync(Url);
        var post = await PostFormAsync(client, "/app", Fields(new SystemSettings()), Url);

        Assert.StartsWith("/Identity/Account/AccessDenied", Location(get));
        Assert.StartsWith("/Identity/Account/AccessDenied", Location(post));
    }

    [Fact]
    public async Task Muestra_los_valores_y_marca_el_menu()
    {
        var html = await GetHtmlAsync(await AdminAsync(), Url);

        Assert.Contains("value=\"199\"", html);
        Assert.Contains("value=\"16\"", html); // IVA en %
        Assert.Contains("data-setting-in-terms", html);
        Assert.Matches(new Regex("class=\"nav-link admin-nav-link active\" href=\"/admin/parametros\""), html);
        Assert.Contains("data-val-regex-pattern", html); // validación en cliente
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task Guardar_cambia_audita_y_advierte_de_los_terminos()
    {
        await WithSettingsAsync(_ => { }, async () =>
        {
            var admin = await AdminAsync();
            var values = await CurrentAsync();
            values.MonthlyPrice = 249m;
            values.VatRate = 0.08m;

            var response = await PostFormAsync(admin, Url, Fields(values));

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var saved = await CurrentAsync();
            Assert.Equal(249m, saved.MonthlyPrice);
            Assert.Equal(0.08m, saved.VatRate);
            var audit = Assert.Single(await WithDbAsync(db => db.AuditLogs.AsNoTracking().ToListAsync()));
            Assert.Equal(AuditAction.SettingsUpdated, audit.Action);
            Assert.Null(audit.MerchantId);
            Assert.Equal(AdminEmail, audit.ActorEmail);
            Assert.Contains("MonthlyPrice 199 → 249", audit.Summary);

            var html = await GetHtmlAsync(admin, Url);
            Assert.Contains("data-settings-terms-warning", html);
            Assert.Contains("Pro, 1 mes", html);
            Assert.Contains("data-settings-change", html);
        });
    }

    [Fact]
    public async Task Un_cambio_fuera_de_los_terminos_no_advierte()
    {
        await WithSettingsAsync(_ => { }, async () =>
        {
            var admin = await AdminAsync();
            var values = await CurrentAsync();
            values.SaleQrExpirationSeconds = 900;

            await PostFormAsync(admin, Url, Fields(values));

            var html = await GetHtmlAsync(admin, Url);
            Assert.Contains("data-settings-notice", html);
            Assert.DoesNotContain("data-settings-terms-warning", html);
            Assert.Equal(900, (await CurrentAsync()).SaleQrExpirationSeconds);
        });
    }

    [Fact]
    public async Task Sin_cambios_no_audita()
    {
        var admin = await AdminAsync();

        var response = await PostFormAsync(admin, Url, Fields(await CurrentAsync()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(await WithDbAsync(db => db.AuditLogs.ToListAsync()));
        Assert.Contains("No había cambios", await GetHtmlAsync(admin, Url));
    }

    [Theory]
    [InlineData(nameof(SystemSettings.FounderMonthlyPrice), "250", "no puede ser mayor que el normal")]
    [InlineData(nameof(SystemSettings.SaleQrExpirationSeconds), "300", "Pey exige al menos 360")]
    [InlineData(nameof(SystemSettings.MonthlyPrice), "199.999", "Escribe un precio")]
    [InlineData(nameof(SystemSettings.FreeMonthlyGeneratedLimit), "50", "no puede ser menor")]
    public async Task Valores_invalidos_no_se_guardan(string field, string value, string message)
    {
        var admin = await AdminAsync();
        var fields = Fields(await CurrentAsync());
        fields[$"Input.{field}"] = value;

        var response = await PostFormAsync(admin, Url, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
        Assert.Equal(SystemSettings.DefaultMonthlyPrice, (await CurrentAsync()).MonthlyPrice);
        Assert.Empty(await WithDbAsync(db => db.AuditLogs.ToListAsync()));
    }

    [Fact]
    public async Task No_se_puede_bajar_de_los_fundadores_asignados()
    {
        await TrialMerchantAsync(Email);
        await WithDbAsync(async db =>
        {
            var subscription = await db.Subscriptions.IgnoreQueryFilters().SingleAsync();
            subscription.AssignFounder(1);
            await db.SaveChangesAsync();
        });
        var fields = Fields(await CurrentAsync());
        fields["Input.FounderSlots"] = "0";

        var response = await PostFormAsync(await AdminAsync(), Url, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ya hay 1 fundadores", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_precio_nuevo_no_cambia_los_cargos_existentes_y_si_los_links_nuevos()
    {
        await WithSettingsAsync(s => s.FounderSlots = 0, async () =>
        {
            var merchant = await TrialMerchantAsync(Email);
            var old = await CreateChargeAsync(merchant);
            Assert.Equal(SystemSettings.DefaultMonthlyPrice, old.Amount);
            var values = await CurrentAsync();
            values.MonthlyPrice = 249m;

            await PostFormAsync(await AdminAsync(), Url, Fields(values));
            var fresh = await CreateChargeAsync(merchant);

            Assert.Equal(SystemSettings.DefaultMonthlyPrice, (await LoadChargeAsync(old.Id)).Amount);
            Assert.NotEqual(old.Id, fresh.Id);
            Assert.Equal(249m, fresh.Amount);
        });
    }

    private Task<SystemSettings> CurrentAsync() =>
        WithDbAsync(db => db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId));

    private static Dictionary<string, string> Fields(SystemSettings s)
    {
        var fields = new Dictionary<string, string>();
        foreach (var field in SystemSettingsRules.Fields)
        {
            var value = SystemSettingsRules.ValueOf(s, field);
            if (field is nameof(SystemSettings.ReferralWelcomeDiscountRate) or nameof(SystemSettings.VatRate)
                or nameof(SystemSettings.ReferenceTerminalFeeRate))
            {
                value = (decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture) * 100m)
                    .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            }

            fields[$"Input.{field}"] = value;
        }

        return fields;
    }

    private async Task<HttpClient> AdminAsync()
    {
        if (!await WithDbAsync(db => db.Users.AnyAsync(u => u.Email == AdminEmail)))
        {
            await CreateConfirmedUserAsync(AdminEmail, Roles.Admin);
        }

        var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, AdminEmail)).StatusCode);
        return client;
    }
}
