using System.Net;
using System.Text.RegularExpressions;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Domain.Subscriptions;
using Local133.Domain.Webhooks;
using Local133.Domain.Pey;
using Local133.Web.Areas.Merchant.Models;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Admin;

/// <summary>6.4: tablero del admin (/admin).</summary>
public partial class DashboardTests(Local133WebFactory factory) : AdminTestBase(factory)
{
    private const string Url = "/admin/tablero";

    /// <summary>GMV de la fila de un mes en la tabla de 6 meses.</summary>
    private static string MonthGmv(string html, DateOnly month) =>
        MonthRowRegex(SaleFormats.IsoDate(month)).Match(html) is { Success: true } m ? m.Groups[1].Value.Trim() : "(sin fila)";

    private static Regex MonthRowRegex(string iso) =>
        new($@"data-dashboard-month=""{iso}"">\s*<th[^>]*>[^<]*</th>\s*<td[^>]*>\s*([^<\s]+)");

    [Fact]
    public async Task Sin_datos_se_muestra_en_ceros_y_marca_el_menu()
    {
        var html = await GetHtmlAsync(await AdminAsync(), Url);

        Assert.Contains("data-dashboard-gmv-today>$0.00</div>", html);
        Assert.Contains("data-dashboard-fair-use-empty", html);
        Assert.Contains("data-dashboard-conversion data-ended=\"0\" data-paid=\"0\"", html);
        Assert.DoesNotContain("data-dashboard-webhook-alert", html);
        Assert.Matches(new Regex("class=\"nav-link admin-nav-link active\" href=\"/admin/tablero\""), html);
        Assert.Contains("data-dashboard-refresh", html);
        Assert.DoesNotContain("data-coming-soon", html);
    }

    [Fact]
    public async Task Gmv_del_dia_y_del_mes_en_hora_de_Mexico_y_de_todos_los_vendedores()
    {
        // Hoy a las 9:00 de México: cerca de la hora real, para no invalidar la sesión (ver TestClock).
        var now = Factory.Clock.FreezeAtMexicoNine();
        var today = MexicoTime.Today(now);
        var todayStart = MexicoTime.DayRange(today).StartUtc;
        var monthStart = MexicoTime.MonthRange(today).StartUtc;
        var yesterdayInMonth = today.Day > 1;
        await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);

        // Una venta con dos pagos hoy: cuenta una vez en las ventas.
        var twoPayments = await InsertSaleAsync(Email, 150m, todayStart.AddMinutes(20));
        await AddPaymentAsync(twoPayments, 100m, todayStart.AddMinutes(30));
        await AddPaymentAsync(twoPayments, 50m, todayStart.AddHours(8));
        // Ayer a las 23:30 de México (ya es hoy en UTC): no es de hoy; es del mes salvo el día 1. De otro vendedor.
        var yesterday = await InsertSaleAsync(OtherEmail, 20m, todayStart.AddHours(-1));
        await AddPaymentAsync(yesterday, 20m, todayStart.AddMinutes(-30));
        // 31 de octubre a las 23:30 de México: del mes anterior.
        var lastMonth = await InsertSaleAsync(Email, 7m, monthStart.AddHours(-1));
        await AddPaymentAsync(lastMonth, 7m, monthStart.AddMinutes(-30));
        var firstOfMonth = await InsertSaleAsync(Email, 3m, monthStart);
        await AddPaymentAsync(firstOfMonth, 3m, monthStart.AddMinutes(10));
        // Hace 5 meses (la fila más vieja) y hace 6 (fuera de la tabla).
        var fiveMonthsAgo = MexicoTime.MonthRange(MexicoTime.MonthStart(today).AddMonths(-5)).StartUtc;
        var oldest = await InsertSaleAsync(Email, 11m, fiveMonthsAgo);
        await AddPaymentAsync(oldest, 11m, fiveMonthsAgo.AddHours(1));
        var outside = await InsertSaleAsync(Email, 13m, fiveMonthsAgo.AddDays(-3));
        await AddPaymentAsync(outside, 13m, fiveMonthsAgo.AddMinutes(-1));

