using System.Net;
using System.Text.RegularExpressions;
using Local133.Application.Auth;
using Local133.Domain.BankAccounts;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Sales;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Identity;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Admin;

/// <summary>6.1: clientes en el admin. Listado con búsqueda y filtros, y detalle con pestañas (solo lectura).</summary>
public partial class CustomersTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string AdminEmail = "admin@prueba.mx";
    private const string ListUrl = "/admin/clientes";

    // --- Acceso -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Vendedor_no_entra_al_listado_ni_al_detalle()
    {
        var client = await ActiveMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);

        foreach (var url in new[] { ListUrl, $"{ListUrl}/{id}", $"{ListUrl}/{id}/ventas", $"{ListUrl}/resultados" })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Identity/Account/AccessDenied", Location(response));
        }
    }

    [Fact]
    public async Task Sin_sesion_el_detalle_redirige_al_login()
    {
        var response = await CreateClient().GetAsync($"{ListUrl}/1/plan");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", Location(response));
    }

    // --- Listado ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Listado_vacio_y_menu_con_clientes_activo()
    {
        var html = await GetHtmlAsync(await AdminAsync(), ListUrl);

        Assert.Contains("data-customers-empty", html);
        Assert.Matches(new Regex("class=\"nav-link admin-nav-link active\" href=\"/admin/clientes\""), html);
    }

    [Fact]
    public async Task Busca_por_nombre_negocio_correo_e_id()
    {
        await ActiveMerchantAsync(Email);
        await CreateMerchantAsync(OtherEmail);
        await UpdateMerchantAsync(OtherEmail, m => { m.BusinessName = "Jugos La Güera"; m.OwnerName = "Pedro Ruiz"; });
        var otherId = await MerchantIdAsync(OtherEmail);
        var admin = await AdminAsync();

        Assert.Equal([otherId], await RowIdsAsync(admin, "?q=Güera"));
        Assert.Equal([otherId], await RowIdsAsync(admin, "?q=Pedro"));
        Assert.Equal([otherId], await RowIdsAsync(admin, "?q=otro%40prueba"));
        Assert.Equal([otherId], await RowIdsAsync(admin, $"?q={otherId}"));
        Assert.Equal(2, (await RowIdsAsync(admin, "?q=prueba.mx")).Count);

        var none = await GetHtmlAsync(admin, $"{ListUrl}?q=nadie");
        Assert.Contains("data-customers-no-results", none);
        Assert.Contains("data-clear-search", none);
    }

    [Fact]
    public async Task Filtra_por_estatus()
    {
        await ActiveMerchantAsync(Email);
        await CreateMerchantAsync(OtherEmail);
        var activeId = await MerchantIdAsync(Email);
        var pendingId = await MerchantIdAsync(OtherEmail);
        var admin = await AdminAsync();

        Assert.Equal([activeId], await RowIdsAsync(admin, "?estatus=activos"));
        Assert.Equal([pendingId], await RowIdsAsync(admin, "?estatus=sin-cuenta"));
        Assert.Empty(await RowIdsAsync(admin, "?estatus=de-baja"));
        Assert.Equal(2, (await RowIdsAsync(admin, "?estatus=desconocido")).Count);
    }

    [Fact]
    public async Task Filtro_de_plan_coincide_con_el_estado_calculado()
    {
        var now = DateTimeOffset.UtcNow;
        var graceDays = await WithDbAsync(db => db.SystemSettings.Where(s => s.Id == SystemSettings.SingletonId).Select(s => s.GraceDays).SingleAsync());
        var trialStart = now.AddDays(-35);
        var trialEnd = now.AddDays(-5);
        var merchants = new Dictionary<string, (SubscriptionPeriodKind, DateTimeOffset, DateTimeOffset)[]?>
        {
            ["onboarding@prueba.mx"] = null,
            ["prueba@prueba.mx"] = [(SubscriptionPeriodKind.Trial, now.AddDays(-7), now.AddDays(23))],
            // Prueba con un premio encadenado: sigue en prueba hasta que el premio empiece.
            ["premio@prueba.mx"] = [(SubscriptionPeriodKind.Trial, now.AddDays(-7), now.AddDays(23)), (SubscriptionPeriodKind.ReferralReward, now.AddDays(23), now.AddDays(33))],
            // Prueba con un pago ya abonado por delante: ya es Pro.
            ["pagado@prueba.mx"] = [(SubscriptionPeriodKind.Trial, now.AddDays(-7), now.AddDays(23)), (SubscriptionPeriodKind.Paid, now.AddDays(23), now.AddDays(53))],
            ["pro@prueba.mx"] = [(SubscriptionPeriodKind.Trial, trialStart, trialEnd), (SubscriptionPeriodKind.Paid, trialEnd, now.AddDays(25))],
            ["regalo@prueba.mx"] = [(SubscriptionPeriodKind.Trial, trialStart, trialEnd), (SubscriptionPeriodKind.AdminGrant, trialEnd, now.AddDays(2))],
            ["gracia@prueba.mx"] = [(SubscriptionPeriodKind.Trial, now.AddDays(-62), now.AddDays(-32)), (SubscriptionPeriodKind.Paid, now.AddDays(-32), now.AddHours(-1))],
            ["fin-gracia@prueba.mx"] = [(SubscriptionPeriodKind.Trial, now.AddDays(-80), now.AddDays(-50)), (SubscriptionPeriodKind.Paid, now.AddDays(-50), now.AddDays(-graceDays).AddHours(-1))],
            ["vencido@prueba.mx"] = [(SubscriptionPeriodKind.Trial, now.AddDays(-40), now.AddDays(-10))],
            // Premio anulado en curso (6.2): se cortó y el regalo que seguía se recorrió hasta el corte, sin hueco.
            ["premio-cortado@prueba.mx"] = [(SubscriptionPeriodKind.Trial, now.AddDays(-40), now.AddDays(-10)), (SubscriptionPeriodKind.ReferralReward, now.AddDays(-10), now.AddHours(-1)), (SubscriptionPeriodKind.AdminGrant, now.AddHours(-1), now.AddDays(4))],
            ["premio-vencido@prueba.mx"] = [(SubscriptionPeriodKind.Trial, now.AddDays(-50), now.AddDays(-20)), (SubscriptionPeriodKind.ReferralReward, now.AddDays(-20), now.AddDays(-1))],
        };

        var expected = new Dictionary<int, PlanTier>();
        foreach (var (email, periods) in merchants)
        {
            await CreateMerchantAsync(email);
            if (periods is not null)
            {
                await SeedPlanAsync(email, periods);
            }

            var id = await MerchantIdAsync(email);
            expected[id] = periods is null
                ? PlanTier.Onboarding
                : PlanStatus.For(periods.Select(p => new SubscriptionPeriod { Kind = p.Item1, StartsAt = p.Item2, EndsAt = p.Item3 }), now, graceDays).Tier;
        }

        // Cubre todos los estados salvo Básico, que el filtro cuenta dentro de «pro» hasta la 5.17 (WithPlan v2).
        Assert.Equal(Enum.GetValues<PlanTier>().Where(t => t != PlanTier.Basic).Order(), expected.Values.Distinct().Order());
        var admin = await AdminAsync();
        foreach (var (tier, slug) in new[]
                 {
                     (PlanTier.Onboarding, "sin-prueba"), (PlanTier.Trial, "prueba"), (PlanTier.Pro, "pro"),
                     (PlanTier.Grace, "gracia"), (PlanTier.Expired, "vencido"),
                 })
        {
            var html = await GetHtmlAsync(admin, $"{ListUrl}?plan={slug}");
            var ids = RowIds(html);
            Assert.Equal(expected.Where(e => e.Value == tier).Select(e => e.Key).Order(), ids.Order());
            Assert.All(RowTiers(html), shown => Assert.Equal(tier.ToString(), shown));
        }
    }

    [Fact]
    public async Task Filtra_por_vencimiento_y_ordena_por_fecha_de_vencimiento()
    {
        var now = DateTimeOffset.UtcNow;
        await CreateMerchantAsync("cinco@prueba.mx");
        await SeedPlanAsync("cinco@prueba.mx", (SubscriptionPeriodKind.Trial, now.AddDays(-25), now.AddDays(5)));
        await CreateMerchantAsync("tres@prueba.mx");
        await SeedPlanAsync("tres@prueba.mx", (SubscriptionPeriodKind.Trial, now.AddDays(-27), now.AddDays(3)));
        await CreateMerchantAsync("veinte@prueba.mx");
        await SeedPlanAsync("veinte@prueba.mx", (SubscriptionPeriodKind.Trial, now.AddDays(-10), now.AddDays(20)));
        await CreateMerchantAsync("vencido@prueba.mx");
        await SeedFreePlanAsync("vencido@prueba.mx");
        await CreateMerchantAsync("onboarding@prueba.mx");
        var admin = await AdminAsync();
        var three = await MerchantIdAsync("tres@prueba.mx");
        var five = await MerchantIdAsync("cinco@prueba.mx");
        var twenty = await MerchantIdAsync("veinte@prueba.mx");

        Assert.Equal([three, five], await RowIdsAsync(admin, "?vence=7"));
        Assert.Equal([three, five, twenty], await RowIdsAsync(admin, "?vence=30"));
        Assert.Equal([await MerchantIdAsync("vencido@prueba.mx")], await RowIdsAsync(admin, "?vence=vencido"));
    }

    [Fact]
    public async Task Filtra_por_fecha_de_registro_en_hora_de_Mexico()
    {
        await CreateMerchantAsync(Email);
        await CreateMerchantAsync(OtherEmail);
        var today = MexicoTime.Today(DateTimeOffset.UtcNow);
        var oldDay = today.AddDays(-10);
        await UpdateMerchantAsync(OtherEmail, m => m.CreatedAt = MexicoTime.DayRange(oldDay).StartUtc.AddMinutes(1));
        var admin = await AdminAsync();
        var recentId = await MerchantIdAsync(Email);
        var oldId = await MerchantIdAsync(OtherEmail);

        Assert.Equal([oldId], await RowIdsAsync(admin, $"?desde={oldDay:yyyy-MM-dd}&hasta={oldDay:yyyy-MM-dd}"));
        Assert.Equal([recentId], await RowIdsAsync(admin, $"?desde={today.AddDays(-1):yyyy-MM-dd}"));
        // Al revés se intercambian.
        Assert.Equal([recentId, oldId], await RowIdsAsync(admin, $"?desde={today:yyyy-MM-dd}&hasta={oldDay:yyyy-MM-dd}"));
    }

    [Fact]
    public async Task Pagina_de_20_en_20_del_mas_reciente_al_mas_antiguo()
    {
        await WithDbAsync(async db =>
        {
            var start = DateTimeOffset.UtcNow.AddDays(-30);
            for (var i = 0; i < 21; i++)
            {
                var user = new ApplicationUser { UserName = $"v{i}@prueba.mx", Email = $"v{i}@prueba.mx", EmailConfirmed = true };
                db.Users.Add(user);
                // PersonName no admite dígitos: Vendedor A … Vendedor U (el más reciente es U).
                db.Merchants.Add(Merchant.Register(user.Id, start.AddHours(i), $"Vendedor {(char)('A' + i)}"));
            }

            await db.SaveChangesAsync();
        });
        var admin = await AdminAsync();

        var first = await GetHtmlAsync(admin, ListUrl);
        var second = await GetHtmlAsync(admin, $"{ListUrl}?pagina=2");

        Assert.Equal(20, RowIds(first).Count);
        Assert.Contains("Vendedor U", first);
        Assert.DoesNotContain("Vendedor A ", first);
        Assert.Single(RowIds(second));
        Assert.Contains("Vendedor A ", second);
        Assert.Contains("data-customers-count=\"21\"", first);
        Assert.Contains("href=\"/admin/clientes?pagina=2\"", first);
    }

    [Fact]
    public async Task Resultados_es_un_fragmento_con_los_mismos_filtros()
    {
        await ActiveMerchantAsync(Email);
        await CreateMerchantAsync(OtherEmail);
        var admin = await AdminAsync();

        var html = await GetHtmlAsync(admin, $"{ListUrl}/resultados?estatus=activos");

        Assert.DoesNotContain("<html", html);
        Assert.Equal([await MerchantIdAsync(Email)], RowIds(html));
    }

    // --- Detalle ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Detalle_con_las_cuatro_pestanas_y_404_si_no_existe()
    {
        await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        var id = await MerchantIdAsync(Email);
        var admin = await AdminAsync();

        var general = WebUtility.HtmlDecode(await GetHtmlAsync(admin, $"{ListUrl}/{id}"));
        Assert.Contains(OwnerName, general);
        Assert.Contains("Tacos Don Ramón", general);
        Assert.Contains(Email, general);
        Assert.Matches(new Regex("aria-current=\"page\"\\s+data-customer-tab=\"General\""), general);
        Assert.DoesNotMatch(new Regex("aria-current=\"page\"\\s+data-customer-tab=\"Plan\""), general);

        var account = await GetHtmlAsync(admin, $"{ListUrl}/{id}/cuenta");
        Assert.Contains(Clabe.Mask(TestClabe), WebUtility.HtmlDecode(account));
        Assert.DoesNotContain(TestClabe, account);
        Assert.Contains("data-codi-status=\"Active\"", account);

        var plan = await GetHtmlAsync(admin, $"{ListUrl}/{id}/plan");
        Assert.Contains("data-plan-tier=\"Trial\"", plan);
        Assert.Contains("data-days-left=\"23\"", plan);
        Assert.Contains("data-plan-periods", plan); // 6.2: periodos, cargos y acciones

        Assert.Contains("data-customer-sales-summary", await GetHtmlAsync(admin, $"{ListUrl}/{id}/ventas"));

        foreach (var suffix in new[] { "", "/cuenta", "/ventas", "/plan" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{ListUrl}/{id + 1000}{suffix}")).StatusCode);
        }
    }

    [Fact]
    public async Task Plan_en_gracia_no_muestra_dias_de_Pro()
    {
        var now = DateTimeOffset.UtcNow;
        await CreateMerchantAsync(Email);
        await SeedPlanAsync(Email,
            (SubscriptionPeriodKind.Trial, now.AddDays(-62), now.AddDays(-32)),
            (SubscriptionPeriodKind.Paid, now.AddDays(-32), now.AddDays(-2)));
        var id = await MerchantIdAsync(Email);

        var plan = await GetHtmlAsync(await AdminAsync(), $"{ListUrl}/{id}/plan");

        Assert.Contains("data-plan-tier=\"Grace\"", plan);
        Assert.DoesNotContain("data-days-left", plan);
    }

    [Fact]
    public async Task Cuenta_sin_registrar_y_plan_en_onboarding()
    {
        await CreateMerchantAsync(Email);
        var id = await MerchantIdAsync(Email);
        var admin = await AdminAsync();

        Assert.Contains("data-customer-no-account", await GetHtmlAsync(admin, $"{ListUrl}/{id}/cuenta"));
        Assert.Contains("data-plan-tier=\"Onboarding\"", await GetHtmlAsync(admin, $"{ListUrl}/{id}/plan"));
    }

    [Fact]
    public async Task Ventas_solo_del_vendedor_con_filtro_de_estatus_y_celular_enmascarado()
    {
        var client = await ActiveMerchantAsync(Email);
        var paid = await ChargeAsync(client, concept: "Venta pagada");
        await UpdateSaleAsync(paid.Id, s => s.MarkPaid(DateTimeOffset.UtcNow));
        var pending = await ChargeByPhoneAsync(client, "55 1234 5678");
        var other = await ActiveMerchantAsync(OtherEmail);
        var otherSale = await ChargeAsync(other, concept: "Venta de otro");
        var id = await MerchantIdAsync(Email);
        var admin = await AdminAsync();

        var all = WebUtility.HtmlDecode(await GetHtmlAsync(admin, $"{ListUrl}/{id}/ventas"));
        var onlyPaid = await GetHtmlAsync(admin, $"{ListUrl}/{id}/ventas?estado=pagadas");

        Assert.Equal([pending.Id, paid.Id], SaleRowIds(all));
        Assert.DoesNotContain(otherSale.Id.ToString(), string.Join(",", SaleRowIds(all)));
        Assert.Contains("Venta pagada", all);
        Assert.DoesNotContain("5512345678", all);
        Assert.Contains(PhoneNumber.Mask("5512345678"), all);
        Assert.Equal([paid.Id], SaleRowIds(onlyPaid));
    }

    // --- Ayudas -------------------------------------------------------------------------------------------------------

    private async Task<HttpClient> AdminAsync()
    {
        await CreateConfirmedUserAsync(AdminEmail, Roles.Admin);
        var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, AdminEmail)).StatusCode);
        return client;
    }

    private static async Task<List<int>> RowIdsAsync(HttpClient client, string query) =>
        RowIds(await GetHtmlAsync(client, ListUrl + query));

    private static List<int> RowIds(string html) =>
        RowIdRegex().Matches(html).Select(m => int.Parse(m.Groups[1].Value)).ToList();

    private static List<string> RowTiers(string html) =>
        RowTierRegex().Matches(html).Select(m => m.Groups[1].Value).ToList();

    private static List<int> SaleRowIds(string html) =>
        SaleRowRegex().Matches(html).Select(m => int.Parse(m.Groups[1].Value)).ToList();

    [GeneratedRegex("data-customer-row=\"(\\d+)\"")]
    private static partial Regex RowIdRegex();

    [GeneratedRegex("data-customer-row=\"\\d+\" data-status=\"\\w+\" data-plan-tier=\"(\\w+)\"")]
    private static partial Regex RowTierRegex();

    [GeneratedRegex("data-sale-row=\"(\\d+)\"")]
    private static partial Regex SaleRowRegex();
}
