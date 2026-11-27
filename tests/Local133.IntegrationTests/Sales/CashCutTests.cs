using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.IntegrationTests.Infrastructure;

namespace Local133.IntegrationTests.Sales;

/// <summary>5.9: corte de caja diario (/app/ventas/corte), solo en Pro.</summary>
public class CashCutTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string Url = "/app/ventas/corte";

    [Fact]
    public async Task Suma_lo_cobrado_en_el_dia_con_su_desglose_productos_y_lo_que_quedo_sin_pagar()
    {
        var client = await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        var now = DateTimeOffset.UtcNow;
        var dayStart = MexicoTime.DayRange(MexicoTime.Today(now)).StartUtc;
        // Horas del día de hoy en México, siempre antes de ahora.
        var at = (int minutes) => dayStart.AddMinutes(minutes) < now ? dayStart.AddMinutes(minutes) : now.AddSeconds(-minutes);

        var qr = await InsertSaleAsync(Email, 100m, at(1), expiresAt: now.AddMinutes(5));
        await AddPaymentAsync(qr, 100m, at(2));
        var push = await InsertSaleAsync(Email, 50m, at(3), phone: "5512345678", expiresAt: now.AddMinutes(5));
        await AddPaymentAsync(push, 50m, at(4));
        var cart = await InsertSaleAsync(Email, 0m, at(5), expiresAt: now.AddMinutes(5),
            items: [("Taco al pastor", 20m, 2), ("Refresco", 15m, 1)]);
        await AddPaymentAsync(cart, 30m, at(6)); // dos pagos de la misma venta: cuenta una vez
        await AddPaymentAsync(cart, 25m, at(7));
        // Creada ayer y pagada hoy: cuenta hoy.
        var yesterday = await InsertSaleAsync(Email, 10m, dayStart.AddHours(-2));
        await AddPaymentAsync(yesterday, 10m, at(8));
        await InsertSaleAsync(Email, 30m, at(9), expiresAt: now.AddMinutes(5)); // pendiente
        await InsertSaleAsync(Email, 40m, at(10), expiresAt: now.AddSeconds(-1)); // vencida (sin job)
        await InsertSaleAsync(Email, 70m, at(11), status: SaleStatus.Cancelled, expiresAt: now.AddMinutes(5));
        var foreign = await InsertSaleAsync(OtherEmail, 999m, at(12));
        await AddPaymentAsync(foreign, 999m, at(13));

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains($"data-cash-cut=\"{Iso(MexicoTime.Today(now))}\"", html);
        Assert.Contains("data-cash-cut-collected>$215.00<", html);
        Assert.Contains("data-cash-cut-paid>4<", html);
        Assert.Contains("data-cash-cut-average>$53.75<", html);
        Assert.Contains("data-cash-cut-row=\"QR\" data-count=\"3\"", html);
        Assert.Contains("data-cash-cut-row=\"Celular\" data-count=\"1\"", html);
        Assert.DoesNotContain("data-cash-cut-row=\"QR fijo\"", html); // solo si hubo
        Assert.Contains("data-cash-cut-row=\"Monto\" data-count=\"3\"", html);
        Assert.Contains("data-cash-cut-row=\"Productos\" data-count=\"1\"", html);
        Assert.Matches(new Regex(@"data-cash-cut-product>\s*<td[^>]*>Taco al pastor</td>\s*<td[^>]*>2</td>\s*<td[^>]*>\$40.00</td>"), html);
        Assert.Matches(new Regex(@"data-cash-cut-product>\s*<td[^>]*>Refresco</td>\s*<td[^>]*>1</td>\s*<td[^>]*>\$15.00</td>"), html);
        Assert.True(html.IndexOf(">Taco al pastor<", StringComparison.Ordinal) < html.IndexOf(">Refresco<", StringComparison.Ordinal));
        Assert.Contains("data-cash-cut-open-row=\"pending\" data-count=\"1\"", html);
        Assert.Contains("data-cash-cut-open-row=\"expired\" data-count=\"1\"", html);
        Assert.Contains($"href=\"/app/ventas?estado=pendientes&amp;desde={Iso(MexicoTime.Today(now))}&amp;hasta={Iso(MexicoTime.Today(now))}\"", html);
        Assert.DoesNotContain("data-cash-cut-next", html); // hoy no tiene día siguiente
        Assert.Matches(new Regex("class=\"nav-link active\" href=\"/app/ventas\""), html); // pestaña «Ventas»

        // Lo cobrado hoy coincide con el resumen de Ventas.
        Assert.Contains("data-day-collected>$215.00<", await GetHtmlAsync(client, "/app/ventas"));

        // El mensaje de WhatsApp lleva el total y los más vendidos.
        var whatsApp = Regex.Match(html, "href=\"(https://wa.me/\\?text=[^\"]+)\"[^>]*data-cash-cut-whatsapp").Groups[1].Value;
        var message = Uri.UnescapeDataString(WebUtility.HtmlDecode(whatsApp)["https://wa.me/?text=".Length..]);
        Assert.StartsWith("Corte de caja · Tacos Don Ramón\n", message);
        Assert.Contains("Cobrado: $215.00 (4 ventas pagadas)", message);
        Assert.Contains("- QR: $165.00 (3)", message);
        Assert.Contains("- 2 Taco al pastor", message);
        Assert.Contains("Pendientes: 1 ($30.00)", message);
        Assert.Contains("Vencidas: 1 ($40.00)", message);
    }

    [Fact]
    public async Task Cada_pago_cuenta_en_su_dia_de_Mexico()
    {
        var client = await ActiveMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        var today = MexicoTime.Today(now);
        var yesterday = today.AddDays(-1);
        var lateNight = MexicoTime.DayRange(yesterday).EndUtc.AddMinutes(-30); // 23:30 de ayer en México
        var sale = await InsertSaleAsync(Email, 80m, lateNight.AddMinutes(-5));
        await AddPaymentAsync(sale, 80m, lateNight);

        var cutYesterday = await GetHtmlAsync(client, $"{Url}?fecha={Iso(yesterday)}");
        Assert.Contains($"data-cash-cut=\"{Iso(yesterday)}\"", cutYesterday);
        Assert.Contains("data-cash-cut-collected>$80.00<", cutYesterday);
        Assert.Contains($"href=\"{Url}\" aria-label=\"Día siguiente\" data-cash-cut-next", cutYesterday);
        Assert.Contains($"href=\"{Url}?fecha={Iso(yesterday.AddDays(-1))}\" aria-label=\"Día anterior\"", cutYesterday);

        var cutToday = await GetHtmlAsync(client, Url);
        Assert.Contains("data-cash-cut-collected>$0.00<", cutToday);
        Assert.Contains("data-cash-cut-empty", cutToday);
    }

    [Theory]
    [InlineData("2099-01-01")]
    [InlineData("ayer")]
    [InlineData("1900-01-01")]
    public async Task Una_fecha_futura_o_ilegible_muestra_hoy(string date)
    {
        var client = await ActiveMerchantAsync(Email);

        var html = await GetHtmlAsync(client, $"{Url}?fecha={date}");

        Assert.Contains($"data-cash-cut=\"{Iso(MexicoTime.Today(DateTimeOffset.UtcNow))}\"", html);
    }

    [Fact]
    public async Task Basico_ve_su_corte()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedBasicPlanAsync(Email);
        var sale = await InsertSaleAsync(Email, 80m, DateTimeOffset.UtcNow.AddMinutes(-5));
        await AddPaymentAsync(sale, 80m, DateTimeOffset.UtcNow.AddMinutes(-4));

        var html = await GetHtmlAsync(client, Url);

        Assert.DoesNotContain("data-cash-cut-locked", html);
        Assert.Contains("data-cash-cut-collected", html);
    }

    [Fact]
    public async Task El_vencido_ve_el_aviso_de_elegir_plan()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedExpiredPlanAsync(Email);
        var sale = await InsertSaleAsync(Email, 80m, DateTimeOffset.UtcNow.AddMinutes(-5));
        await AddPaymentAsync(sale, 80m, DateTimeOffset.UtcNow.AddMinutes(-4));

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("data-cash-cut-locked", html);
        Assert.Contains("data-plan-expired", html);
        Assert.DoesNotContain("data-pro-required", html);
        Assert.DoesNotContain("data-cash-cut-collected", html);
    }

    [Fact]
    public async Task Sin_sesion_redirige_al_login()
    {
        var response = await CreateClient().GetAsync(Url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", Location(response));
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