        var html = await GetHtmlAsync(await AdminAsync(), Url);

        var month = MexicoTime.MonthStart(today);
        // El día 1, el pago de las 00:10 del día 1 también es de hoy.
        Assert.Contains($"data-dashboard-gmv-today>{(yesterdayInMonth ? "$150.00" : "$153.00")}</div>", html);
        Assert.Contains($"data-dashboard-gmv-month>{(yesterdayInMonth ? "$173.00" : "$153.00")}</div>", html);
        Assert.Contains($"data-dashboard-gmv-month-sales=\"{(yesterdayInMonth ? 3 : 2)}\"", html);
        Assert.Contains($"data-dashboard-gmv-previous>{(yesterdayInMonth ? "$7.00" : "$27.00")}</div>", html);
        Assert.Contains($"data-dashboard-month=\"{SaleFormats.IsoDate(month)}\"", html);
        Assert.Contains($"data-dashboard-month=\"{SaleFormats.IsoDate(month.AddMonths(-5))}\"", html);
        Assert.DoesNotContain($"data-dashboard-month=\"{SaleFormats.IsoDate(month.AddMonths(-6))}\"", html);
        Assert.Equal("$11.00", MonthGmv(html, month.AddMonths(-5)));
        Assert.Equal(yesterdayInMonth ? "$173.00" : "$153.00", MonthGmv(html, month));
        Assert.DoesNotContain("$13.00", html);
    }

    [Fact]
    public async Task Ingresos_de_la_suscripcion_con_abono_a_mano_e_iva()
    {
        var now = Factory.Clock.FreezeAtMexicoNine();
        var monthStart = MexicoTime.MonthRange(MexicoTime.Today(now)).StartUtc;
        await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await InsertChargeAsync(Email, 199m, monthStart, paidAt: monthStart.AddDays(2));
        await InsertChargeAsync(Email, 149m, monthStart, paidAt: monthStart.AddDays(3), manual: true, plan: SubscriptionPlan.Basic);
        await InsertChargeAsync(Email, 199m, monthStart.AddDays(-5), paidAt: monthStart.AddDays(-3));
        await InsertChargeAsync(Email, 199m, monthStart.AddDays(4)); // pendiente: no cuenta
        await InsertChargeAsync(Email, 199m, monthStart.AddDays(4), status: SubscriptionChargeStatus.Partial);

        var html = await GetHtmlAsync(await AdminAsync(), Url);

        Assert.Contains("data-dashboard-revenue=\"month\">$348.00</div>", html);
        Assert.Contains("$300.00 sin IVA · IVA $48.00", html);
        Assert.Contains("data-dashboard-revenue-charges=\"2\" data-dashboard-revenue-manual=\"1\"", html);
        Assert.Contains("data-dashboard-revenue=\"previous\">$199.00</div>", html);
        // Por plan: el primero de cada uno es el de este mes.
        Assert.Contains("data-dashboard-revenue-plan=\"Basic\" data-amount=\"149.00\" data-charges=\"1\"", html);
        Assert.Contains("data-dashboard-revenue-plan=\"Pro\" data-amount=\"199.00\" data-charges=\"1\"", html);
        Assert.Contains("data-dashboard-revenue-plan=\"Basic\" data-amount=\"0\" data-charges=\"0\"", html); // mes anterior
    }

    [Fact]
    public async Task Vendedores_por_plan_y_por_estatus_con_ligas_a_clientes()
    {
        var now = DateTimeOffset.UtcNow;
        await CreateMerchantAsync("sinprueba@prueba.mx");
        await CreateMerchantAsync("prueba@prueba.mx");
        await SeedTrialAsync("prueba@prueba.mx");
        await CreateMerchantAsync("gratis@prueba.mx");
        await SeedExpiredPlanAsync("gratis@prueba.mx");
        await CreateMerchantAsync("gracia@prueba.mx");
        await SeedPlanAsync("gracia@prueba.mx", (SubscriptionPeriodKind.Trial, now.AddDays(-70), now.AddDays(-40)),
            (SubscriptionPeriodKind.Paid, now.AddDays(-40), now.AddDays(-2)));
        await CreateMerchantAsync("basico@prueba.mx");
        await SeedBasicPlanAsync("basico@prueba.mx");
        await ActiveMerchantAsync("pro@prueba.mx");
        await SeedPlanAsync("pro@prueba.mx", (SubscriptionPeriodKind.Trial, now.AddDays(-40), now.AddDays(-10)),
            (SubscriptionPeriodKind.Paid, now.AddDays(-10), now.AddDays(20)));

        var html = await GetHtmlAsync(await AdminAsync(), Url);

        foreach (var tier in Enum.GetValues<PlanTier>())
        {
            Assert.Contains($"data-plan-count=\"{tier}\" data-count=\"1\"", html);
        }

        Assert.Contains("data-status-count=\"PendingBankAccount\" data-count=\"5\"", html);
        Assert.Contains("data-status-count=\"Active\" data-count=\"1\"", html);
        Assert.Contains("data-dashboard-total-merchants=\"6\"", html);
        Assert.Contains("href=\"/admin/clientes?plan=gracia\"", html);
        Assert.Contains("href=\"/admin/clientes?plan=basico\"", html);
        Assert.Contains("href=\"/admin/clientes?estatus=activos\"", html);
    }

    [Fact]
    public async Task Conversion_de_prueba_a_pago_historica_y_reciente()
    {
        var now = DateTimeOffset.UtcNow;
        // Terminó hace 10 días y pagó.
        await CreateMerchantAsync("a@prueba.mx");
        await SeedPlanAsync("a@prueba.mx", (SubscriptionPeriodKind.Trial, now.AddDays(-40), now.AddDays(-10)));
        await InsertChargeAsync("a@prueba.mx", 99m, now.AddDays(-12), paidAt: now.AddDays(-11), plan: SubscriptionPlan.Basic); // cualquier plan cuenta
        // Terminó hace 40 días sin pagar (solo cuenta en el histórico).
        await CreateMerchantAsync("b@prueba.mx");
        await SeedPlanAsync("b@prueba.mx", (SubscriptionPeriodKind.Trial, now.AddDays(-70), now.AddDays(-40)));
        await InsertChargeAsync("b@prueba.mx", 199m, now.AddDays(-41)); // generó un link y no lo pagó
        // Terminó hace 5 días sin pagar.
        await CreateMerchantAsync("c@prueba.mx");
        await SeedPlanAsync("c@prueba.mx", (SubscriptionPeriodKind.Trial, now.AddDays(-35), now.AddDays(-5)));
        // Sigue en la prueba (ya pagó): todavía no cuenta.
        await CreateMerchantAsync("d@prueba.mx");
        await SeedTrialAsync("d@prueba.mx");
        await InsertChargeAsync("d@prueba.mx", 199m, now.AddDays(-1), paidAt: now.AddDays(-1));

        var html = await GetHtmlAsync(await AdminAsync(), Url);

        Assert.Contains("data-dashboard-conversion data-ended=\"3\" data-paid=\"1\"", html);
        Assert.Contains("33 %", html);
        Assert.Contains("data-dashboard-recent-conversion data-ended=\"2\" data-paid=\"1\"", html);
        Assert.Contains("50 %", html);
    }

    [Fact]
    public async Task Subidas_de_Basico_a_Pro_en_los_ultimos_30_dias()
    {
        var now = DateTimeOffset.UtcNow;
        const SubscriptionPlan Pro = SubscriptionPlan.Pro;
        const SubscriptionPlan Basic = SubscriptionPlan.Basic;
        // Subió hace 3 días; la conversión dejó dos tramos de Pro: cuenta una vez.
        await CreateMerchantAsync("a@prueba.mx");
        await SeedPlanAsync("a@prueba.mx",
            (SubscriptionPeriodKind.Trial, Pro, now.AddDays(-30), now.AddDays(-15)),
            (SubscriptionPeriodKind.Paid, Basic, now.AddDays(-15), now.AddDays(-3)),
            (SubscriptionPeriodKind.Conversion, Pro, now.AddDays(-3), now.AddDays(5)),
            (SubscriptionPeriodKind.Conversion, Pro, now.AddDays(-3).AddSeconds(1), now.AddDays(5).AddSeconds(1)));
        // Subió hace 20 días.
        await CreateMerchantAsync("b@prueba.mx");
        await SeedPlanAsync("b@prueba.mx",
            (SubscriptionPeriodKind.Trial, Pro, now.AddDays(-50), now.AddDays(-35)),
            (SubscriptionPeriodKind.Paid, Basic, now.AddDays(-35), now.AddDays(-20)),
            (SubscriptionPeriodKind.Conversion, Pro, now.AddDays(-20), now.AddDays(-10)));
        // Subió hace 40 días: ya no cuenta.
        await CreateMerchantAsync("c@prueba.mx");
        await SeedPlanAsync("c@prueba.mx",
            (SubscriptionPeriodKind.Trial, Pro, now.AddDays(-70), now.AddDays(-55)),
            (SubscriptionPeriodKind.Paid, Basic, now.AddDays(-55), now.AddDays(-40)),
            (SubscriptionPeriodKind.Conversion, Pro, now.AddDays(-40), now.AddDays(-30)));
        // Pagó Pro sin Básico por delante: no es subida.
        await CreateMerchantAsync("d@prueba.mx");
        await SeedPlanAsync("d@prueba.mx",
            (SubscriptionPeriodKind.Trial, Pro, now.AddDays(-20), now.AddDays(-5)),
            (SubscriptionPeriodKind.Paid, Pro, now.AddDays(-5), now.AddDays(25)));

        var html = await GetHtmlAsync(await AdminAsync(), Url);

        Assert.Contains("data-dashboard-upgrades=\"2\"", html);
    }

    [Fact]
    public async Task Lugares_de_fundador_asignados()
    {
        await CreateMerchantAsync(Email);
        await SeedTrialAsync(Email);
        var id = await MerchantIdAsync(Email);
        await WithDbAsync(async db =>
        {
            var subscription = await db.Subscriptions.SingleAsync(s => s.MerchantId == id);
            subscription.AssignFounder(1);
            await db.SaveChangesAsync();
        });

        var html = await GetHtmlAsync(await AdminAsync(), Url);

        Assert.Contains("data-dashboard-founders data-assigned=\"1\" data-slots=\"100\"", html);
        Assert.Contains("data-dashboard-founders-left=\"99\"", html);
    }

    [Fact]
    public async Task Alerta_de_webhooks_fallidos_y_atorados()
    {
        var now = DateTimeOffset.UtcNow;
        await InsertWebhookEventAsync(PeyWebhookEventTypes.CodiQrPaymentReceived, "{}", now.AddHours(-1), WebhookEventStatus.Failed);
        await InsertWebhookEventAsync(PeyWebhookEventTypes.CodiQrPaymentReceived, "{}", now.AddMinutes(-20), WebhookEventStatus.Received);
        await InsertWebhookEventAsync(PeyWebhookEventTypes.CodiQrPaymentReceived, "{}", now.AddMinutes(-2), WebhookEventStatus.Received);
        await InsertWebhookEventAsync(PeyWebhookEventTypes.CodiQrPaymentReceived, "{}", now.AddHours(-2), WebhookEventStatus.Processed);

        var html = await GetHtmlAsync(await AdminAsync(), Url);

        Assert.Contains("data-dashboard-webhook-alert", html);
        Assert.Contains("data-dashboard-failed-webhooks=\"1\"", html);
        Assert.Contains("data-dashboard-stuck-webhooks=\"1\"", html);
        Assert.Contains("href=\"/admin/webhooks?estatus=atorados\"", html);
    }

    [Fact]
    public async Task Uso_justo_lista_a_quien_lo_paso_este_mes()
    {
        await WithSettingsAsync(s => s.FairUseMonthlyPaid = 2, async () =>
        {
            var now = DateTimeOffset.UtcNow;
            await ActiveMerchantAsync(Email);
            await ActiveMerchantAsync(OtherEmail);
            for (var i = 0; i < 2; i++)
            {
                var sale = await InsertSaleAsync(Email, 10m, now.AddMinutes(-10 - i));
                await AddPaymentAsync(sale, 10m, now.AddMinutes(-5 - i));
            }

            var other = await InsertSaleAsync(OtherEmail, 10m, now.AddMinutes(-10));
            await AddPaymentAsync(other, 10m, now.AddMinutes(-5));
            var heavy = await MerchantIdAsync(Email);
            var light = await MerchantIdAsync(OtherEmail);

            var html = await GetHtmlAsync(await AdminAsync(), Url);

            Assert.Contains($"data-fair-use-merchant=\"{heavy}\"", html);
            Assert.DoesNotContain($"data-fair-use-merchant=\"{light}\"", html);
            Assert.Contains("data-dashboard-fair-use=\"1\"", html);
        });
    }

    [Fact]
    public async Task Se_guarda_cinco_minutos_y_actualizar_lo_recalcula()
    {
        var now = Factory.Clock.FreezeAtMexicoNine();
        await ActiveMerchantAsync(Email);
        var admin = await AdminAsync();
        Assert.Contains("data-dashboard-gmv-today>$0.00</div>", await GetHtmlAsync(admin, Url));

        var sale = await InsertSaleAsync(Email, 25m, now.AddMinutes(-5));
        await AddPaymentAsync(sale, 25m, now.AddMinutes(-1));
        Factory.Clock.FrozenAt = now.AddMinutes(4);
        var cached = await GetHtmlAsync(admin, Url);

        var refresh = await admin.GetAsync(Url + "?actualizar=true");
        var refreshed = await GetHtmlAsync(admin, Url);

        Assert.Contains("data-dashboard-gmv-today>$0.00</div>", cached);
        Assert.Equal(HttpStatusCode.Redirect, refresh.StatusCode);
        Assert.Equal(Url, Location(refresh));
        Assert.Contains("data-dashboard-gmv-today>$25.00</div>", refreshed);

        // Sin «Actualizar», caduca a los 5 minutos.
        var second = await InsertSaleAsync(Email, 5m, now.AddMinutes(-5));
        await AddPaymentAsync(second, 5m, now);
        Assert.Contains("data-dashboard-gmv-today>$25.00</div>", await GetHtmlAsync(admin, Url));
        Factory.Clock.FrozenAt = now.AddMinutes(4 + 5);
        Assert.Contains("data-dashboard-gmv-today>$30.00</div>", await GetHtmlAsync(admin, Url));
    }

    [Fact]
    public async Task La_entrada_del_admin_lleva_a_clientes_que_va_primero_en_el_menu()
    {
        var admin = await AdminAsync();

        var entry = await admin.GetAsync("/admin");
        var html = await GetHtmlAsync(admin, "/admin/clientes");

        Assert.Equal("/admin/clientes", Location(entry));
        Assert.True(html.IndexOf("href=\"/admin/clientes\"", StringComparison.Ordinal)
            < html.IndexOf("href=\"/admin/tablero\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_vendedor_no_entra_al_tablero()
    {
        var client = await ActiveMerchantAsync(Email);

        Assert.StartsWith("/Identity/Account/AccessDenied", Location(await client.GetAsync(Url)));
    }
}
